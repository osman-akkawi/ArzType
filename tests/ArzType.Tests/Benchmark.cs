using ArzType.Core;

namespace ArzType.Tests;

/// <summary>
/// Accuracy benchmark on real Windows layouts. For each layout pair it types real words of both
/// languages on the right and on the wrong layout and counts what the engine does. Packs are loaded
/// with 1 in 10 rarer words held out, so "unseen" words test the n-gram model honestly.
/// Usage: dotnet run -c Release --project tests/ArzType.Tests -- bench [samples]
/// </summary>
public static class Benchmark
{
    private static readonly (string A, string B)[] Pairs =
    [
        ("en-t-k0-windows", "ar-t-k0-windows"), ("en-t-k0-windows", "fr-t-k0-windows"),
        ("fr-t-k0-windows", "ar-t-k0-windows"), ("en-t-k0-windows", "ru-t-k0-windows"),
        ("en-t-k0-windows", "uk-t-k0-windows"), ("en-t-k0-windows", "de-t-k0-windows"),
        ("en-t-k0-windows", "he-t-k0-windows"), ("en-t-k0-windows", "fa-t-k0-windows"),
        ("en-t-k0-windows", "el-t-k0-windows"), ("en-t-k0-windows", "tr-t-k0-windows"),
        ("en-t-k0-windows", "bg-t-k0-windows"), ("en-t-k0-windows", "th-t-k0-windows"),
        ("en-t-k0-windows", "ka-t-k0-windows"), ("en-t-k0-windows", "hy-t-k0-windows"),
        ("en-t-k0-windows", "es-t-k0-windows"), ("en-t-k0-windows", "pl-t-k0-windows"),
        ("en-t-k0-windows", "ur-t-k0-windows"), ("en-t-k0-windows", "hi-t-k0-windows"),
        ("en-t-k0-windows", "kk-t-k0-windows"), ("en-t-k0-windows", "be-t-k0-windows"),
        ("en-t-k0-windows", "sr-t-k0-windows"), ("en-t-k0-windows", "mn-t-k0-windows"),
        ("ru-t-k0-windows", "uk-t-k0-windows"),
    ];

    public static void Run(string packDir, CldrMapper mapper, string[] args)
    {
        int samples = args.Length > 0 ? int.Parse(args[0]) : 1500;
        var model = new LanguageModel();
        var heldOut = new Dictionary<string, List<string>>();
        var packs = new Dictionary<string, LanguagePack>();
        foreach (var lang in Pairs.SelectMany(p => new[] { mapper[p.A].Lang, mapper[p.B].Lang }).Distinct())
        {
            var list = new List<string>();
            var path = Path.Combine(packDir, LanguageModel.Root(lang) + ".lpk");
            if (!File.Exists(path)) { Console.WriteLine($"no pack for {lang}"); continue; }
            var pack = LanguagePack.Load(path, holdoutEvery: 10, heldOut: list);
            model.Register(pack);
            packs[LanguageModel.Root(lang)] = pack;
            heldOut[LanguageModel.Root(lang)] = list;
        }
        var rnd = new Random(42);
        Console.WriteLine($"{"pair",-12} {"lang",-4} | {"wrong-layout fixed",18} {"to wrong lang",13} | {"right-layout changed (FP)",26} | {"unseen fixed",12} {"unseen FP",9}");
        double tpSum = 0, fpSum = 0, fpUnseenSum = 0; int rows = 0;
        foreach (var (a, b) in Pairs)
        {
            var la = mapper[a]; var lb = mapper[b];
            foreach (var (intended, other) in new[] { (la, lb), (lb, la) })
            {
                string root = LanguageModel.Root(intended.Lang);
                if (!packs.TryGetValue(root, out var pack)) continue;
                var common = SampleByFrequency(packDir, root, samples, rnd);
                var unseen = heldOut[root].OrderBy(_ => rnd.Next()).Take(samples).ToList();
                var (fixedOk, fixedWrong, n1) = Measure(model, mapper, common, intended, other, wrongLayout: true);
                var (fp, _, n2) = Measure(model, mapper, common, intended, other, wrongLayout: false);
                var (uFix, _, n3) = Measure(model, mapper, unseen, intended, other, wrongLayout: true);
                var (uFp, _, n4) = Measure(model, mapper, unseen, intended, other, wrongLayout: false);
                Console.WriteLine($"{la.Lang + "/" + lb.Lang,-12} {intended.Lang,-4} | {Pct(fixedOk, n1),18} {Pct(fixedWrong, n1),13} | {Pct(fp, n2),26} | {Pct(uFix, n3),12} {Pct(uFp, n4),9}");
                tpSum += (double)fixedOk / Math.Max(1, n1); fpSum += (double)fp / Math.Max(1, n2); fpUnseenSum += (double)uFp / Math.Max(1, n4); rows++;
            }
        }
        Console.WriteLine();
        Console.WriteLine("Sentences of 6 frequency-sampled words typed entirely on the wrong layout:");
        double fullSum = 0; int srows = 0;
        foreach (var (a, b) in Pairs)
        {
            foreach (var (intended, other) in new[] { (mapper[a], mapper[b]), (mapper[b], mapper[a]) })
            {
                string root = LanguageModel.Root(intended.Lang);
                if (!packs.ContainsKey(root)) continue;
                var pool = SampleByFrequency(packDir, root, 4000, rnd).Where(w => mapper.Type(w, intended) is not null).ToList();
                int full = 0, total = 0, rightSentences = 0, rightChanged = 0;
                for (int k = 0; k + 6 <= pool.Count && total < 300; k += 6)
                {
                    var words = pool.GetRange(k, 6);
                    if (words.All(w => string.Concat(mapper.Type(w, intended)!.Select(st => mapper.Map(st, other.Id))) == w)) continue;
                    total++;
                    if (TypeSentence(model, mapper, words, intended, other, other) == string.Join(" ", words)) full++;
                    rightSentences++;
                    if (TypeSentence(model, mapper, words, intended, intended, other) != string.Join(" ", words)) rightChanged++;
                }
                Console.WriteLine($"  {intended.Lang,-4} on {other.Lang,-4} layout: fully fixed {Pct(full, total)}   | typed on right layout, altered {Pct(rightChanged, rightSentences)}");
                fullSum += (double)full / Math.Max(1, total); srows++;
            }
        }
        Console.WriteLine($"SENTENCES fully fixed on average: {100 * fullSum / srows:F1}%");
        Console.WriteLine($"AVERAGE: fixed {100 * tpSum / rows:F1}% of wrong-layout words, false changes {100 * fpSum / rows:F2}% (common) / {100 * fpUnseenSum / rows:F2}% (unseen words)");
    }

