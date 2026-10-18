#!/usr/bin/env python3
"""
Convert Unicode CLDR (release-43) Windows keyboard XML files into a compact JSON
fixture (tests/fixtures/windows-layouts.json) so the detection engine can be
benchmarked on Linux/CI with the *real* Windows key maps.

Usage: python3 tools/cldr_layouts.py <cldr>/keyboards/windows tests/fixtures/windows-layouts.json
"""
import json, os, re, sys
import xml.etree.ElementTree as ET

ISO_TO_SCAN = {"E00": 0x29, "E11": 0x0C, "E12": 0x0D, "D11": 0x1A, "D12": 0x1B, "D13": 0x2B,
               "C10": 0x27, "C11": 0x28, "C12": 0x2B, "B00": 0x56, "B08": 0x33, "B09": 0x34, "B10": 0x35}
for i in range(1, 11):
    ISO_TO_SCAN[f"E{i:02d}"] = 0x01 + i
    ISO_TO_SCAN[f"D{i:02d}"] = 0x0F + i
for i in range(1, 10):
    ISO_TO_SCAN[f"C{i:02d}"] = 0x1D + i
for i in range(1, 8):
    ISO_TO_SCAN[f"B{i:02d}"] = 0x2B + i


def unescape(s):
    return re.sub(r"\\u\{([0-9A-Fa-f]+)\}", lambda m: chr(int(m.group(1), 16)), s)


def main(src, dst):
    out = {}
    for name in sorted(os.listdir(src)):
        if not name.endswith(".xml") or name.startswith("_"):
            continue
        root = ET.parse(os.path.join(src, name)).getroot()
        locale = root.get("locale")
        display = root.find("names/name").get("value")
        lang = locale.split("-t-")[0].split("-")[0]
        dead = set()
        for t in root.iter("transform"):
            frm = unescape(t.get("from", ""))
            if len(frm) >= 2:
                dead.add(frm[0])
        maps = {}
        for km in root.findall("keyMap"):
            mods = km.get("modifiers")
            if mods is None:
                level = "base"
            elif mods in ("shift", "shift+caps?", "shift caps"):
                level = "shift"
            else:
                continue
            for m in km.findall("map"):
                scan = ISO_TO_SCAN.get(m.get("iso"))
                if scan is None:
                    continue
                to = unescape(m.get("to"))
                if to in dead or any(ord(c) < 0x20 for c in to):
                    to = None
                maps.setdefault(level, {})[f"{scan:02X}"] = to
        if "base" not in maps:
            continue
        out[locale] = {"name": display, "lang": lang, "base": maps["base"], "shift": maps.get("shift", {})}
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    with open(dst, "w", encoding="utf-8") as f:
        json.dump(out, f, ensure_ascii=False, separators=(",", ":"), sort_keys=True)
    print(f"{len(out)} layouts -> {dst} ({os.path.getsize(dst)} bytes)")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
