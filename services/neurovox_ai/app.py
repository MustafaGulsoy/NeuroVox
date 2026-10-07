import io
import math
import os
import time
import wave
from typing import Any

import numpy as np
from fastapi import FastAPI, HTTPException
from pydantic import BaseModel

import turkish_nlp

app = FastAPI(title="NeuroVox AI Service", version="0.1.0")

MODEL_SIZE = os.environ.get("NEUROVOX_STT_MODEL", "small")
ALGORITHM_VERSION = "1.0"

_whisper_model = None


def get_whisper():
    global _whisper_model
    if _whisper_model is None:
        from faster_whisper import WhisperModel
        _whisper_model = WhisperModel(MODEL_SIZE, device="cpu", compute_type="int8")
    return _whisper_model


class AnalyzeRequest(BaseModel):
    audio_path: str
    customer_id: str | None = None
    visit_id: str | None = None
    recording_id: str | None = None
    language: str = "tr"
    information_units: list[str] | None = None


VAGUE_TOKENS = ["şey", "yani", "falan", "filan", "bir şey", "öyle", "şöyle"]


def word_access_candidates(text: str, segments: list) -> dict:
    tokens = [w.lower().strip(".,!?;:()\"'") for w in text.split() if w.strip()]
    vague_hits = sum(1 for t in tokens if t in VAGUE_TOKENS)
    repetitions = 0
    for i in range(1, len(tokens)):
        if tokens[i] == tokens[i - 1] and tokens[i]:
            repetitions += 1
    long_gaps = 0
    for i in range(1, len(segments)):
        gap = segments[i].start - segments[i - 1].end
        if gap >= 1.0:
            long_gaps += 1
    return {
        "vague_expression_candidates": float(vague_hits),
        "repetition_candidates": float(repetitions),
        "anomia_candidate_pauses": float(long_gaps),
        # Circumlocution candidate: vague words co-occurring with long pauses.
        "circumlocution_candidates": float(min(vague_hits, long_gaps)) if (vague_hits > 0 and long_gaps > 0) else 0.0,
    }


def information_unit_metrics(text: str, units: list[str] | None, total_words: int) -> dict:
    if not units:
        return {"information_coverage": None, "information_density": None}
    lowered = text.lower()
    found = sum(1 for u in units if u.lower() in lowered)
    coverage = found / len(units) if units else None
    density = found / total_words if total_words > 0 and found is not None else None
    return {"information_coverage": round(coverage, 4) if coverage is not None else None,
            "information_density": round(density, 4) if density is not None else None}


def decode_audio_16k(path: str) -> np.ndarray:
    from faster_whisper.audio import decode_audio
    return decode_audio(path, sampling_rate=16000)


def vad_pause_stats(audio: np.ndarray, sample_rate: int = 16000) -> dict[str, float]:
    try:
        import webrtcvad
        vad = webrtcvad.Vad(2)
        frame_ms = 30
        frame_len = int(sample_rate * frame_ms / 1000)
        pcm16 = (np.clip(audio, -1.0, 1.0) * 32767).astype(np.int16).tobytes()
        speech_frames = 0
        total_frames = 0
        for i in range(0, len(pcm16), frame_len * 2):
            frame = pcm16[i:i + frame_len * 2]
            if len(frame) < frame_len * 2:
                break
            total_frames += 1
            if vad.is_speech(frame, sample_rate):
                speech_frames += 1
        total_s = total_frames * 0.03
        speech_s = speech_frames * 0.03
        return {
            "speech_duration_seconds": round(speech_s, 2),
            "pause_duration_seconds": round(max(total_s - speech_s, 0.0), 2),
            "pause_ratio": round((total_s - speech_s) / total_s, 4) if total_s > 0 else 0.0,
            "total_duration_seconds": round(total_s, 2),
        }
    except ImportError:
        total_s = len(audio) / sample_rate
        return {
            "speech_duration_seconds": round(total_s, 2),
            "pause_duration_seconds": 0.0,
            "pause_ratio": 0.0,
            "total_duration_seconds": round(total_s, 2),
        }


def lexical_metrics(text: str) -> dict[str, float]:
    words = [w.lower().strip(".,!?;:()\"'") for w in text.split() if w.strip()]
    words = [w for w in words if w]
    total = len(words)
    if total == 0:
        return {"total_words": 0, "ttr": 0.0, "mattr": 0.0}
    ttr = len(set(words)) / total
    window = 25
    if total >= window:
        mattrs = []
        for i in range(0, total - window + 1):
            chunk = words[i:i + window]
            mattrs.append(len(set(chunk)) / window)
        mattr = sum(mattrs) / len(mattrs)
    else:
        mattr = ttr
    return {"total_words": total, "ttr": round(ttr, 4), "mattr": round(mattr, 4)}


def count_utterances(segments: list[Any], text: str) -> int:
    if segments:
        return len(segments)
    parts = [p for p in text.replace("!", ".").replace("?", ".").split(".") if p.strip()]
    return max(len(parts), 1)


def measurement(name: str, value: float | None, text: str | None = None, unit: str | None = None) -> dict:
    return {
        "feature_name": name,
        "numeric_value": value,
        "text_value": text,
        "definition_version": ALGORITHM_VERSION,
        "software_version": "neurovox-ai/0.1.0",
        "model_version": MODEL_SIZE,
        "is_candidate_annotation": True,
    }


@app.get("/health")
def health():
    return {"status": "ok", "model": MODEL_SIZE}


