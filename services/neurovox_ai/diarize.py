"""Doctor/patient separation: find the interviewer's speech so it can be cut out before transcription.

Pipeline: VAD speech turns -> ~1 s windows -> ECAPA speaker embedding per window (speechbrain, public model, no token)
-> 2 clusters -> the speaker of the first window is the doctor (the examiner opens the recording with the instruction).
Everything optional: no webrtcvad/speechbrain/model -> None (nothing is cut, analysis runs on the whole recording).
Tuned on one real interview (precision 0.89 / recall 0.86 against therapist-marked doctor ranges); therapists correct per recording in the UI.
"""
from __future__ import annotations

import os

import numpy as np

SR = 16000
WINDOW_S = 1.0          # embedding window; longer windows blur speaker changes, shorter ones are too noisy
MAX_DOCTOR_SHARE = 0.6  # a "doctor" holding more than this share of the speech is probably the wrong role: cut nothing


def cut(audio: np.ndarray, ranges: list[tuple[float, float]], gap_s: float = 0.3, sr: int = SR) -> np.ndarray:
    """Audio without `ranges` (seconds). A short silence is put where something was cut so neighbouring words do not run together."""
    n = len(audio)
    spans = sorted((max(0, int(s * sr)), min(n, int(e * sr))) for s, e in ranges if e > s)
    merged: list[list[int]] = []
    for s, e in spans:
        if merged and s <= merged[-1][1]:
            merged[-1][1] = max(merged[-1][1], e)
        else:
            merged.append([s, e])
    gap = np.zeros(int(gap_s * sr), dtype=audio.dtype)
    out, pos = [], 0
    for s, e in merged:
        if s > pos:
            out.append(audio[pos:s])
        if gap.size and out:   # ponytail: each cut adds gap_s of silence, which the pause metrics count; fine for a few cuts
            out.append(gap)
        pos = e
    if pos < n:
        out.append(audio[pos:])
    return np.concatenate(out) if out else audio[:0]


def speech_turns(audio: np.ndarray, sr: int = SR, merge_gap: float = 0.6, min_len: float = 0.5) -> list[tuple[float, float]]:
    """Speech stretches from webrtcvad; gaps shorter than merge_gap are bridged. [] when webrtcvad is missing."""
    try:
        import webrtcvad
    except ImportError:
        return []
    vad = webrtcvad.Vad(2)
    frame = int(sr * 0.03)
    pcm = (np.clip(audio, -1.0, 1.0) * 32767).astype(np.int16).tobytes()
    turns: list[list[float]] = []
    for i in range(0, len(pcm) - frame * 2 + 1, frame * 2):
        if not vad.is_speech(pcm[i:i + frame * 2], sr):
            continue
        t = i / 2 / sr
        if turns and t - turns[-1][1] <= merge_gap:
            turns[-1][1] = t + 0.03
        else:
            turns.append([t, t + 0.03])
    return [(s, e) for s, e in turns if e - s >= min_len]


