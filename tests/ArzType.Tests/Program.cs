using System.Diagnostics;
using ArzType.Core;
using ArzType.Tests;

string root = FindRepoRoot();
string packs = Path.Combine(root, "language-packs");
var mapper = new CldrMapper(Path.Combine(root, "tests", "fixtures", "windows-layouts.json"));

if (args.Length > 0 && args[0] == "bench")
{
    Benchmark.Run(packs, mapper, args.Skip(1).ToArray());
    return;
}

int passed = 0, failed = 0;
void Test(string name, Action action)
{
    try { action(); Console.WriteLine($"PASS {name}"); passed++; }
    catch (Exception e) { Console.WriteLine($"FAIL {name}: {e.Message}"); failed++; }
}
void Assert(bool ok, string message) { if (!ok) throw new Exception(message); }

var model = new LanguageModel(packs);
var sw = Stopwatch.StartNew();
model.Preload(["en", "ar", "fr", "ru", "de", "he", "uk", "fa", "el"]);
Console.WriteLine($"Loaded 9 packs in {sw.ElapsedMilliseconds} ms");

var en = mapper["en-t-k0-windows"]; var ar = mapper["ar-t-k0-windows"]; var fr = mapper["fr-t-k0-windows"];
var ru = mapper["ru-t-k0-windows"]; var de = mapper["de-t-k0-windows"]; var he = mapper["he-t-k0-windows"];
var uk = mapper["uk-t-k0-windows"];

Corrector NewCorrector(params CldrMapper.Layout[] layouts)
{
    var c = new Corrector(model, mapper);
    c.SetLayouts(layouts.Select(l => l.Info).ToArray());
    return c;
}

// Types `text` (intended for layout `intended`) while `active` is selected; returns what ends up on screen.
(string Screen, nint Layout, List<Correction> Fixes) Simulate(Corrector c, string text, CldrMapper.Layout intended, CldrMapper.Layout active)
{
    var screen = new System.Text.StringBuilder();
    nint layout = active.Id;
    var fixes = new List<Correction>();
    var t = new DateTime(2026, 1, 1);
    foreach (var word in text.Split(' '))
    {
        var strokes = mapper.Type(word, intended) ?? throw new Exception($"cannot type {word}");
        foreach (var s in strokes)
        {
            t = t.AddMilliseconds(120);
            c.OnTextKey(s, layout, t);
            screen.Append(mapper.Map(s, layout));
        }
        t = t.AddMilliseconds(120);
        var fix = c.OnBoundary(Boundary.Space, layout, t);
        if (fix is not null)
        {
            fixes.Add(fix);
            string cur = screen.ToString();
            int remove = 0, cps = 0;
            for (int i = cur.Length - 1; i >= 0 && cps < fix.Backspaces; i--) { remove++; if (!char.IsLowSurrogate(cur[i])) cps++; }
            screen.Length -= remove;
            screen.Append(fix.Text);
            layout = fix.SwitchTo;
        }
        screen.Append(' ');
    }
    return (screen.ToString().TrimEnd(), layout, fixes);
}

