import hmac
import json
import math
import os
import shutil
import tempfile
import threading
import time
from pathlib import Path
from typing import Any

import numpy as np
from fastapi import Depends, FastAPI, File, Form, Header, HTTPException, UploadFile
from fastapi.responses import Response
from pydantic import BaseModel

import llm_review
import stats
import turkish_nlp

app = FastAPI(title="NeuroVox AI Service", version="0.1.0")

# Only files under AUDIO_ROOT may be analysed; the API sends bare file names.
AUDIO_ROOT = Path(os.environ.get("NEUROVOX_AUDIO_ROOT", "/data/audio")).resolve()
API_KEY = os.environ.get("NEUROVOX_AI_API_KEY", "")
_model_lock = threading.Lock()


def require_key(x_api_key: str = Header(default="")):
    # Fail closed: without a configured key the service refuses analysis/prediction.
    if not API_KEY or not hmac.compare_digest(x_api_key, API_KEY):
        raise HTTPException(status_code=401, detail="invalid api key")


def resolve_audio(name: str) -> Path:
    path = (AUDIO_ROOT / Path(name).name).resolve()
    if AUDIO_ROOT not in path.parents or not path.is_file():
        raise HTTPException(status_code=404, detail="audio file not found")
    return path

MODEL_SIZE = os.environ.get("NEUROVOX_STT_MODEL", "small")
DEVICE = os.environ.get("NEUROVOX_DEVICE", "cpu")  # "cuda" on GPU hosts (Kaggle); falls back to cpu if unusable
ALGORITHM_VERSION = "1.0"

_whisper_model = None


def get_whisper():
    global _whisper_model
    with _model_lock:
        if _whisper_model is None:
            from faster_whisper import WhisperModel
            try:
                m = WhisperModel(MODEL_SIZE, device=DEVICE, compute_type="float16" if DEVICE == "cuda" else "int8")
                if DEVICE == "cuda":  # missing cuDNN only shows up at the first transcribe
                    list(m.transcribe(np.zeros(16000, dtype=np.float32))[0])
            except Exception:
                if DEVICE == "cpu":
                    raise
                m = WhisperModel(MODEL_SIZE, device="cpu", compute_type="int8")
            _whisper_model = m
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
        "unit": unit,
        "definition_version": ALGORITHM_VERSION,
        "software_version": "neurovox-ai/0.1.0",
        "model_version": MODEL_SIZE,
        "is_candidate_annotation": True,
    }


_last_work = time.monotonic()


@app.middleware("http")
async def _track_work(request, call_next):
    global _last_work
    if request.url.path != "/health":
        _last_work = time.monotonic()
    response = await call_next(request)
    if request.url.path != "/health":
        _last_work = time.monotonic()
    return response


@app.get("/health")
def health():
    # idle_seconds lets a remote host (Kaggle) shut itself down when nobody uses it.
    return {"status": "ok", "model": MODEL_SIZE, "idle_seconds": int(time.monotonic() - _last_work)}


@app.post("/analyze", dependencies=[Depends(require_key)])
def analyze(req: AnalyzeRequest):
    return _analyze(resolve_audio(req.audio_path), req)


# Remote mode (no shared volume): the API uploads the audio bytes instead of a file name.
@app.post("/analyze-upload", dependencies=[Depends(require_key)])
def analyze_upload(file: UploadFile = File(...), language: str = Form("tr"), information_units: str | None = Form(None)):
    import json
    units = json.loads(information_units) if information_units else None
    with tempfile.TemporaryDirectory() as d:
        path = Path(d) / "audio"
        with open(path, "wb") as f:
            while chunk := file.file.read(1 << 20):
                f.write(chunk)
        return _analyze(path, AnalyzeRequest(audio_path="upload", language=language, information_units=units))


