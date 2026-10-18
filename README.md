# ArzType ⌨️

**Typed a whole sentence with the keyboard on the wrong language? ArzType fixes it the moment you press Space — and switches the keyboard for you.**

```
You meant:      today          مرحبا          привет         what a morning
You typed:      فخيشغ          lvpfh          ghbdtn         zhqt q ,orning   (French AZERTY)
ArzType makes:  today ✓        مرحبا ✓        привет ✓       what a morning ✓
```

Free, open source (MIT), **100% offline**, for Windows 10/11 (64-bit). One `ArzType.exe`, no installer, no account, no internet.

[العربية ← README.ar.md](README.ar.md)

---

## Why people keep it running

- **Smart detection in 70+ languages.** Arabic, English, French, Russian, Ukrainian, Hebrew, Persian, Urdu, Hindi, Greek, Turkish, German, Spanish, Polish, Thai, Georgian, Armenian, Kazakh, Mongolian… [full list below](#languages). Every language has an offline pack: a frequency-ranked word list **plus a character model**, so it also recognises names, slang and words that are in no dictionary.
- **Fast.** The decision takes about **40 µs** per word, inside the keyboard hook, at the exact moment you press Space. No timers, so it keeps up with fast typists.
- **Careful.** In a benchmark on the real key maps of Windows layouts (Unicode CLDR), it changed **0 of 28,000+ correctly typed common words**, and 0.3% of rare words it had never seen. When it isn't sure, it waits for the next word instead of guessing.
- **Fixes whole sentences.** Short words like `to` → `فخ` or `a` → `ش` are ambiguous alone. ArzType keeps them pending and fixes them together with the next word. In the benchmark, **92% of 6-word sentences** typed fully on the wrong layout came out completely right.
- **One-key manual fix for any language.** Tap **Shift twice** (or press **Pause**) to convert the last word, or the word you're typing, to the other layout. Press again to cycle through your layouts. This works for every installed layout, including languages without a pack.
- **Undo, and it learns.** Tap Shift twice right after a fix to undo it, and ArzType never touches that word again. Fix a word by hand, and next time it fixes it automatically. Only salted fingerprints are stored, never the words.
- **Respects your privacy and your work.** Nothing you type is saved or sent. It never touches password fields, terminals, code editors, password managers, remote desktops or admin windows. You can exclude any app from the tray menu.
- **Looks after itself.** It starts with Windows and lives in the tray, where its icon shows the current language (EN / AR / FR…). The interface is in English or Arabic, following your Windows language.

## Install

### For users: one setup file

Download **`ArzType-Setup-0.2.1.exe`** and double-click it. Next, Next, Finish.

- No administrator rights are needed. It installs for the current user in `%LOCALAPPDATA%\Programs\ArzType`.
- It adds a Start-menu entry, an optional desktop shortcut and "start with Windows", and an uninstaller in *Settings › Apps*.
- The setup wizard is in English and Arabic.
- Windows SmartScreen may say "Windows protected your PC" because the file isn't code-signed yet. Click **More info › Run anyway**.

You need at least two keyboard layouts in Windows (Settings › Time & language › Language & region), for example English + Arabic, or English + French. Then open ArzType and turn on **Auto-fix**.

### For the developer: build the setup file

On Windows 10/11 x64, double-click **`scripts\make-installer.cmd`**. It:
1. installs the .NET 10 SDK and Inno Setup with `winget` if they're missing (run it again after that),
2. runs the tests and builds the single-file `publish\ArzType.exe` (language packs included),
3. builds **`publish\ArzType-Setup-0.2.1.exe`** and opens the folder.

That setup file is what you share: on your website, GitHub Releases, WhatsApp or a USB stick. `publish\ArzType.exe` on its own also works as a portable version. To just build and try it, use `scripts\build-and-run.cmd`.

## How to use

| You do | ArzType does |
|---|---|
| Type normally and press **Space** or **Enter** | A wrong-layout word (or the last few) is replaced, and the keyboard switches |
| Tap **Shift Shift** (or **Pause**) right after a fix | Undo. That word is never auto-fixed again |
| Tap **Shift Shift** after any word, or in the middle of one | Convert it to the most likely other layout. Tap again to cycle |
| Right-click the tray icon › *Never fix in …* | Excludes the current app |

Options: start with Windows, fix on Enter, the two hotkeys, learning, sensitivity (Careful / Balanced / Eager), and an advanced mode for apps that hide their text fields from Windows accessibility.

## Languages

Smart detection (pack included): Afrikaans, Albanian, Arabic, Armenian, Assamese, Azerbaijani, Bangla, Basque, Bashkir, Belarusian, Bosnian, Breton, Bulgarian, Catalan, Croatian, Czech, Danish, Dutch, English, Esperanto, Estonian, Faroese, Filipino, Finnish, French, Galician, Georgian, German, Greek, Gujarati, Hebrew, Hindi, Hungarian, Icelandic, Indonesian, Irish, Italian, Kannada, Kazakh, Latvian, Lithuanian, Luxembourgish, Macedonian, Malay, Malayalam, Mongolian, Nepali, Norwegian, Pashto, Persian, Polish, Portuguese, Romanian, Russian, Sakha, Scottish Gaelic, Serbian (Cyrillic + Latin), Sinhala, Slovak, Slovenian, Spanish, Swedish, Tamil, Telugu, Thai, Turkish, Turkmen, Ukrainian, Urdu, Vietnamese, Welsh, Zulu.

- **Any other layout:** the Shift-Shift manual fix still works, because it uses Windows' own key tables.
- **Add or extend a language:** put a `xx.txt` (one word per line) or a built `xx.lpk` in `%LOCALAPPDATA%\ArzType\packs`. The tray window has a button that opens that folder. See [`language-packs/README.md`](language-packs/README.md).
- **Not touched:** IME-based input (Japanese, Chinese, Korean, Amharic…), because typed keys there are not final text.

## Accuracy (reproducible)

```
dotnet run -c Release --project tests/ArzType.Tests -- bench 800
```

This types real words with the real Windows key maps of 23 layout pairs (en/ar, en/fr AZERTY, fr/ar, en/ru, en/uk, en/de QWERTZ, en/he, en/fa, en/el, en/tr, en/th, en/ka, en/hy, en/ur, en/hi, ru/uk…). It does this on the right layout and on the wrong one, and holds back 10% of the rarer words so "unseen" really means unseen. Latest results:

| Measure | Result |
|---|---|
| Correctly typed common words changed | **0.00%** (0 of 28,246) |
| Correctly typed *unseen* words changed | 0.32% |
| Wrong-layout single words fixed instantly | 81.4% (most misses are 1–2 letter words, which are fixed with the next word) |
| Wrong-layout 6-word sentences fully fixed | **92.3%** (English on Arabic layout 99.7%, Arabic on English layout 98%) |

The hardest pair is Russian↔Ukrainian, where many words are spelled the same way. ArzType is deliberately cautious there.

## How it works

1. A low-level keyboard hook runs on its own high-priority thread. It records **physical keys** (scan codes), not text.
2. When a word starts, a background check uses Windows UI Automation to confirm that the focus is a normal, non-password text field in a non-excluded, non-elevated app.
3. When you press Space or Enter, every installed layout "re-types" the same keys through Windows `ToUnicodeEx`. Each language model scores its version: lexicon frequency mixed with a character 4-gram model (Witten-Bell). ArzType acts only when the likelihood ratio is above a length-dependent threshold, and only when one language is a clear winner.
4. The key is held back, the word is replaced with Backspace + Unicode input (never the clipboard), the key is re-sent, and the window is asked to switch layout.

```
src/ArzType.Core     platform-independent engine: packs, n-gram model, decisions, typing state machine, learning
src/ArzType.App      Windows: hook thread, ToUnicodeEx mapper, safety gate, tray UI (EN/AR)
tests/ArzType.Tests  unit tests + accuracy benchmark on real Windows layouts (CLDR fixture), runs on Linux/macOS too
tests/ArzType.E2E    real Windows test: drives Notepad with SendInput and checks the result
tools/               scripts that build the language packs and the layout fixture from open data
```

## Honest limits

- It was developed and benchmarked against real Windows key tables, and the app compiles against the Windows Desktop reference assemblies. **The finished .exe has not yet been run by the author on a Windows desktop.** Please try it in Notepad first, and report anything odd.
- Apps that don't expose text fields to Windows accessibility (some games, some custom UIs) are skipped unless you turn on the advanced option.
- Words are only checked at Space/Enter. Punctuation keys that are letters in the other layout (`,` = و, `;` = ك) are handled, but text pasted or autocompleted by the app is not tracked.
- Undo after **Enter** isn't offered, because the message may already be sent. Use the app's own Ctrl+Z.

## Privacy

Keys live in memory for a few words at most and are dropped on any click, focus change or navigation key. Nothing is written to disk except `%LOCALAPPDATA%\ArzType\settings.json`: your options, counters, excluded apps, and learned-word **fingerprints** (salted SHA-256). Fingerprints of very short words could in theory be guessed, and *Forget learned words* clears them. There is no network code at all.

## Credits & licences

App code: MIT. Language data: see [DATA-LICENSES.md](DATA-LICENSES.md). It comes from FrequencyWords/OpenSubtitles (CC-BY-SA-4.0), Hunspell dictionaries (various open licences) and UniMorph (CC-BY-SA). The layout test fixture comes from Unicode CLDR (Unicode licence).
