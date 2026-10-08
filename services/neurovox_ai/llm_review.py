"""Grammar and semantic error *candidates* from an instruction-tuned LLM (runs on the GPU host, e.g. Kaggle).

Output is a candidate count for the blind human raters to confirm, never a clinical label. Transcripts come from a
speech recogniser that tends to "repair" grammar, so these counts are lower bounds. Disabled on CPU hosts.
"""
import json
import os
import re
import threading

MODEL_NAME = os.environ.get("NEUROVOX_LLM_MODEL", "Qwen/Qwen2.5-3B-Instruct")
ENABLED = os.environ.get("NEUROVOX_LLM", "1") == "1" and os.environ.get("NEUROVOX_DEVICE") == "cuda"
CHUNK_WORDS, MAX_CHUNKS = 120, 8
VERSION = "llm-review/1"

SYSTEM = "Sen Türkçe konuşma dilini inceleyen bir dilbilimcisin. Yalnızca geçerli JSON döndürürsün."
PROMPT = """Aşağıda bir kişinin resim betimlerken söylediği cümlelerin yazıya dökülmüş hali var. Konuşma dili olduğu için noktalama, yazım ve küçük duraksamaları YOK SAY.

Yalnızca şu iki türde GERÇEK hataları bul:
- "gramer": ek/çekim hatası, özne-yüklem uyumsuzluğu, durum eki hatası, devrik veya eksik yapı yüzünden bozulmuş cümle.
- "anlamsal": bağlamda yanlış kelime seçimi, anlamsız veya çelişen ifade, kelimenin yanlış anlamda kullanımı.

Emin değilsen hata yazma. Hata yoksa boş liste döndür.
Biçim: {{"hatalar": [{{"tur": "gramer" veya "anlamsal", "metin": "hatalı kısım", "aciklama": "kısa neden"}}]}}

Metin:
\"\"\"{text}\"\"\"
"""

_lock = threading.Lock()
_state: dict = {"tok": None, "model": None, "failed": False}


def _load():
    if _state["model"] is not None or _state["failed"]:
        return _state["model"] is not None
    try:
        import torch
        from transformers import AutoModelForCausalLM, AutoTokenizer
        _state["tok"] = AutoTokenizer.from_pretrained(MODEL_NAME)
        _state["model"] = AutoModelForCausalLM.from_pretrained(MODEL_NAME, torch_dtype=torch.float16, device_map="cuda")
        return True
    except Exception:
        _state["failed"] = True   # do not retry on every recording
        return False


def _chunks(text: str) -> list[str]:
    words = text.split()
    return [" ".join(words[i:i + CHUNK_WORDS]) for i in range(0, len(words), CHUNK_WORDS)][:MAX_CHUNKS]


def _parse(raw: str) -> list[dict]:
    m = re.search(r"\{.*\}", raw, re.S)
    if not m:
        return []
    try:
        items = json.loads(m.group(0)).get("hatalar", [])
    except Exception:
        return []
    return [i for i in items if isinstance(i, dict) and i.get("tur") in ("gramer", "anlamsal")]


def review(text: str) -> dict | None:
    """{'grammar': n, 'semantic': n, 'items': [...]}, or None when the LLM is unavailable (never raises)."""
    if not ENABLED or len(text.split()) < 8:
        return None
    with _lock:
        if not _load():
            return None
        import torch
        tok, model = _state["tok"], _state["model"]
        items: list[dict] = []
        for chunk in _chunks(text):
            try:
                msgs = [{"role": "system", "content": SYSTEM}, {"role": "user", "content": PROMPT.format(text=chunk)}]
                ids = tok.apply_chat_template(msgs, add_generation_prompt=True, return_tensors="pt").to(model.device)
                with torch.no_grad():
                    out = model.generate(ids, max_new_tokens=300, do_sample=False)
                items += _parse(tok.decode(out[0][ids.shape[1]:], skip_special_tokens=True))
            except Exception:
                continue
    return {"grammar": sum(i["tur"] == "gramer" for i in items), "semantic": sum(i["tur"] == "anlamsal" for i in items), "items": items}
