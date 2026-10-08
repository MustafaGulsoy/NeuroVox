import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import stats  # noqa: E402
import turkish_nlp  # noqa: E402


def test_benjamini_hochberg_known_values():
    assert stats.bh_adjust([0.01, 0.04, 0.03]) == [0.03, 0.04, 0.04]
    assert stats.bh_adjust([None, 0.5]) == [None, 0.5]


def test_mann_whitney_is_chosen_for_non_normal_and_effect_is_exact():
    r = stats.compare_two([1, 2, 3], [4, 5, 6, 7])
    assert r["status"] == "ok" and r["test"] == "mann_whitney_u" or r["test"] == "welch_t"
    # fully separated groups: U = 0 -> rank-biserial = -1
    skew = stats.compare_two([1, 1, 1, 2, 30], [40, 41, 42, 90, 400])
    assert skew["test"] == "mann_whitney_u"
    assert skew["statistic"] == 0 and skew["effect"] == -1.0


def test_welch_t_for_normal_groups_and_direction_of_effect():
    a = [10.1, 9.8, 10.4, 10.0, 9.9, 10.2, 10.3, 9.7]
    b = [12.0, 12.2, 11.8, 12.1, 11.9, 12.3, 12.0, 11.7]
    r = stats.compare_two(a, b)
    assert r["test"] == "welch_t" and r["p"] < 0.001
    assert r["effect_name"] == "cohen_d" and r["effect"] < -5   # a is lower than b


def test_too_few_values_is_reported_not_computed():
    r = stats.compare_two([1, 2], [3, 4, 5])
    assert r["status"] == "insufficient" and "p" not in r


def test_constant_group_does_not_crash():
    r = stats.compare_two([5, 5, 5, 5], [1, 2, 3, 4])
    assert r["status"] == "ok" and r["shapiro_p"]["a"] is None and r["test"] == "mann_whitney_u"


@pytest.mark.skipif(not turkish_nlp.ZEYREK_AVAILABLE, reason="needs zeyrek")
def test_pos_counts_use_real_analyses():
    c = turkish_nlp.analyze_pos_counts("Çocuk yemek yapıyor")
    assert c["total"] == 3 and c["verb"] >= 1 and c["function_word"] == 0


@pytest.mark.skipif(not turkish_nlp.ZEYREK_AVAILABLE, reason="needs zeyrek")
def test_error_candidates():
    e = turkish_nlp.error_candidates("Bir kadın bir kadın yemek yapıyor. Mutfakta çocuk şey şey düşüyor. asdfgh qwrtp mutfak")
    assert e["self_correction_candidates"] >= 1
    assert e["filler_count"] >= 2
    assert e["unanalyzable_word_candidates"] >= 2
    assert e["verbless_sentence_candidates"] == 1   # the last, verb-less 3-word sentence
    assert turkish_nlp.error_candidates("") ["unanalyzable_word_ratio"] == 0.0


def test_stats_endpoint_requires_key(tmp_path, monkeypatch):
    import importlib
    from fastapi.testclient import TestClient
    monkeypatch.setenv("NEUROVOX_AUDIO_ROOT", str(tmp_path))
    monkeypatch.setenv("NEUROVOX_AI_API_KEY", "secret")
    import app
    app = importlib.reload(app)
    c = TestClient(app.app)
    body = {"features": {"f": {"a": [1, 2, 3, 4], "b": [5, 6, 7, 8]}}}
    assert c.post("/stats/compare-many", json=body).status_code == 401
    r = c.post("/stats/compare-many", json=body, headers={"X-Api-Key": "secret"})
    assert r.status_code == 200 and r.json()["results"][0]["feature"] == "f"
