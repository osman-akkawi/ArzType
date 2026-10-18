# Data sources and licences

ArzType's code is MIT-licensed. The language packs in `language-packs/*.lpk` are derived data files, distributed alongside the code under their own licences (an aggregation, not a combined work). Every pack's header line names its sources (`fw`, `hs`, `um`).

| Tag | Source | Licence | Used for |
|---|---|---|---|
| `fw` | [hermitdave/FrequencyWords](https://github.com/hermitdave/FrequencyWords), 2018 OpenSubtitles frequency lists | CC-BY-SA-4.0 (content) | Most languages (word + frequency) |
| `hs` | [wooorm/dictionaries](https://github.com/wooorm/dictionaries) Hunspell word lists | cy LGPL-3.0, fo/hy GPL-2.0/LGPL-2.1/MPL-1.1, ga GPL-2.0, gd GPL-3.0, lb EUPL-1.1, mn LPPL-1.3c, ne LGPL-2.1, tk Apache-2.0 | cy, fo, ga, gd, lb, mn, ne, tk, hy |
| `um` | [UniMorph](https://github.com/unimorph) inflection tables | CC-BY-SA (per language) | am, as, az, ba, be, bn, gu, hy, ka, kk, kn, ps, sah, te, ur, zu |
| fixture | [Unicode CLDR](https://github.com/unicode-org/cldr) release-43 `keyboards/windows` | Unicode License v3 | `tests/fixtures/windows-layouts.json` (tests only) |

Processing (`tools/build_packs.py`): NFC normalisation, lower-casing, removal of tokens with digits or punctuation, removal of words written in a script foreign to the language (for example English words in the Russian subtitle list), stress-mark removal for Cyrillic dictionaries, a Latin→Cyrillic copy for Serbian, and a cap of 50,000–60,000 words per language. Packs built from CC-BY-SA data remain under CC-BY-SA-4.0.
