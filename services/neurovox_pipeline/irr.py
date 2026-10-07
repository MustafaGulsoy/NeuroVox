"""Inter-rater reliability helpers.

Categorical: Cohen's kappa (2 raters), Fleiss' kappa (>2).
Ordinal: weighted kappa.
Continuous: ICC(2,1) two-way random, single measurement, absolute agreement.
"""

from __future__ import annotations

import numpy as np
import pandas as pd


def cohens_kappa(rater_a, rater_b) -> float:
    a = np.asarray(rater_a)
    b = np.asarray(rater_b)
    if a.shape != b.shape:
        raise ValueError("Raters must have equal-length ratings")
    labels = np.unique(np.concatenate([a, b]))
    po = float(np.mean(a == b))
    pe = sum((np.mean(a == l)) * (np.mean(b == l)) for l in labels)
    return (po - pe) / (1 - pe) if (1 - pe) > 0 else 0.0


def fleiss_kappa(ratings: pd.DataFrame) -> float:
    """ratings: one row per subject, one column per rater (categorical labels)."""
    raters = ratings.columns
    categories = np.unique(ratings.to_numpy())
    n = len(ratings)
    k = len(raters)
    count = pd.DataFrame(0, index=ratings.index, columns=categories)
    for r in raters:
        for cat in categories:
            count[cat] = (ratings[r] == cat).astype(int)
    p_j = count.sum(axis=0) / (n * k)
    P_i = ((count ** 2).sum(axis=1) - k) / (k * (k - 1))
    P_bar = P_i.mean()
    P_e = float((p_j ** 2).sum())
    return (P_bar - P_e) / (1 - P_e) if (1 - P_e) > 0 else 0.0


def weighted_kappa(rater_a, rater_b, weights: str = "quadratic") -> float:
    from sklearn.metrics import cohen_kappa_score
    return float(cohen_kappa_score(np.asarray(rater_a), np.asarray(rater_b), weights=weights))


def icc_two_way_random_single(raters: pd.DataFrame) -> float:
    """ICC(2,1), absolute agreement. raters: rows=subjects, cols=raters (numeric)."""
    X = raters.to_numpy(dtype=float)
    n, k = X.shape
    row_means = X.mean(axis=1)
    col_means = X.mean(axis=0)
    grand = X.mean()
    ss_rows = k * ((row_means - grand) ** 2).sum()
    ss_cols = n * ((col_means - grand) ** 2).sum()
    ss_total = ((X - grand) ** 2).sum()
    ss_error = ss_total - ss_rows - ss_cols
    ms_rows = ss_rows / (n - 1)
    ms_cols = ss_cols / (k - 1)
    ms_error = ss_error / ((n - 1) * (k - 1))
    denom = ms_rows + (k - 1) * ms_error + k * (ms_cols - ms_error) / n
    return float((ms_rows - ms_error) / denom) if denom != 0 else 0.0
