# Tools

Rebuild the language packs from open data (Python 3, no packages needed):

```bash
mkdir -p data/fw data/hs data/um
# FrequencyWords (repeat for each language code; small languages only have *_full.txt)
curl -o data/fw/ar.txt https://raw.githubusercontent.com/hermitdave/FrequencyWords/master/content/2018/ar/ar_50k.txt
# Hunspell lists
curl -o data/hs/cy.dic https://raw.githubusercontent.com/wooorm/dictionaries/main/dictionaries/cy/index.dic
# UniMorph (ISO 639-3 file names)
curl -o data/um/bel.txt https://raw.githubusercontent.com/unimorph/bel/master/bel

python3 tools/build_packs.py data language-packs
```

Regenerate the Windows layout fixture used by the tests:

```bash
git clone --depth 1 --branch release-43 --filter=blob:none --sparse https://github.com/unicode-org/cldr
(cd cldr && git sparse-checkout set keyboards/windows keyboards/dtd)
python3 tools/cldr_layouts.py cldr/keyboards/windows tests/fixtures/windows-layouts.json
```

Then measure accuracy: `dotnet run -c Release --project tests/ArzType.Tests -- bench 800`.
