"""Measure the speech pipeline against therapist-corrected data, so every change (model, VAD, prompt, diarization) is judged by numbers.

gold.json: [{"audio": "path.m4a", "reference": "corrected patient transcript", "doctor": [[start, end], ...]}, ...]
  reference = TranscriptText of a recording with TranscriptSource 'manual'; doctor = its manual DoctorSegments (patient-only metrics).
Run:  python eval/evaluate.py gold.json   (uses the same _analyze as production; set NEUROVOX_STT_MODEL / NEUROVOX_STT_PROMPT to compare)
Prints WER, CER (patient text) and doctor-range precision/recall (auto diarization) per recording and in total.
"""
import json
import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))


def norm(text: str) -> list[str]:
    return re.sub(r"[^\w\s]", " ", text.replace("I", "ı").replace("İ", "i").lower()).split()


def edits(a: list, b: list) -> int:
    prev = list(range(len(b) + 1))
    for i, x in enumerate(a, 1):
        cur = [i]
        for j, y in enumerate(b, 1):
            cur.append(min(prev[j] + 1, cur[j - 1] + 1, prev[j - 1] + (x != y)))
        prev = cur
    return prev[-1]


def overlap(a, b) -> float:
    return sum(max(0.0, min(x[1], y[1]) - max(x[0], y[0])) for x in a for y in b)


def main(path: str) -> None:
    import os
    os.environ.setdefault("NEUROVOX_AUDIO_ROOT", "/")
    import app
    tot = dict(we=0, wn=0, ce=0, cn=0, hit=0.0, auto=0.0, man=0.0)
    for g in json.loads(Path(path).read_text(encoding="utf-8")):
        r = app._analyze(Path(g["audio"]), app.AnalyzeRequest(audio_path=g["audio"], exclude_segments=g.get("doctor")))
        ref, hyp = norm(g["reference"]), norm(r["transcript"])
        we, ce = edits(ref, hyp), edits(list("".join(ref)), list("".join(hyp)))
        cn = len("".join(ref))
        line = f"{Path(g['audio']).name}: WER {we / max(len(ref), 1):.2f}  CER {ce / max(cn, 1):.2f}"
        tot["we"] += we; tot["wn"] += len(ref); tot["ce"] += ce; tot["cn"] += cn
        if g.get("doctor"):
            import diarize
            import numpy as np
            auto = [(d["start"], d["end"]) for d in (diarize.find_doctor_segments(app.decode_audio_16k(g["audio"])) or [])]
            a, m, hit = sum(e - s for s, e in auto), sum(e - s for s, e in g["doctor"]), overlap(auto, g["doctor"])
            tot["hit"] += hit; tot["auto"] += a; tot["man"] += m
            line += f"  doctor P {hit / a if a else 0:.2f} R {hit / m:.2f}"
        print(line)
    print(f"TOTAL: WER {tot['we'] / max(tot['wn'], 1):.3f}  CER {tot['ce'] / max(tot['cn'], 1):.3f}"
          + (f"  doctor P {tot['hit'] / tot['auto']:.2f} R {tot['hit'] / tot['man']:.2f}" if tot["man"] and tot["auto"] else ""))


if __name__ == "__main__":
    main(sys.argv[1])
