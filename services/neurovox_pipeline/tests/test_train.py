import json
import subprocess
import sys
from pathlib import Path

import numpy as np
import pandas as pd
import pytest

HERE = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(HERE))
import train  # noqa: E402


def make_df(n_participants=24, visits=3, seed=0):
    rng = np.random.default_rng(seed)
    rows = []
    for p in range(n_participants):
        label = p % 2
        for v in range(visits):
            rows.append({"participant_code": f"P{p}", "visit_id": f"{p}-{v}", "visit_type": "Baseline", "outcome_label": label,
                         "speech_rate": rng.normal(label, 1), "ttr": rng.normal(), "ace_total": rng.normal(-label, 1)})
    return pd.DataFrame(rows)


def test_cv_never_splits_a_participant_and_reports_all_sets(tmp_path, monkeypatch):
    seen = []
    real = train.cross_val_predict

    def spy(pipe, X, y, groups, cv, method):
        for tr, te in cv.split(X, y, groups):
            assert set(groups[tr]).isdisjoint(set(groups[te]))   # whole participants only
            seen.append(len(te))
        return real(pipe, X, y, groups=groups, cv=cv, method=method)

    monkeypatch.setattr(train, "cross_val_predict", spy)
    rep = train.train_models(make_df(), str(tmp_path))
    assert seen and rep["n_participants"] == 24 and rep["n_rows"] == 72 and rep["folds"] == 5
    assert set(rep["feature_sets"]) == {"speech", "cognitive", "combined"} and rep["primary_set"] == "combined"
    m = rep["models"]["logistic_regression"]
    assert 0 <= m["roc_auc"] <= 1 and m["roc_auc_ci95"][0] <= m["roc_auc"] <= m["roc_auc_ci95"][1]
    assert m["sensitivity"] is not None and sum(m["confusion"].values()) == 72
    # speech and ACE are both informative in the synthetic data, so combining must not be worse than speech alone
    fs = rep["feature_sets"]
    assert fs["combined"]["models"]["logistic_regression"]["roc_auc"] >= fs["speech"]["models"]["logistic_regression"]["roc_auc"] - 0.02
    assert json.loads((tmp_path / "feature_order.json").read_text()) == ["speech_rate", "ttr", "ace_total"]
    assert (tmp_path / "logistic_regression.joblib").exists() and (tmp_path / "report.json").exists()


def test_without_ace_columns_trains_speech_only(tmp_path):
    rep = train.train_models(make_df().drop(columns=["ace_total"]), str(tmp_path))
    assert rep["primary_set"] == "speech" and set(rep["feature_sets"]) == {"speech"}


def test_rejects_unusable_data(tmp_path):
    with pytest.raises(ValueError):
        train.train_models(make_df().assign(outcome_label=1), str(tmp_path))          # one class
    with pytest.raises(ValueError):
        train.train_models(make_df(n_participants=2, visits=1), str(tmp_path))        # a class has < 2 participants
    with pytest.raises(ValueError):
        train.train_models(pd.DataFrame({"a": [1]}), str(tmp_path))                   # missing columns


def test_cli_runs(tmp_path):
    csv = tmp_path / "x.csv"
    make_df().to_csv(csv, index=False)
    subprocess.run([sys.executable, str(HERE / "train.py"), "--features", str(csv), "--out", str(tmp_path / "art")], check=True, capture_output=True)
    assert (tmp_path / "art" / "report.json").exists()
