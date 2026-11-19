"""Doctor/patient separation: find the interviewer's speech so it can be cut out before transcription.

Pipeline: VAD speech turns -> voice embedding per turn (resemblyzer, no gated models) -> 2 clusters ->
the cluster that talks the most is the doctor. Everything optional: no webrtcvad/resemblyzer -> None (nothing is cut).
"""
from __future__ import annotations

import numpy as np

SR = 16000
# ponytail: "talks the most = doctor" fits interviews where the patient answers briefly; therapists correct it per recording in the UI.


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


def cluster_two(emb: np.ndarray, min_sep: float = 0.2) -> np.ndarray | None:
    """Split embeddings into two speakers; None when they do not clearly separate (one speaker, or too few turns)."""
    if len(emb) < 4:
        return None
    from scipy.cluster.hierarchy import fcluster, linkage
    labels = fcluster(linkage(emb, "average", metric="cosine"), 2, "maxclust") - 1
    if len(set(labels)) < 2:
        return None
    c0, c1 = emb[labels == 0].mean(axis=0), emb[labels == 1].mean(axis=0)
    dist = 1 - float(np.dot(c0, c1) / (np.linalg.norm(c0) * np.linalg.norm(c1) + 1e-9))
    return labels if dist >= min_sep else None


def doctor_ranges(turns: list[tuple[float, float]], labels: np.ndarray) -> list[tuple[float, float]]:
    """Turns of the speaker with the larger total speaking time."""
    total = [sum(e - s for (s, e), l in zip(turns, labels) if l == k) for k in (0, 1)]
    doctor = 0 if total[0] >= total[1] else 1
    return [t for t, l in zip(turns, labels) if l == doctor]


_encoder = None


def find_doctor_segments(audio: np.ndarray, sr: int = SR) -> list[dict] | None:
    """[{start,end}] of the detected doctor, [] if one speaker, None if detection is unavailable/failed."""
    global _encoder
    try:
        turns = speech_turns(audio, sr)
        if len(turns) < 4:
            return []
        if _encoder is None:
            from resemblyzer import VoiceEncoder
            _encoder = VoiceEncoder(device="cpu")
        peak = float(np.max(np.abs(audio))) or 1.0
        emb = np.stack([_encoder.embed_utterance(audio[int(s * sr):int(e * sr)] / peak) for s, e in turns])
        labels = cluster_two(emb)
        if labels is None:
            return []
        return [{"start": round(s, 2), "end": round(e, 2)} for s, e in doctor_ranges(turns, labels)]
    except Exception:   # optional feature: any failure means "analyse the whole recording" as before
        return None
