#!/usr/bin/env python3
"""
Build ArzType offline language packs (language-packs/<lang>.lpk).

Each .lpk file is gzip-compressed UTF-8 text:
    #ArzType-LP 2 <lang> <freq|flat> <source-tag>
    word<TAB>count          (count 0 when the source has no frequencies)

Sources (download them first; see tools/README.md):
  fw/<code>.txt   hermitdave/FrequencyWords 2018 (OpenSubtitles), CC-BY-SA-4.0
  hs/<code>.dic   wooorm/dictionaries Hunspell word lists (per-language licences)
  um/<iso3>.txt   UniMorph inflection tables, CC-BY-SA

The app builds its character n-gram model from these words at load time,
so a pack is only a cleaned, script-filtered, frequency-ranked word list.
"""
import gzip, os, random, sys, unicodedata
from collections import Counter, defaultdict

SRC = sys.argv[1] if len(sys.argv) > 1 else "."
OUT = sys.argv[2] if len(sys.argv) > 2 else "language-packs"
MAX_WORDS = 50000
MAX_FLAT = 60000

# Source code -> Windows CultureInfo.TwoLetterISOLanguageName
FW_MAP = {"no": "nb", "pt_br": "pt", "tl": "fil", "zh_cn": None, "zh_tw": None, "ja": None, "ko": None}
# tat/uig UniMorph data is romanised, but the Windows layouts are Cyrillic/Arabic script.
UM_MAP = {"bod": "bo", "kir": "ky", "kaz": "kk", "hye": "hy", "uzb": "uz", "tel": "te", "urd": "ur",
          "tgk": "tg", "zul": "zu", "mlt": "mt", "ben": "bn", "aze": "az", "kat": "ka",
          "guj": "gu", "pus": "ps", "bak": "ba", "kan": "kn", "amh": "am", "bel": "be",
          "asm": "as", "sah": "sah"}
CYRILLIC_LANGS = {"ru", "uk", "be", "bg", "mk", "sr", "kk", "ky", "tt", "ba", "sah", "tg", "mn"}

# Serbian Latin -> Cyrillic so the Serbian Cyrillic layout is covered too.
SR_LAT2CYR = [("dž", "џ"), ("lj", "љ"), ("nj", "њ"), ("a", "а"), ("b", "б"), ("v", "в"), ("g", "г"),
              ("d", "д"), ("đ", "ђ"), ("e", "е"), ("ž", "ж"), ("z", "з"), ("i", "и"), ("j", "ј"),
              ("k", "к"), ("l", "л"), ("m", "м"), ("n", "н"), ("o", "о"), ("p", "п"), ("r", "р"),
              ("s", "с"), ("t", "т"), ("ć", "ћ"), ("u", "у"), ("f", "ф"), ("h", "х"), ("c", "ц"),
              ("č", "ч"), ("š", "ш")]


def to_cyr(w):
    for a, b in SR_LAT2CYR:
        w = w.replace(a, b)
    return w


def clean(word, lang):
    w = unicodedata.normalize("NFC", word.strip()).replace("’", "'").replace("ʼ", "'")
    w = w.lower().replace("i̇", "i")
    if lang in CYRILLIC_LANGS:  # dictionary stress marks
        w = unicodedata.normalize("NFC", "".join(c for c in unicodedata.normalize("NFD", w)
                                                if c not in "́̀") )
    if not (1 <= len(w) <= 32):
        return None
    for i, c in enumerate(w):
        cat = unicodedata.category(c)
        if cat[0] == "L" or cat in ("Mn", "Mc"):
            continue
        if c in "‌‍" and 0 < i < len(w) - 1:
            continue
        if c == "'" and 0 < i < len(w) - 1:
            continue
        return None
    if "ـ" in w:  # Arabic tatweel is decoration, not spelling
        return None
    return w


def script_of(c):
    try:
        name = unicodedata.name(c)
    except ValueError:
        return None
    first = name.split()[0]
    if first in ("COMBINING", "ZERO", "MODIFIER", "APOSTROPHE"):
        return None
    if first == "CJK":
        return "CJK"
    return first


def scripts_of(w):
    return {s for s in map(script_of, w) if s}


