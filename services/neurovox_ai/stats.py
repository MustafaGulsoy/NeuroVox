"""Two-group comparison for the research dashboard (numbers only -- no identifiers cross this boundary).

Per feature: descriptives, Shapiro-Wilk per group, then Welch t-test when both groups look normal, otherwise
Mann-Whitney U (two-sided). Effect size: Cohen's d (t) or rank-biserial r (U). p-values are also adjusted over all
features with Benjamini-Hochberg, because comparing dozens of speech features inflates false positives.
"""

import math

import numpy as np
from scipy import stats

ALPHA = 0.05


def _num(v):
    return None if v is None or (isinstance(v, float) and not math.isfinite(v)) else float(v)


def describe(x: np.ndarray) -> dict:
    q1, med, q3 = np.percentile(x, [25, 50, 75])
    return {"n": int(len(x)), "mean": _num(x.mean()), "sd": _num(x.std(ddof=1)) if len(x) > 1 else None,
            "median": _num(med), "q1": _num(q1), "q3": _num(q3)}


def shapiro_p(x: np.ndarray):
    if len(x) < 3 or np.ptp(x) == 0:
        return None   # undefined: too few values or constant
    return _num(stats.shapiro(x).pvalue)


def compare_two(a, b) -> dict:
    a = np.asarray([v for v in a if v is not None and math.isfinite(v)], dtype=float)
    b = np.asarray([v for v in b if v is not None and math.isfinite(v)], dtype=float)
    out = {"a": describe(a) if len(a) else {"n": 0}, "b": describe(b) if len(b) else {"n": 0}}
    if len(a) < 3 or len(b) < 3:
        return {**out, "status": "insufficient", "note": "her grupta en az 3 katılımcı gerekir"}
    pa, pb = shapiro_p(a), shapiro_p(b)
    out["shapiro_p"] = {"a": pa, "b": pb}
    normal = pa is not None and pb is not None and pa > ALPHA and pb > ALPHA
    if normal:
        r = stats.ttest_ind(a, b, equal_var=False)
        pooled = math.sqrt(((len(a) - 1) * a.var(ddof=1) + (len(b) - 1) * b.var(ddof=1)) / (len(a) + len(b) - 2))
        effect = (a.mean() - b.mean()) / pooled if pooled > 0 else None
        out.update(test="welch_t", statistic=_num(r.statistic), p=_num(r.pvalue), effect_name="cohen_d", effect=_num(effect))
    else:
        r = stats.mannwhitneyu(a, b, alternative="two-sided")
        out.update(test="mann_whitney_u", statistic=_num(r.statistic), p=_num(r.pvalue), effect_name="rank_biserial",
                   effect=_num(2 * r.statistic / (len(a) * len(b)) - 1))
    out["status"] = "ok"
    return out


def bh_adjust(pvalues: list) -> list:
    """Benjamini-Hochberg FDR; None stays None."""
    idx = [i for i, p in enumerate(pvalues) if p is not None]
    adj = [None] * len(pvalues)
    ranked = sorted(idx, key=lambda i: pvalues[i])
    m, prev = len(ranked), 1.0
    for rank in range(m, 0, -1):
        i = ranked[rank - 1]
        prev = min(prev, pvalues[i] * m / rank)
        adj[i] = round(prev, 6)
    return adj


def compare_many(features: dict) -> list:
    """features: {name: {"a": [...], "b": [...]}} -> list of results sorted by p."""
    rows = [{"feature": name, **compare_two(g.get("a", []), g.get("b", []))} for name, g in features.items()]
    adj = bh_adjust([r.get("p") for r in rows])
    for r, q in zip(rows, adj):
        r["p_adj"] = q
    return sorted(rows, key=lambda r: (r.get("p") is None, r.get("p") or 1.0))
