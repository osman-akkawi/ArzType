# Offline language packs

`xx.lpk` files are gzip-compressed UTF-8 text, named by the language code Windows reports for a layout (`CultureInfo.TwoLetterISOLanguageName`, e.g. `ar`, `fr`, `nb`, `fil`, `sah`):

```
#ArzType-LP 2 <lang> <freq|flat> <sources> scripts=<SCRIPT,...>
word<TAB>count        (count 0 = frequency unknown)
```

At load time ArzType builds a character 4-gram model from the words, so a pack needs only a clean, frequency-ranked word list. Only the packs for keyboard layouts you have installed are loaded. All packs are embedded in `ArzType.exe` at build time.

## Adding or improving a language (no rebuild needed)

Put a file in `%LOCALAPPDATA%\ArzType\packs\` (the *My word lists…* button opens that folder):

- `xx.txt`: one word per line, `#` for comments. It is **merged** into the built-in pack, or becomes a new language if there is none. Use it for your own names, slang or work vocabulary.
- `xx.lpk`: a full pack. It **replaces** the built-in one.

Restart ArzType afterwards.

## Rebuilding all packs

See `tools/README.md`. Contribute only openly licensed or original vocabulary.
