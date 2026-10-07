"""Turkish NLP helper.

Primary analyzer: zeyrek (open-source Turkish morphological analyzer).
Fallback when zeyrek is unavailable: conservative regex heuristics,
always marked EXPERIMENTAL in feature metadata.
"""

import re

_ZEYREK = None
try:
    import zeyrek
    _ZEYREK = zeyrek.MorphAnalyzer()
except Exception:
    _ZEYREK = None

ZEYREK_AVAILABLE = _ZEYREK is not None

CONTENT_POS = {"Noun", "Verb", "Adj", "Adverb"}
FUNCTION_SUFFIXES = ()


def analyze_pos_counts(text: str) -> dict:
    tokens = [w for w in re.findall(r"\w+", text.lower()) if w]
    counts = {"noun": 0, "verb": 0, "adjective": 0, "adverb": 0, "content_word": 0, "function_word": 0, "total": len(tokens)}

    if not tokens:
        return counts

    if ZEYREK_AVAILABLE:
        for tok in tokens:
            try:
                results = _ZEYREK.analyze(tok)
                if not results:
                    continue
                pos = results[0][1].pos if len(results[0]) > 1 else None
                pos = getattr(pos, "name", str(pos))
                if pos == "Noun":
                    counts["noun"] += 1
                    counts["content_word"] += 1
                elif pos == "Verb":
                    counts["verb"] += 1
                    counts["content_word"] += 1
                elif pos == "Adj":
                    counts["adjective"] += 1
                    counts["content_word"] += 1
                elif pos == "Adverb":
                    counts["adverb"] += 1
                    counts["content_word"] += 1
                else:
                    counts["function_word"] += 1
            except Exception:
                counts["function_word"] += 1
    else:
        # EXPERIMENTAL heuristic fallback: treat verbs as tokens ending with common Turkish verb markers.
        for tok in tokens:
            if re.search(r"(yor|di|miş|mek|mak|ecek|acak|sin|im)$", tok):
                counts["verb"] += 1
                counts["content_word"] += 1
            elif len(tok) > 3:
                counts["noun"] += 1
                counts["content_word"] += 1
            else:
                counts["function_word"] += 1

    return counts


def sentence_count(text: str) -> int:
    parts = [p for p in re.split(r"[.!?]+", text) if p.strip()]
    return max(len(parts), 1)


def clause_count_approximate(text: str) -> int:
    # Turkish clauses commonly introduced by conjunctives/subordinators; approximate.
    markers = [" ve ", " ama ", " çünkü ", " eğer ", " ki ", " sonra ", " önce ", " den ", " olarak "]
    count = 1
    lowered = f" {text.lower()} "
    for m in markers:
        count += lowered.count(m)
    return count