def dominant_scripts(counter):
    weight = Counter()
    for w, c in counter.most_common(3000):
        for s in scripts_of(w):
            weight[s] += max(c, 1)
    total = sum(weight.values()) or 1
    keep = {s for s, v in weight.items() if v / total >= 0.12}
    top = weight.most_common(1)[0][0] if weight else None
    if top and top != "LATIN":
        keep.discard("LATIN")  # Latin noise in a non-Latin language (subtitles, romanisation)
    return keep


def read_fw(path, lang):
    out = Counter()
    with open(path, encoding="utf-8", errors="ignore") as f:
        for line in f:
            parts = line.rstrip("\n").split(" ")
            if len(parts) != 2 or not parts[1].isdigit():
                continue
            w = clean(parts[0], lang)
            if w:
                out[w] += int(parts[1])
    return out


def read_hunspell(path, lang):
    out = Counter()
    with open(path, encoding="utf-8", errors="ignore") as f:
        for i, line in enumerate(f):
            if i == 0 or line.startswith("#"):
                continue
            raw = line.split("/")[0].split("\t")[0].strip()
            if not raw or raw[0].isupper():  # skip proper nouns
                continue
            w = clean(raw, lang)
            if w:
                out[w] = 0
    return out


def read_unimorph(path, lang):
    out = Counter()
    with open(path, encoding="utf-8", errors="ignore") as f:
        for line in f:
            parts = line.rstrip("\n").split("\t")
            for raw in parts[:2]:
                for piece in raw.split(" "):
                    w = clean(piece, lang)
                    if w:
                        out[w] = 0
    return out


def main():
    packs = defaultdict(Counter)
    sources = defaultdict(set)
    for name in sorted(os.listdir(os.path.join(SRC, "fw"))):
        code = name[:-4]
        lang = FW_MAP.get(code, code)
        if lang is None:
            continue
        packs[lang].update(read_fw(os.path.join(SRC, "fw", name), lang))
        sources[lang].add("fw")
    for name in sorted(os.listdir(os.path.join(SRC, "hs"))):
        lang = name[:-4]
        for w in read_hunspell(os.path.join(SRC, "hs", name), lang):
            packs[lang].setdefault(w, 0)
        sources[lang].add("hs")
    for name in sorted(os.listdir(os.path.join(SRC, "um"))):
        lang = UM_MAP.get(name[:-4])
        if not lang:
            continue
        for w in read_unimorph(os.path.join(SRC, "um", name), lang):
            packs[lang].setdefault(w, 0)
        sources[lang].add("um")

    if "sr" in packs:  # add Cyrillic Serbian
        for w, c in list(packs["sr"].items()):
            if all(script_of(ch) in ("LATIN", None) for ch in w):
                packs["sr"][to_cyr(w)] += c

    os.makedirs(OUT, exist_ok=True)
    rnd = random.Random(1939)
    summary = []
    for lang, counter in sorted(packs.items()):
        scripts = dominant_scripts(counter)
        if lang == "sr":
            scripts = {"LATIN", "CYRILLIC"}
        words = [(w, c) for w, c in counter.items() if scripts_of(w) and scripts_of(w) <= scripts]
        freq = [x for x in words if x[1] > 0]
        flat = [x for x in words if x[1] == 0]
        freq.sort(key=lambda x: (-x[1], x[0]))
        freq = freq[:MAX_WORDS]
        flat.sort()
        rnd.shuffle(flat)
        room = MAX_WORDS - len(freq) if freq else MAX_FLAT
        flat = flat[:max(0, min(room, MAX_FLAT))] if len(freq) < 20000 else []
        if len(freq) + len(flat) < 1500:
            print(f"skip {lang}: only {len(freq) + len(flat)} words", file=sys.stderr)
            continue
        kind = "freq" if freq else "flat"
        tag = "+".join(sorted(sources[lang]))
        path = os.path.join(OUT, f"{lang}.lpk")
        with gzip.open(path, "wt", encoding="utf-8", compresslevel=9) as f:
            f.write(f"#ArzType-LP 2 {lang} {kind} {tag} scripts={','.join(sorted(scripts))}\n")
            for w, c in freq + sorted(flat):
                f.write(f"{w}\t{c}\n")
        summary.append((lang, len(freq), len(flat), kind, tag, ",".join(sorted(scripts)), os.path.getsize(path)))
    for row in summary:
        print("%-4s freq=%-6d flat=%-6d %-4s %-8s %-30s %7d B" % row)
    print(f"{len(summary)} packs, {sum(r[6] for r in summary) / 1e6:.1f} MB")


if __name__ == "__main__":
    main()
