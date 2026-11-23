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


def find_doctor_segments(audio: np.ndarray, sr: int = SR) -> list[dict] | None:
    """[{start,end}] of the detected doctor, [] if one speaker / unsure, None if detection is unavailable or failed."""
    try:
        spans = windows(speech_turns(audio, sr))
        if len(spans) < 4:
            return []
        labels = cluster_two(_embed(audio, spans, sr))
        if labels is None:
            return []
        return [{"start": round(s, 2), "end": round(e, 2)} for s, e in doctor_ranges(spans, labels)]
    except Exception as e:   # optional feature: any failure means "analyse the whole recording" as before
        import sys
        print(f"diarize failed: {type(e).__name__}: {e}", file=sys.stderr)
        return None
