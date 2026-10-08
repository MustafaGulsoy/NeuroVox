"""Turkish NLP helper.

Primary analyzer: zeyrek (open-source Turkish morphological analyzer), used through its per-word `_parse`
(its text API needs NLTK punkt data, which is not available in the containers). Without zeyrek, conservative
regex heuristics are used and the results are marked EXPERIMENTAL; error candidates are then unavailable (None).

Everything here is a *candidate* signal for the research team -- never a clinical label.
"""

import logging
import re

_ZEYREK = None
try:
    import zeyrek
    _ZEYREK = zeyrek.MorphAnalyzer()
except Exception:
    _ZEYREK = None

ZEYREK_AVAILABLE = _ZEYREK is not None
logging.getLogger("zeyrek").setLevel(logging.ERROR)   # it logs every candidate parse at WARNING

FILLERS = {"şey", "yani", "işte", "hani", "ııı", "ıı", "eee", "ee", "hmm", "ıhm", "aaa"}
CORRECTION_MARKERS = [("pardon",), ("yok", "yok"), ("hayır", "hayır"), ("yani", "şey"), ("şey", "yani"), ("daha", "doğrusu")]
_cache: dict[str, list] = {}


def _tokens(text: str) -> list[str]:
    return [w for w in re.findall(r"\w+", text.lower()) if w]


def _parses(token: str) -> list[tuple[str, str]]:
    """[(pos, lemma), ...] for a word; [] when zeyrek cannot analyse it."""
    if token not in _cache:
        try:
            res = _ZEYREK._parse(token)
        except Exception:
            res = []
        _cache[token] = [(a.pos.value, a.dict_item.lemma) for a in res]
    return _cache[token]


def analyze_pos_counts(text: str) -> dict:
    tokens = _tokens(text)
    counts = {"noun": 0, "verb": 0, "adjective": 0, "adverb": 0, "content_word": 0, "function_word": 0, "total": len(tokens)}
    if not tokens:
        return counts

    if ZEYREK_AVAILABLE:
        for tok in tokens:
            parses = _parses(tok)
            pos = parses[0][0] if parses else None   # no disambiguation: first analysis (EXPERIMENTAL)
            if pos == "Noun":
                counts["noun"] += 1
                counts["content_word"] += 1
            elif pos == "Verb":
                counts["verb"] += 1
                counts["content_word"] += 1
            elif pos == "Adj":
                counts["adjective"] += 1
                counts["content_word"] += 1
            elif pos == "Adv":
                counts["adverb"] += 1
                counts["content_word"] += 1
            else:
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


def _sentences(text: str) -> list[str]:
    return [p.strip() for p in re.split(r"[.!?…]+", text) if p.strip()]


def sentence_count(text: str) -> int:
    return max(len(_sentences(text)), 1)


def clause_count_approximate(text: str) -> int:
    # Turkish clauses commonly introduced by conjunctives/subordinators; approximate.
    markers = [" ve ", " ama ", " çünkü ", " eğer ", " ki ", " sonra ", " önce ", " den ", " olarak "]
    count = 1
    lowered = f" {text.lower()} "
    for m in markers:
        count += lowered.count(m)
    return count


def error_candidates(text: str) -> dict | None:
    """Language-error CANDIDATES from the transcript. None when no morphological analyzer is available.

    - unanalyzable_word_*: words zeyrek cannot analyse (neologism / paraphasia / ASR failure candidates).
    - verbless_sentence_*: sentences of >= 3 words without any word that can be a verb (fragment / agrammatism candidates).
    - filler_*: hesitation words (eee, ııı, şey, yani, işte, hani).
    - self_correction_candidates: immediate word-pair repetitions and explicit correction markers.
    Grammar-agreement and semantic errors need a human rater (blind annotation layer); they are not guessed here.
    """
    if not ZEYREK_AVAILABLE:
        return None
    tokens = _tokens(text)
    n = len(tokens)
    words = [t for t in tokens if t.isalpha() and len(t) > 1]
    unanalyzable = sum(1 for t in words if not _parses(t))

    sentences = [s for s in (_tokens(x) for x in _sentences(text)) if s]
    long_sentences = [s for s in sentences if len(s) >= 3]
    verbless = sum(1 for s in long_sentences if not any(p == "Verb" for t in s for p, _ in _parses(t)))

    fillers = sum(1 for t in tokens if t in FILLERS)
    # bigram repetition: "bir kadın bir kadın"
    bigrams = list(zip(tokens, tokens[1:]))
    corrections = sum(1 for i in range(len(bigrams) - 2) if bigrams[i] == bigrams[i + 2] and bigrams[i][0] != bigrams[i][1])
    lowered = " ".join(tokens)
    corrections += sum(lowered.count(" ".join(m)) for m in CORRECTION_MARKERS)

    return {
        "unanalyzable_word_candidates": float(unanalyzable),
        "unanalyzable_word_ratio": round(unanalyzable / len(words), 4) if words else 0.0,
        "verbless_sentence_candidates": float(verbless),
        "verbless_sentence_ratio": round(verbless / len(long_sentences), 4) if long_sentences else 0.0,
        "filler_count": float(fillers),
        "filler_ratio": round(fillers / n, 4) if n else 0.0,
        "self_correction_candidates": float(corrections),
    }
