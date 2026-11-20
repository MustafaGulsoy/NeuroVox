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


def test_two_speakers_cluster_and_first_speaker_is_doctor():
    rng = np.random.default_rng(0)
    a, b = rng.normal(size=64), rng.normal(size=64)
    emb = np.stack([a + rng.normal(scale=.05, size=64) for _ in range(3)] + [b + rng.normal(scale=.05, size=64) for _ in range(5)])
    labels = diarize.cluster_two(emb)
    assert labels is not None
    spans = [(i * 1.0, i * 1.0 + 1.0) for i in range(8)]   # speaker a talks first (3 s), speaker b afterwards (5 s): a is the doctor
    assert diarize.doctor_ranges(spans, labels) == [(0.0, 3.0)]   # touching windows are joined


def test_doctor_with_most_of_the_speech_is_distrusted():
    labels = np.array([0, 0, 0, 0, 0, 0, 1, 1])   # the first speaker holds 75% -> probably the wrong role
    assert diarize.doctor_ranges([(i * 1.0, i * 1.0 + 1.0) for i in range(8)], labels) == []


def test_windows_split_long_turns_only():
    assert diarize.windows([(0, 0.8), (1, 4)]) == [(0, 0.8), (1, 2), (2, 3), (3, 4)]


def test_one_speaker_or_too_few_turns_is_not_split():
    rng = np.random.default_rng(1)
    a = rng.normal(size=64)
    assert diarize.cluster_two(np.stack([a + rng.normal(scale=.01, size=64) for _ in range(8)])) is None
    assert diarize.cluster_two(np.stack([a, -a])) is None
