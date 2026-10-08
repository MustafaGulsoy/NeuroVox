import importlib
import sys
from pathlib import Path

import pytest
from fastapi.testclient import TestClient

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))


@pytest.fixture()
def app_module(tmp_path, monkeypatch):
    monkeypatch.setenv("NEUROVOX_AUDIO_ROOT", str(tmp_path))
    monkeypatch.setenv("NEUROVOX_AI_API_KEY", "secret")
    import app
    return importlib.reload(app)


def test_analyze_requires_api_key(app_module):
    c = TestClient(app_module.app)
    assert c.post("/analyze", json={"audio_path": "a.wav"}).status_code == 401
    assert c.post("/analyze", json={"audio_path": "a.wav"}, headers={"X-Api-Key": "wrong"}).status_code == 401


def test_path_traversal_is_confined_to_audio_root(app_module, tmp_path):
    outside = tmp_path.parent / "outside.wav"
    outside.write_bytes(b"x")
    c = TestClient(app_module.app)
    r = c.post("/analyze", json={"audio_path": "../outside.wav"}, headers={"X-Api-Key": "secret"})
    assert r.status_code == 404  # basename is looked up inside the root only


def test_missing_key_config_fails_closed(tmp_path, monkeypatch):
    monkeypatch.setenv("NEUROVOX_AUDIO_ROOT", str(tmp_path))
    monkeypatch.delenv("NEUROVOX_AI_API_KEY", raising=False)
    import app
    app = importlib.reload(app)
    assert TestClient(app.app).post("/analyze", json={"audio_path": "a.wav"}, headers={"X-Api-Key": ""}).status_code == 401


def test_lexical_metrics(app_module):
    m = app_module.lexical_metrics("bir bir iki üç")
    assert m["total_words"] == 4
    assert m["ttr"] == 0.75


def test_word_access_counts_repetition_and_vague(app_module):
    class Seg:
        def __init__(self, s, e): self.start, self.end = s, e
    r = app_module.word_access_candidates("şey şey ev", [Seg(0, 1), Seg(3, 4)])
    assert r["repetition_candidates"] == 1.0
    assert r["anomia_candidate_pauses"] == 1.0
    assert r["circumlocution_candidates"] == 1.0


def test_predict_without_model_is_503(app_module, tmp_path, monkeypatch):
    monkeypatch.setenv("NEUROVOX_MODEL_DIR", str(tmp_path / "none"))
    r = TestClient(app_module.app).post("/predict", json={"features": {"a": 1.0}}, headers={"X-Api-Key": "secret"})
    assert r.status_code == 503


def test_analyze_upload_requires_key_and_rejects_garbage(app_module):
    c = TestClient(app_module.app)
    f = {"file": ("a.wav", b"not audio")}
    assert c.post("/analyze-upload", files=f).status_code == 401
    assert c.post("/analyze-upload", files=f, headers={"X-Api-Key": "secret"}).status_code == 422


def test_train_endpoint_returns_artifact_zip(app_module, monkeypatch):
    pytest.importorskip("pandas")
    pytest.importorskip("sklearn")
    import io, zipfile
    sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "neurovox_pipeline"))
    rows = ["participant_code,visit_id,visit_type,outcome_label,f1,f2"]
    for p in range(12):
        for v in range(2):
            rows.append(f"P{p},{p}-{v},Baseline,{p % 2},{(p % 2) + v * 0.1},{p * 0.3}")
    c = TestClient(app_module.app)
    f = {"file": ("t.csv", "\n".join(rows).encode())}
    assert c.post("/train", files=f).status_code == 401
    r = c.post("/train", files=f, headers={"X-Api-Key": "secret"})
    assert r.status_code == 200, r.text
    assert {"report.json", "feature_order.json", "logistic_regression.joblib"} <= set(zipfile.ZipFile(io.BytesIO(r.content)).namelist())
    bad = c.post("/train", files={"file": ("t.csv", b"a,b\n1,2\n")}, headers={"X-Api-Key": "secret"})


def test_predict_imputes_missing_features_but_needs_at_least_half(app_module, tmp_path, monkeypatch):
    pytest.importorskip("sklearn")
    pd = pytest.importorskip("pandas")
    sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "neurovox_pipeline"))
    import numpy as np
    import train
    rng = np.random.default_rng(1)
    rows = [{"participant_code": f"P{p}", "outcome_label": p % 2, "f1": rng.normal(p % 2), "f2": rng.normal(), "ace_total": rng.normal(-(p % 2))}
            for p in range(16) for _ in range(2)]
    train.train_models(pd.DataFrame(rows), str(tmp_path / "art"))
    monkeypatch.setenv("NEUROVOX_MODEL_DIR", str(tmp_path / "art"))
    c = TestClient(app_module.app)
    h = {"X-Api-Key": "secret"}
    ok = c.post("/predict", json={"features": {"f1": 1.0, "f2": 0.0, "ace_total": -1.0}}, headers=h)
    assert ok.status_code == 200 and 0 <= ok.json()["model_estimated_risk"] <= 1
    partial = c.post("/predict", json={"features": {"f1": 1.0, "ace_total": -1.0}}, headers=h)   # 2 of 3 -> imputed
    assert partial.status_code == 200
    assert c.post("/predict", json={"features": {"f1": 1.0}}, headers=h).status_code == 422       # 1 of 3 -> refused
