"""Análise editorial do corte: frases de legenda, palavras de impacto, gancho e temas ilustráveis."""
import re
import unicodedata

PUNCT_END = ".!?…"
SOFT_END = ",;:"

# peso de impacto por palavra (sem acento, minúscula)
IMPACT = {
    "deus": 3, "jesus": 3, "cristo": 3, "senhor": 2, "espirito": 2, "santo": 1,
    "fe": 3, "verdade": 3, "proposito": 3, "promessa": 3, "perdao": 3, "milagre": 4,
    "nunca": 4, "sempre": 2, "jamais": 4, "impossivel": 4, "medo": 3, "coragem": 3,
    "cruz": 4, "sangue": 3, "gloria": 3, "luz": 3, "fogo": 4, "tempestade": 4, "vento": 2,
    "vida": 2, "morte": 3, "ressurreicao": 4, "vitoria": 3, "esperanca": 3, "graca": 3,
    "amor": 3, "pecado": 3, "salvacao": 3, "atencao": 3, "cuidado": 3, "levanta": 3,
    "desista": 4, "tudo": 2, "nada": 2, "agora": 2, "hoje": 2, "libertar": 3, "liberdade": 3,
}
TRIGGERS_BEFORE = {"nao", "nunca", "jamais"}

THEMES = [
    ("cruz", {"cruz", "calvario", "crucificado", "crucificou", "sangue"}, "gong"),
    ("tempestade", {"tempestade", "tormenta", "vento", "chuva", "onda", "ondas", "mar"}, "thunder"),
    ("fogo", {"fogo", "chama", "chamas", "arde", "queimar", "incendeia", "brasa"}, "crackle"),
    ("luz", {"luz", "gloria", "ceu", "esperanca", "vitoria", "milagre", "resplandece", "aurora"}, "chime"),
]


def norm(word):
    text = unicodedata.normalize("NFD", word.lower())
    text = "".join(c for c in text if unicodedata.category(c) != "Mn")
    return re.sub(r"[^a-z0-9]", "", text)


def load_words(raw):
    """Aceita lista de palavras ou lista de segmentos (formato do Cortafé/Faster-Whisper)."""
    words = []
    for item in raw:
        if "words" in item:
            words.extend(item["words"])
        else:
            words.append(item)
    out = []
    for w in words:
        text = str(w.get("word", w.get("w", ""))).strip()
        if not text:
            continue
        out.append({"w": text, "s": float(w["start"]), "e": float(w["end"]), "n": norm(text)})
    out.sort(key=lambda x: x["s"])
    for i, w in enumerate(out):  # garante duração mínima e sem sobreposição
        nxt = out[i + 1]["s"] if i + 1 < len(out) else w["e"] + 1
        w["e"] = max(w["s"] + 0.08, min(w["e"], nxt)) if w["e"] > w["s"] else min(w["s"] + 0.25, nxt)
    return out


def word_score(words, i):
    w = words[i]
    score = IMPACT.get(w["n"], 0)
    if i > 0 and words[i - 1]["n"] in TRIGGERS_BEFORE:
        score += 2
    if w["w"].rstrip().endswith(("!", "?")):
        score += 1
    if len(w["n"]) >= 10:
        score += 1
    return score


def phrases(words, max_words=3, max_chars=20):
    out, cur = [], []
    for i, w in enumerate(words):
        cur.append(i)
        text = " ".join(words[j]["w"] for j in cur)
        gap = (words[i + 1]["s"] - w["e"]) if i + 1 < len(words) else 9
        last = w["w"].rstrip()[-1:]
        if len(cur) >= max_words or len(text) >= max_chars or gap > 0.45 or last in PUNCT_END + SOFT_END:
            out.append(cur)
            cur = []
    if cur:
        out.append(cur)
    return out


def pick_emphasis(words, duration, min_gap=4.0, start_guard=0.6):
    limit = max(2, int(duration / 9))
    ranked = sorted(((word_score(words, i), -words[i]["s"], i) for i in range(len(words))), reverse=True)
    chosen = []
    for score, _, i in ranked:
        if score < 3 or len(chosen) >= limit:
            continue
        t = words[i]["s"]
        if t < start_guard or t > duration - 0.8:
            continue
        if all(abs(t - words[j]["s"]) >= min_gap for j in chosen):
            chosen.append(i)
    return sorted(chosen)


def sentences(words):
    out, cur = [], []
    for i, w in enumerate(words):
        cur.append(i)
        gap = (words[i + 1]["s"] - w["e"]) if i + 1 < len(words) else 9
        if w["w"].rstrip()[-1:] in PUNCT_END or gap > 0.8:
            out.append(cur)
            cur = []
    if cur:
        out.append(cur)
    return out


def pick_hook(words):
    """Escolhe a frase mais forte (4 a 10 palavras) para abrir o vídeo em texto grande."""
    best, best_score = None, -1
    for sent in sentences(words):
        if not 4 <= len(sent) <= 10:
            continue
        score = sum(word_score(words, i) for i in sent) + (3 if words[sent[-1]]["w"].rstrip().endswith("?") else 0)
        score -= 0.04 * words[sent[0]]["s"]
        if score > best_score:
            best, best_score = sent, score
    if best is None:
        best = list(range(min(7, len(words))))
    toks = [words[i]["w"].rstrip(".,;:") for i in best]
    star = max(range(len(best)), key=lambda k: word_score(words, best[k]))
    return toks, star


def pick_themes(words, duration, max_events=5, min_gap=3.6):
    found = []
    for i, w in enumerate(words):
        for name, vocab, sfx in THEMES:
            if w["n"] in vocab and 1.0 < w["s"] < duration - 1.5:
                found.append((w["s"], name, sfx))
                break
    chosen, last_by_theme = [], {}
    for t, name, sfx in found:
        if chosen and t - chosen[-1][0] < min_gap:
            continue
        if name in last_by_theme and t - last_by_theme[name] < 12:
            continue
        chosen.append((t, name, sfx))
        last_by_theme[name] = t
        if len(chosen) >= max_events:
            break
    return chosen