Test("Arabic layout, English intended: 'today' fixed", () =>
{
    var r = Simulate(NewCorrector(en, ar), "today", en, ar);
    Assert(r.Screen == "today" && r.Layout == en.Id, $"got '{r.Screen}'");
});
Test("English layout, Arabic intended: 'مرحبا' fixed", () =>
{
    var r = Simulate(NewCorrector(en, ar), "مرحبا", ar, en);
    Assert(r.Screen == "مرحبا" && r.Layout == ar.Id, $"got '{r.Screen}'");
});
Test("Arabic word using punctuation keys (وين = ,dk) fixed", () =>
{
    var r = Simulate(NewCorrector(en, ar), "وين", ar, en);
    Assert(r.Screen == "وين", $"got '{r.Screen}'");
});
Test("Arabic lam-alef key (لا) handled both ways", () =>
{
    var r = Simulate(NewCorrector(en, ar), "لازم", ar, en);
    Assert(r.Screen == "لازم", $"got '{r.Screen}'");
    var r2 = Simulate(NewCorrector(en, ar), "about", en, ar);
    Assert(r2.Screen == "about", $"got '{r2.Screen}'");
});
Test("Correct English is never touched", () =>
{
    var r = Simulate(NewCorrector(en, ar), "i want to make a cake for my friends today", en, en);
    Assert(r.Fixes.Count == 0, $"changed: {string.Join(",", r.Fixes.Select(f => f.Before))}");
});
Test("Correct Arabic is never touched", () =>
{
    var r = Simulate(NewCorrector(en, ar), "انا بدي روح عالبيت بكرا الصبح ان شاء الله", ar, ar);
    Assert(r.Fixes.Count == 0, $"changed: {string.Join(",", r.Fixes.Select(f => f.Before))}");
});
Test("Whole wrong-layout Arabic sentence fixed", () =>
{
    var r = Simulate(NewCorrector(en, ar), "السلام عليكم كيف حالك اليوم", ar, en);
    Assert(r.Screen == "السلام عليكم كيف حالك اليوم", $"got '{r.Screen}'");
});
Test("Russian typed on English layout (ghbdtn → привет)", () =>
{
    var r = Simulate(NewCorrector(en, ru), "привет как дела", ru, en);
    Assert(r.Screen == "привет как дела", $"got '{r.Screen}'");
});
Test("English typed on French AZERTY fixed", () =>
{
    var r = Simulate(NewCorrector(en, fr), "what a beautiful morning", en, fr);
    Assert(r.Screen == "what a beautiful morning", $"got '{r.Screen}'");
});
Test("French on French AZERTY untouched", () =>
{
    var r = Simulate(NewCorrector(en, fr), "bonjour je voudrais un café", fr, fr);
    Assert(r.Fixes.Count == 0, $"changed: {string.Join(",", r.Fixes.Select(f => f.Before))}");
});
Test("German QWERTZ: English typed on German layout ('everybody' → y/z swap)", () =>
{
    var r = Simulate(NewCorrector(en, de), "everybody is lazy", en, de);
    Assert(r.Screen == "everybody is lazy", $"got '{r.Screen}'");
});
Test("Hebrew typed on English layout", () =>
{
    var r = Simulate(NewCorrector(en, he), "שלום מה שלומך", he, en);
    Assert(r.Screen == "שלום מה שלומך", $"got '{r.Screen}'");
});
Test("Three layouts: Ukrainian vs Russian picked correctly", () =>
{
    var r = Simulate(NewCorrector(en, ru, uk), "привіт як справи", uk, en);
    Assert(r.Screen == "привіт як справи", $"got '{r.Screen}'");
});
Test("Single letters are never auto-changed", () =>
{
    var r = Simulate(NewCorrector(en, ar), "و", ar, en);
    Assert(r.Fixes.Count == 0, "changed one letter");
});
Test("Manual fix converts word in progress and cycles back", () =>
{
    var c = NewCorrector(en, ar);
    var t = DateTime.UtcNow;
    foreach (var s in mapper.Type("sdg", en)!) c.OnTextKey(s, en.Id, t = t.AddMilliseconds(100)); // user wanted "سيل"
    var fix = c.OnManualFix(en.Id, t);
    Assert(fix is { Text: "سيل", Backspaces: 3 } && fix.SwitchTo == ar.Id, $"got {fix}");
    var back = c.OnManualFix(ar.Id, t.AddMilliseconds(300));
    Assert(back is { Text: "sdg", Backspaces: 3 } && back.SwitchTo == en.Id, $"cycle got {back}");
});
Test("Undo after auto-fix restores text and learns 'never'", () =>
{
    var rules = new UserRules();
    var c = new Corrector(model, mapper, rules);
    c.SetLayouts([en.Info, ar.Info]);
    var t = DateTime.UtcNow;
    foreach (var s in mapper.Type("today", en)!) c.OnTextKey(s, ar.Id, t = t.AddMilliseconds(100));
    var fix = c.OnBoundary(Boundary.Space, ar.Id, t = t.AddMilliseconds(100));
    Assert(fix is { Text: "today" }, "no auto fix");
    var undo = c.OnManualFix(en.Id, t.AddMilliseconds(500));
    Assert(undo is { Kind: CorrectionKind.Undo, Backspaces: 6, Text: "فخيشغ " }, $"undo got {undo}");
    Assert(rules.IsNever("ar", "فخيشغ"), "not learned");
    foreach (var s in mapper.Type("today", en)!) c.OnTextKey(s, ar.Id, t = t.AddMilliseconds(100));
    Assert(c.OnBoundary(Boundary.Space, ar.Id, t.AddMilliseconds(100)) is null, "corrected again after undo");
});
Test("Manual fix of finished word is learned as 'always'", () =>
{
    var rules = new UserRules();
    var c = new Corrector(model, mapper, rules);
    c.SetLayouts([en.Info, ar.Info]);
    var t = DateTime.UtcNow;
    foreach (var s in mapper.Type("xyz", en)!) c.OnTextKey(s, en.Id, t = t.AddMilliseconds(100));
    Assert(c.OnBoundary(Boundary.Space, en.Id, t = t.AddMilliseconds(100)) is null, "auto changed xyz");
    var fix = c.OnManualFix(en.Id, t.AddMilliseconds(200));
    Assert(fix is { Backspaces: 4 } && fix.Text.EndsWith(' '), $"manual got {fix}");
    Assert(rules.AlwaysTarget("en", "xyz") == "ar", "not learned");
});
Test("Rules store fingerprints, not words", () =>
{
    var rules = new UserRules();
    rules.AddNever("en", "secretword");
    var (never, _) = rules.Export();
    Assert(never.Length == 1 && !never[0].Contains("secret") && never[0].Length == 24, "plain text stored");
});
Test("User .txt word list extends a language", () =>
{
    var m = new LanguageModel();
    m.AddWords("zz", ["testword", "example"]);
    Assert(m.HasLanguage("zz") && m.TryGetLoaded("zz")!.TryLookup("testword", out _), "pack loading failed");
});
Test("Decision speed: < 0.2 ms per word", () =>
{
    var c = NewCorrector(en, ar, fr);
    var strokes = mapper.Type("beautiful", en)!;
    var t = DateTime.UtcNow;
    var timer = Stopwatch.StartNew();
    for (int i = 0; i < 2000; i++)
    {
        foreach (var s in strokes) c.OnTextKey(s, ar.Id, t = t.AddMilliseconds(50));
        c.OnBoundary(Boundary.Space, ar.Id, t = t.AddMilliseconds(50));
        c.Reset();
    }
    double ms = timer.Elapsed.TotalMilliseconds / 2000;
    Console.WriteLine($"     {ms * 1000:F0} µs per word with 3 layouts");
    Assert(ms < 0.2, $"{ms:F3} ms");
});

Console.WriteLine($"{passed} passed, {failed} failed.");
Environment.Exit(failed == 0 ? 0 : 1);

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "language-packs"))) dir = dir.Parent;
    return dir?.FullName ?? throw new DirectoryNotFoundException("language-packs not found");
}
