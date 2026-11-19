import sys
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import diarize


def test_cut_removes_ranges_and_keeps_a_gap():
    a = np.ones(16000 * 10, dtype=np.float32)          # 10 s
    out = diarize.cut(a, [(2, 4), (3, 5), (8, 9)])      # overlapping ranges merge: removes 2-5 and 8-9
    assert abs(len(out) / 16000 - (10 - 3 - 1 + 2 * 0.3)) < 1e-3   # 6 s kept + a 0.3 s gap per cut


def test_cut_nothing_and_everything():
    a = np.ones(16000, dtype=np.float32)
    assert len(diarize.cut(a, [])) == len(a)
    assert len(diarize.cut(a, [(0, 1)])) == 0


def test_two_speakers_cluster_and_longer_talker_is_doctor():
    rng = np.random.default_rng(0)
    a, b = rng.normal(size=64), rng.normal(size=64)
    emb = np.stack([a + rng.normal(scale=.05, size=64) for _ in range(6)] + [b + rng.normal(scale=.05, size=64) for _ in range(2)])
    labels = diarize.cluster_two(emb)
    assert labels is not None
    turns = [(i * 3.0, i * 3.0 + 2.0) for i in range(8)]   # equal-length turns: 6 of speaker a, 2 of speaker b
    assert diarize.doctor_ranges(turns, labels) == turns[:6]


def test_one_speaker_or_too_few_turns_is_not_split():
    rng = np.random.default_rng(1)
    a = rng.normal(size=64)
    assert diarize.cluster_two(np.stack([a + rng.normal(scale=.01, size=64) for _ in range(8)])) is None
    assert diarize.cluster_two(np.stack([a, -a])) is None