def _analyze(audio_file: Path, req: AnalyzeRequest):
    try:
        audio = decode_audio_16k(str(audio_file))
    except Exception:
        raise HTTPException(status_code=422, detail="audio could not be decoded")
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
    err_cand = turkish_nlp.error_candidates(text)   # None without a morphological analyzer

    llm = llm_review.review(text)   # None on CPU hosts / when the model is unavailable
    llm_m = []
    if llm is not None:
        per100 = 100.0 / max(lex["total_words"], 1)
        llm_m = [
            measurement("llm_grammar_error_candidates", llm["grammar"], unit="count"),
            measurement("llm_semantic_error_candidates", llm["semantic"], unit="count"),
            measurement("llm_grammar_errors_per100", round(llm["grammar"] * per100, 3), unit="per100words"),
            measurement("llm_semantic_errors_per100", round(llm["semantic"] * per100, 3), unit="per100words"),
            measurement("llm_review_details", None, text=json.dumps(llm["items"], ensure_ascii=False)[:4000]),
        ]
        for m in llm_m:
            m["model_version"] = llm_review.MODEL_NAME

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
        *[measurement(k, v, unit="ratio" if k.endswith("_ratio") else "count") for k, v in (err_cand or {}).items()],
        *llm_m,
        measurement("nlp_backend", 1.0 if turkish_nlp.ZEYREK_AVAILABLE else 0.0, text="zeyrek" if turkish_nlp.ZEYREK_AVAILABLE else "heuristic-fallback"),
    ]

    return {
        "transcript": text,
        "stt_model_version": f"faster-whisper/{MODEL_SIZE}",
        "stt_language": info.language if info else req.language,
        "speech_duration_seconds": speech_s,
        "measurements": measurement_list,
    }


class CompareRequest(BaseModel):
    features: dict[str, dict[str, list[float | None]]]


@app.post("/stats/compare-many", dependencies=[Depends(require_key)])
def compare_many(req: CompareRequest):
    if len(req.features) > 500 or sum(len(v) for g in req.features.values() for v in g.values()) > 200_000:
        raise HTTPException(status_code=413, detail="too many values")
    return {"results": stats.compare_many(req.features)}


# Training runs where the compute is (Kaggle kernel): needs train.py (neurovox_pipeline) next to this file.
@app.post("/train", dependencies=[Depends(require_key)])
def train_endpoint(file: UploadFile = File(...)):
    try:
        import pandas as pd
        import train
    except ImportError:
        raise HTTPException(status_code=501, detail="training is not available on this host")
    with tempfile.TemporaryDirectory() as d:
        csv, out = Path(d) / "in.csv", Path(d) / "art"
        with open(csv, "wb") as f:
            shutil.copyfileobj(file.file, f)
        try:
            train.train_models(pd.read_csv(csv), str(out))
        except (ValueError, AssertionError) as e:
            raise HTTPException(status_code=422, detail=str(e)[:300])
        zipped = shutil.make_archive(str(Path(d) / "art"), "zip", out)
        return Response(Path(zipped).read_bytes(), media_type="application/zip")


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


@app.post("/predict", dependencies=[Depends(require_key)])
def predict(req: PredictRequest):
    try:
        model, order = _load_model()
    except FileNotFoundError as e:
        raise HTTPException(status_code=503, detail="Model artifact not found")
    return _predict(model, order, req.features)


# Remote hosts (Kaggle) do not mount the model volume: the API sends the small model file with the request.
@app.post("/predict-upload", dependencies=[Depends(require_key)])
def predict_upload(model: UploadFile = File(...), feature_order: str = Form(...), features: str = Form(...)):
    import json
    import joblib
    try:
        feats = {k: float(v) for k, v in json.loads(features).items()}
        order = json.loads(feature_order)
        with tempfile.TemporaryDirectory() as d:
            path = Path(d) / "m.joblib"
            path.write_bytes(model.file.read(50_000_000))
            mdl = joblib.load(path)
    except Exception:
        raise HTTPException(status_code=422, detail="invalid model or features")
    return _predict(mdl, order, feats)


def _predict(model, order, features: dict):
    if not features or not all(math.isfinite(v) for v in features.values()):
        raise HTTPException(status_code=422, detail="features must be finite numbers")
    keys = order if order else sorted(features.keys())
    present = sum(1 for k in keys if k in features)
    if present < max(1, (len(keys) + 1) // 2):
        raise HTTPException(status_code=422, detail=f"only {present}/{len(keys)} model features supplied")
    # Missing values go in as NaN: the pipeline's imputer (fitted on training data) fills them, never a made-up 0.
    X = np.array([[features.get(k, np.nan) for k in keys]], dtype=float)
    proba = None
    try:
        proba = float(model.predict_proba(X)[0][1])
    except Exception:
        pass
    pred = int(model.predict(X)[0])
    return {"predicted_label": pred, "model_estimated_risk": proba, "features_used": keys}
