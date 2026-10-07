"""NeuroVox training pipeline.

Leakage rules enforced here:
- A participant's visits are never split between train and test (GroupShuffleSplit).
- Imputation/scaling/feature selection are fitted inside a sklearn Pipeline on
  TRAIN data only (Pipeline handles this; never fit on the full dataset first).
"""

import argparse
import json
import os

import joblib
import pandas as pd
from sklearn.ensemble import GradientBoostingClassifier, RandomForestClassifier
from sklearn.impute import SimpleImputer
from sklearn.linear_model import LogisticRegression, LogisticRegressionCV
from sklearn.metrics import (accuracy_score, f1_score, roc_auc_score)
from sklearn.model_selection import GroupShuffleSplit
from sklearn.pipeline import Pipeline
from sklearn.preprocessing import StandardScaler
from sklearn.svm import SVC

TARGET_COLUMN = "outcome_label"
GROUP_COLUMN = "participant_code"

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


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--features", required=True, help="CSV with participant_code, outcome_label, numeric features")
    ap.add_argument("--out", default="artifacts")
    ap.add_argument("--test-size", type=float, default=0.2)
    args = ap.parse_args()

    df = pd.read_csv(args.features)
    if GROUP_COLUMN not in df.columns or TARGET_COLUMN not in df.columns:
        raise SystemExit(f"CSV must contain '{GROUP_COLUMN}' and '{TARGET_COLUMN}' columns")

    os.makedirs(args.out, exist_ok=True)
    y = df[TARGET_COLUMN]
    groups = df[GROUP_COLUMN]
    X = df.drop(columns=[TARGET_COLUMN, GROUP_COLUMN, "visit_id", "visit_type"], errors="ignore")
    X = X.select_dtypes(include="number")

    splitter = GroupShuffleSplit(n_splits=1, test_size=args.test_size, random_state=42)
    train_idx, test_idx = next(splitter.split(X, y, groups))

    assert set(groups.iloc[train_idx]).isdisjoint(set(groups.iloc[test_idx])), "Group leakage detected"

    X_train, X_test = X.iloc[train_idx], X.iloc[test_idx]
    y_train, y_test = y.iloc[train_idx], y.iloc[test_idx]

    report = {}
    for name, model in MODELS.items():
        pipe = build_pipeline(model)
        pipe.fit(X_train, y_train)
        preds = pipe.predict(X_test)
        entry = {
            "accuracy": round(accuracy_score(y_test, preds), 4),
            "f1": round(f1_score(y_test, preds, average="weighted"), 4),
        }
        try:
            proba = pipe.predict_proba(X_test)[:, 1] if len(set(y_test)) == 2 else None
            if proba is not None:
                entry["roc_auc"] = round(roc_auc_score(y_test, proba), 4)
        except Exception:
            pass
        report[name] = entry
        joblib.dump(pipe, os.path.join(args.out, f"{name}.joblib"))

    with open(os.path.join(args.out, "feature_order.json"), "w", encoding="utf-8") as f:
        json.dump(list(X.columns), f, ensure_ascii=False, indent=2)

    with open(os.path.join(args.out, "report.json"), "w", encoding="utf-8") as f:
        json.dump({
            "n_train": int(len(train_idx)),
            "n_test": int(len(test_idx)),
            "models": report,
            "note": "Exploratory research model. Results are associated with the outcome; they do not imply causation.",
        }, f, ensure_ascii=False, indent=2)

    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