@app.post("/analyze")
def analyze(req: AnalyzeRequest):
    if not os.path.isfile(req.audio_path):
        raise HTTPException(status_code=404, detail=f"Audio file not found: {req.audio_path}")

    audio = decode_audio_16k(req.audio_path)
    total_s = len(audio) / 16000
    vad = vad_pause_stats(audio)

    model = get_whisper()
    segments_iter, info = model.transcribe(audio, language=req.language, beam_size=5)
    segments = list(segments_iter)
    text = " ".join(s.text for s in segments).strip()

    lex = lexical_metrics(text)
    utterances = count_utterances(segments, text)
    speech_s = vad["speech_duration_seconds"] or total_s

    pos_counts = turkish_nlp.analyze_pos_counts(text)
    n_sentent = turkish_nlp.sentence_count(text)
    n_clauses = turkish_nlp.clause_count_approximate(text)
    wacc = word_access_candidates(text, segments)
    info_u = information_unit_metrics(text, req.information_units, lex["total_words"])

    measurement_list = [
        measurement("total_words", lex["total_words"], unit="count"),
        measurement("total_utterances", utterances, unit="count"),
        measurement("speech_duration_seconds", speech_s, unit="seconds"),
        measurement("total_recording_duration_seconds", round(total_s, 2), unit="seconds"),
        measurement("pause_duration_seconds", vad["pause_duration_seconds"], unit="seconds"),
        measurement("pause_ratio", vad["pause_ratio"], unit="ratio"),
        measurement("speech_rate_wps", round(lex["total_words"] / speech_s, 3) if speech_s > 0 else None, unit="words/second"),
        measurement("articulation_rate_wps", round(lex["total_words"] / speech_s, 3) if speech_s > 0 else None, unit="words/second"),
        measurement("words_per_minute", round(lex["total_words"] / (total_s / 60), 2) if total_s > 0 else None, unit="wpm"),
        measurement("mean_utterance_length_words", round(lex["total_words"] / utterances, 2) if utterances > 0 else None),
        measurement("type_token_ratio", lex["ttr"], unit="ratio"),
        measurement("mattr", lex["mattr"], unit="ratio"),
        # Morphosyntax (Turkish); zeyrek when available, heuristic fallback otherwise (EXPERIMENTAL).
        measurement("sentence_count", n_sentent, unit="count"),
        measurement("clause_count", n_clauses, unit="count"),
        measurement("clause_density", round(n_clauses / n_sentent, 3), unit="clauses/sentence"),
        measurement("mean_sentence_length_words", round(lex["total_words"] / n_sentent, 2)),
        measurement("noun_ratio", round(pos_counts["noun"] / pos_counts["total"], 4) if pos_counts["total"] else None),
        measurement("verb_ratio", round(pos_counts["verb"] / pos_counts["total"], 4) if pos_counts["total"] else None),
        measurement("adjective_ratio", round(pos_counts["adjective"] / pos_counts["total"], 4) if pos_counts["total"] else None),
        measurement("adverb_ratio", round(pos_counts["adverb"] / pos_counts["total"], 4) if pos_counts["total"] else None),
        measurement("content_word_ratio", round(pos_counts["content_word"] / pos_counts["total"], 4) if pos_counts["total"] else None),
        measurement("function_word_ratio", round(pos_counts["function_word"] / pos_counts["total"], 4) if pos_counts["total"] else None),
        # Word access candidates (AI-generated candidates, NOT confirmed clinical labels).
        measurement("vague_expression_candidates", wacc["vague_expression_candidates"], unit="count"),
        measurement("repetition_candidates", wacc["repetition_candidates"], unit="count"),
        measurement("anomia_candidate_pauses", wacc["anomia_candidate_pauses"], unit="count"),
        measurement("circumlocution_candidates", wacc["circumlocution_candidates"], unit="count"),
        measurement("information_coverage", info_u["information_coverage"], unit="ratio"),
        measurement("information_density", info_u["information_density"]),
        measurement("nlp_backend", 1.0 if turkish_nlp.ZEYREK_AVAILABLE else 0.0, text="zeyrek" if turkish_nlp.ZEYREK_AVAILABLE else "heuristic-fallback"),
    ]

    return {
        "transcript": text,
        "stt_model_version": f"faster-whisper/{MODEL_SIZE}",
        "stt_language": info.language if info else req.language,
        "speech_duration_seconds": speech_s,
        "measurements": measurement_list,
    }


_MODEL_CACHE: dict = {}


class PredictRequest(BaseModel):
    features: dict[str, float]


def _load_model():
    import json
    import joblib
    model_dir = os.environ.get("NEUROVOX_MODEL_DIR", "artifacts")
    model_name = os.environ.get("NEUROVOX_MODEL_NAME", "logistic_regression")
    if model_name in _MODEL_CACHE:
        return _MODEL_CACHE[model_name]
    path = os.path.join(model_dir, f"{model_name}.joblib")
    feature_path = os.path.join(model_dir, "feature_order.json")
    if not os.path.isfile(path):
        raise FileNotFoundError(path)
    model = joblib.load(path)
    order = json.load(open(feature_path)) if os.path.isfile(feature_path) else None
    _MODEL_CACHE[model_name] = (model, order)
    return model, order


@app.post("/predict")
def predict(req: PredictRequest):
    try:
        model, order = _load_model()
    except FileNotFoundError as e:
        raise HTTPException(status_code=503, detail=f"Model artifact not found: {e.filename}")
    import numpy as np
    keys = order if order else sorted(req.features.keys())
    X = np.array([[req.features.get(k, 0.0) for k in keys]])
    proba = None
    try:
        proba = float(model.predict_proba(X)[0][1])
    except Exception:
        pass
    pred = int(model.predict(X)[0])
    return {"predicted_label": pred, "model_estimated_risk": proba, "features_used": keys}