def windows(turns: list[tuple[float, float]], win: float = WINDOW_S) -> list[tuple[float, float]]:
    """Split each turn into equal pieces of about `win` seconds (a turn shorter than 1.5 win stays whole)."""
    out = []
    for s, e in turns:
        k = max(1, int((e - s) // win))
        step = (e - s) / k
        out += [(s + i * step, s + (i + 1) * step) for i in range(k)]
    return out


def cluster_two(emb: np.ndarray, min_sep: float = 0.2) -> np.ndarray | None:
    """Split embeddings into two speakers; None when they do not clearly separate (one speaker, or too few windows)."""
    if len(emb) < 4:
        return None
    from scipy.cluster.hierarchy import fcluster, linkage
    labels = fcluster(linkage(emb, "average", metric="cosine"), 2, "maxclust") - 1
    if len(set(labels)) < 2:
        return None
    c0, c1 = emb[labels == 0].mean(axis=0), emb[labels == 1].mean(axis=0)
    dist = 1 - float(np.dot(c0, c1) / (np.linalg.norm(c0) * np.linalg.norm(c1) + 1e-9))
    return labels if dist >= min_sep else None


def doctor_ranges(spans: list[tuple[float, float]], labels: np.ndarray, join_gap: float = 0.3) -> list[tuple[float, float]]:
    """Ranges of the speaker who talks first; neighbouring windows of that speaker are joined.
    [] if that speaker holds more than MAX_DOCTOR_SHARE of the speech (then the role is probably the other way round)."""
    doctor = labels[0]
    total = sum(e - s for s, e in spans)
    mine = [sp for sp, l in zip(spans, labels) if l == doctor]
    if total <= 0 or sum(e - s for s, e in mine) / total > MAX_DOCTOR_SHARE:
        return []
    out: list[list[float]] = []
    for s, e in mine:
        if out and s - out[-1][1] <= join_gap:
            out[-1][1] = e
        else:
            out.append([s, e])
    return [(s, e) for s, e in out]


_encoder = None


def _embed(audio: np.ndarray, spans: list[tuple[float, float]], sr: int) -> np.ndarray:
    global _encoder
    import torch
    if _encoder is None:
        from speechbrain.inference.speaker import EncoderClassifier
        device = "cuda" if os.environ.get("NEUROVOX_DEVICE") == "cuda" and torch.cuda.is_available() else "cpu"
        _encoder = EncoderClassifier.from_hparams(source="speechbrain/spkrec-ecapa-voxceleb", run_opts={"device": device},
                                                  savedir=os.path.join(os.environ.get("HF_HOME", "/tmp"), "ecapa"))
    with torch.no_grad():
        emb = np.stack([_encoder.encode_batch(torch.from_numpy(np.ascontiguousarray(audio[int(s * sr):int(e * sr)])).float().unsqueeze(0))
                        .squeeze().cpu().numpy() for s, e in spans])
    return emb - emb.mean(axis=0)   # remove the recording's own channel/microphone offset


def _turns(spans: list[tuple[float, float]], labels, join_gap: float = 0.3) -> list[dict]:
    """Neighbouring spans of one speaker joined into turns; speakers numbered 0.. by first appearance."""
    order: dict = {}
    out: list[dict] = []
    for (s, e), l in sorted(zip(spans, labels), key=lambda x: x[0][0]):
        k = order.setdefault(l, len(order))
        if out and out[-1]["speaker"] == k and s - out[-1]["end"] <= join_gap:
            out[-1]["end"] = round(e, 2)
        else:
            out.append({"start": round(s, 2), "end": round(e, 2), "speaker": k})
    return out


def _ecapa_turns(audio: np.ndarray, sr: int) -> list[dict] | None:
    """Two speakers at most (clustering is tuned for doctor + patient). [] = one speaker or too little speech."""
    spans = windows(speech_turns(audio, sr))
    if len(spans) < 4:
        return []
    labels = cluster_two(_embed(audio, spans, sr))
    return [] if labels is None else _turns(spans, labels)


_pyannote = None


def _pyannote_turns(audio: np.ndarray, sr: int, n: int | None = None) -> list[dict] | None:
    """Any number of speakers (pyannote 3.1 pipeline, gated model: needs HF_TOKEN). None when not configured."""
    global _pyannote
    token = os.environ.get("HF_TOKEN")
    if not token:
        return None
    import torch
    if _pyannote is None:
        from pyannote.audio import Pipeline
        try:
            pipe = Pipeline.from_pretrained("pyannote/speaker-diarization-3.1", token=token)
        except TypeError:   # pyannote.audio < 4 spells it use_auth_token
            pipe = Pipeline.from_pretrained("pyannote/speaker-diarization-3.1", use_auth_token=token)
        if os.environ.get("NEUROVOX_DEVICE") == "cuda" and torch.cuda.is_available():
            pipe.to(torch.device("cuda"))
        _pyannote = pipe
    out = _pyannote({"waveform": torch.from_numpy(np.ascontiguousarray(audio)).float().unsqueeze(0), "sample_rate": sr}, num_speakers=n or 2)   # left free it splits one quiet speaker into several: the therapist says how many people talk
    dia = getattr(out, "exclusive_speaker_diarization", None) or getattr(out, "speaker_diarization", out)   # exclusive: nobody overlaps
    raw = [(t.start, t.end, spk) for t, _, spk in dia.itertracks(yield_label=True) if t.end - t.start >= 0.3]
    return _turns([(s, e) for s, e, _ in raw], [k for *_, k in raw])


def find_speakers(audio: np.ndarray, sr: int = SR, n: int | None = None) -> dict | None:
    """{"turns": [{start,end,speaker}], "doctor": [{start,end}], "backend": ..}; None if detection is unavailable or failed.
    doctor = the turns of the first speaker (the examiner opens the recording); the therapist picks who else is out in the UI."""
    for backend, fn in (("pyannote", _pyannote_turns), ("ecapa", _ecapa_turns)):
        try:
            turns = fn(audio, sr, n) if fn is _pyannote_turns else fn(audio, sr)
        except Exception as e:   # optional feature: a failing backend falls through to the next, then to "analyse everything"
            import sys
            print(f"diarize ({backend}) failed: {type(e).__name__}: {e}", file=sys.stderr)
            continue
        if turns is None:
            continue
        spans = [(t["start"], t["end"]) for t in turns]
        labels = np.array([t["speaker"] for t in turns])
        doctor = doctor_ranges(spans, labels, join_gap=0.3) if len(set(labels)) > 1 else []
        return {"turns": turns, "doctor": [{"start": round(s, 2), "end": round(e, 2)} for s, e in doctor], "backend": backend}
    return None
