"""NeuroVox training pipeline.

Leakage rules enforced here:
- A participant's visits are never split between train and test (StratifiedGroupKFold on participant_code).
- Imputation/scaling are fitted inside a sklearn Pipeline on the TRAIN folds only.
- Reported metrics come from out-of-fold predictions of a grouped, stratified k-fold -- with ~50 participants a single
  80/20 split is too noisy -- plus a participant-level bootstrap 95% CI for the AUC.

Feature sets (so the contribution of speech can be judged against cognition, as the study plan asks):
  speech    = automatic speech/language measurements
  cognitive = ACE-III scores (columns starting with "ace_")
  combined  = both
Artifacts (<model>.joblib, feature_order.json) are written for the primary set: combined when ACE columns exist,
otherwise speech.
"""

import argparse
import json
import os

import joblib
import numpy as np
import pandas as pd
from sklearn.ensemble import GradientBoostingClassifier, RandomForestClassifier
from sklearn.impute import SimpleImputer
from sklearn.linear_model import LogisticRegression, LogisticRegressionCV
from sklearn.metrics import accuracy_score, confusion_matrix, f1_score, roc_auc_score
from sklearn.model_selection import StratifiedGroupKFold, cross_val_predict
from sklearn.pipeline import Pipeline
from sklearn.preprocessing import StandardScaler
from sklearn.svm import SVC

TARGET_COLUMN = "outcome_label"
GROUP_COLUMN = "participant_code"
META_COLUMNS = [TARGET_COLUMN, GROUP_COLUMN, "visit_id", "visit_type"]
BOOTSTRAPS = 500

MODELS = {
    "logistic_regression": LogisticRegression(max_iter=2000, class_weight="balanced"),
    "elastic_net": LogisticRegressionCV(
        Cs=10, penalty="elasticnet", solver="saga", l1_ratios=[0.5], max_iter=5000, class_weight="balanced"),
    "svm": SVC(kernel="rbf", probability=True, class_weight="balanced"),
    "random_forest": RandomForestClassifier(n_estimators=300, class_weight="balanced_subsample", random_state=42),
    "gradient_boosting": GradientBoostingClassifier(random_state=42),
}


def build_pipeline(model) -> Pipeline:
    return Pipeline([
        ("imputer", SimpleImputer(strategy="median")),
        ("scaler", StandardScaler()),
        ("model", model),
    ])


def auc_ci(y, proba, groups, seed=42):
    """95% CI of the AUC, resampling PARTICIPANTS (visits of one person are correlated)."""
    rng = np.random.default_rng(seed)
    by_group = {g: np.flatnonzero(groups == g) for g in np.unique(groups)}
    keys = list(by_group)
    vals = []
    for _ in range(BOOTSTRAPS):
        idx = np.concatenate([by_group[k] for k in rng.choice(keys, len(keys))])
        if len(set(y[idx])) == 2:
            vals.append(roc_auc_score(y[idx], proba[idx]))
    return [round(float(np.percentile(vals, 2.5)), 4), round(float(np.percentile(vals, 97.5)), 4)] if len(vals) >= 50 else None


def metrics(y, proba, groups) -> dict:
    pred = (proba >= 0.5).astype(int)
    tn, fp, fn, tp = confusion_matrix(y, pred, labels=[0, 1]).ravel()
    return {
        "accuracy": round(accuracy_score(y, pred), 4),
        "f1": round(f1_score(y, pred, average="weighted"), 4),
        "roc_auc": round(roc_auc_score(y, proba), 4),
        "roc_auc_ci95": auc_ci(y, proba, groups),
        "sensitivity": round(tp / (tp + fn), 4) if tp + fn else None,   # converters detected
        "specificity": round(tn / (tn + fp), 4) if tn + fp else None,   # stable MCI kept stable
        "confusion": {"tn": int(tn), "fp": int(fp), "fn": int(fn), "tp": int(tp)},
    }


def train_models(df: pd.DataFrame, out: str, folds: int = 5) -> dict:
    """Cross-validates every model on every feature set, writes artifacts to `out`, returns the report dict."""
    if GROUP_COLUMN not in df.columns or TARGET_COLUMN not in df.columns:
        raise ValueError(f"CSV must contain '{GROUP_COLUMN}' and '{TARGET_COLUMN}' columns")
    df = df.dropna(subset=[TARGET_COLUMN, GROUP_COLUMN]).reset_index(drop=True)
    y = df[TARGET_COLUMN].astype(int).to_numpy()
    groups = df[GROUP_COLUMN].astype(str).to_numpy()
    if set(y) != {0, 1}:
        raise ValueError("outcome_label must contain both classes 0 and 1")

    per_class_participants = min(len(set(groups[y == 0])), len(set(groups[y == 1])))
    k = min(folds, per_class_participants)
    if k < 2:
        raise ValueError(f"each class needs at least 2 participants (smallest class has {per_class_participants})")

    X_all = df.drop(columns=META_COLUMNS, errors="ignore").select_dtypes(include="number")
    ace = [c for c in X_all.columns if c.startswith("ace_")]
    speech = [c for c in X_all.columns if not c.startswith("ace_")]
    sets = {"speech": speech, "cognitive": ace, "combined": speech + ace if speech and ace else []}
    sets = {name: cols for name, cols in sets.items() if cols}
    primary = "combined" if ace and speech else ("speech" if speech else "cognitive")

    os.makedirs(out, exist_ok=True)
    cv = StratifiedGroupKFold(n_splits=k, shuffle=True, random_state=42)
    report_sets = {}
    for set_name, cols in sets.items():
        X = X_all[cols]
        res = {}
        for name, model in MODELS.items():
            pipe = build_pipeline(model)
            proba = cross_val_predict(pipe, X, y, groups=groups, cv=cv, method="predict_proba")[:, 1]
            res[name] = metrics(y, proba, groups)
            if set_name == primary:
                joblib.dump(build_pipeline(model).fit(X, y), os.path.join(out, f"{name}.joblib"))
        report_sets[set_name] = {"features": cols, "models": res}

    with open(os.path.join(out, "feature_order.json"), "w", encoding="utf-8") as f:
        json.dump(sets[primary], f, ensure_ascii=False, indent=2)

    full = {
        "n_rows": int(len(df)),
        "n_participants": int(len(set(groups))),
        "class_counts": {"0": int((y == 0).sum()), "1": int((y == 1).sum())},
        "folds": k,
        "primary_set": primary,
        "models": report_sets[primary]["models"],
        "feature_sets": report_sets,
        "note": "Exploratory research model; metrics are out-of-fold (participants never shared between train and test). "
                "Results are associated with the outcome; they do not imply causation and are not a diagnosis.",
    }
    with open(os.path.join(out, "report.json"), "w", encoding="utf-8") as f:
        json.dump(full, f, ensure_ascii=False, indent=2)
    return full


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--features", required=True, help="CSV with participant_code, outcome_label, numeric features")
    ap.add_argument("--out", default="artifacts")
    ap.add_argument("--folds", type=int, default=5)
    args = ap.parse_args()
    try:
        full = train_models(pd.read_csv(args.features), args.out, args.folds)
    except ValueError as e:
        raise SystemExit(str(e))
    print(json.dumps(full["models"], indent=2))


if __name__ == "__main__":
    main()