    private static string TypeSentence(LanguageModel model, CldrMapper mapper, List<string> words, CldrMapper.Layout intended, CldrMapper.Layout active, CldrMapper.Layout other)
    {
        var c = new Corrector(model, mapper);
        c.SetLayouts([intended.Info, other.Info]);
        var screen = new System.Text.StringBuilder();
        nint layout = active.Id;
        var t = new DateTime(2026, 1, 1);
        foreach (var w in words)
        {
            foreach (var st in mapper.Type(w, intended)!)
            {
                c.OnTextKey(st, layout, t = t.AddMilliseconds(100));
                screen.Append(mapper.Map(st, layout));
            }
            var fix = c.OnBoundary(Boundary.Space, layout, t = t.AddMilliseconds(100));
            if (fix is not null)
            {
                string cur = screen.ToString();
                int remove = 0, cps = 0;
                for (int i = cur.Length - 1; i >= 0 && cps < fix.Backspaces; i--) { remove++; if (!char.IsLowSurrogate(cur[i])) cps++; }
                screen.Length -= remove;
                screen.Append(fix.Text);
                layout = fix.SwitchTo;
            }
            screen.Append(' ');
        }
        return screen.ToString().TrimEnd();
    }

    private static string Pct(int k, int n) => n == 0 ? "-" : $"{100.0 * k / n:F2}% ({n})";

    /// <summary>Words that differ between the two layouts, judged one at a time after a space.</summary>
    private static (int Good, int Bad, int N) Measure(LanguageModel model, CldrMapper mapper, List<string> words,
        CldrMapper.Layout intended, CldrMapper.Layout other, bool wrongLayout)
    {
        var c = new Corrector(model, mapper);
        c.SetLayouts([intended.Info, other.Info]);
        var active = wrongLayout ? other : intended;
        int good = 0, bad = 0, n = 0;
        var t = new DateTime(2026, 1, 1);
        foreach (var w in words)
        {
            var strokes = mapper.Type(w, intended);
            if (strokes is null) continue;
            string? there = string.Concat(strokes.Select(s => mapper.Map(s, other.Id) ?? "\0"));
            if (there.Contains('\0') || there == w) continue; // identical or untypeable on the other layout
            n++;
            c.Reset();
            foreach (var s in strokes) c.OnTextKey(s, active.Id, t = t.AddMilliseconds(100));
            var fix = c.OnBoundary(Boundary.Space, active.Id, t = t.AddMilliseconds(100));
            if (Environment.GetEnvironmentVariable("ARZ_DUMP") == intended.Lang + (wrongLayout ? "+" : "-") && (fix is null) == wrongLayout)
                Console.Error.WriteLine($"  {(wrongLayout ? "miss" : "FP")}: {w} [{there}]");
            if (wrongLayout)
            {
                if (fix?.SwitchTo == intended.Id) good++;
                else if (fix is not null) bad++;
            }
            else if (fix is not null) good++;
        }
        return (good, bad, n);
    }

    private static List<string> SampleByFrequency(string packDir, string root, int n, Random rnd)
    {
        // Token-weighted sample of the pack (what people actually type most).
        using var gz = new System.IO.Compression.GZipStream(File.OpenRead(Path.Combine(packDir, root + ".lpk")), System.IO.Compression.CompressionMode.Decompress);
        using var r = new StreamReader(gz);
        r.ReadLine();
        var words = new List<(string W, double C)>();
        string? line;
        while ((line = r.ReadLine()) is not null)
        {
            var p = line.Split('\t');
            words.Add((p[0], Math.Max(1, double.Parse(p[1]))));
        }
        double total = words.Sum(x => x.C);
        var cumulative = new double[words.Count];
        double acc = 0;
        for (int i = 0; i < words.Count; i++) { acc += words[i].C; cumulative[i] = acc; }
        var result = new List<string>(n);
        for (int i = 0; i < n; i++)
        {
            double x = rnd.NextDouble() * total;
            int idx = Array.BinarySearch(cumulative, x);
            if (idx < 0) idx = ~idx;
            result.Add(words[Math.Min(idx, words.Count - 1)].W);
        }
        return result;
    }
}
