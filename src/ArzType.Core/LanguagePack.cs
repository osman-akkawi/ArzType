using System.IO.Compression;
using System.Text;

namespace ArzType.Core;

/// <summary>
/// One language: a frequency-ranked lexicon plus a character 4-gram model (Witten-Bell
/// interpolation) trained from that lexicon at load time. The n-gram model is what lets
/// ArzType judge words it has never seen: "ghbdtn" is impossible English, "привет" is
/// very likely Russian, even for names and slang that are in no dictionary.
/// </summary>
public sealed class LanguagePack
{
    public const int Order = 4;
    private const char Bos = '\u0002', Eos = '\u0003';
    private const double FloorVocabulary = 4096; // probability mass for characters never seen

    private readonly Dictionary<string, (float LogP, int Rank)> _lexicon = new(StringComparer.Ordinal);
    private readonly Dictionary<ulong, float>[] _grams = new Dictionary<ulong, float>[Order + 1];
    private readonly Dictionary<ulong, (float Total, int Types)>[] _contexts = new Dictionary<ulong, (float, int)>[Order];
    private double _unigramTotal;
    private int _unigramTypes;

    public string Language { get; }
    public bool HasFrequencies { get; private set; }
    public int WordCount => _lexicon.Count;
    /// <summary>Weight of the lexicon versus the n-gram model in the word probability mixture.</summary>
    public double LexiconWeight => HasFrequencies ? 0.85 : 0.6;

    private LanguagePack(string language)
    {
        Language = language;
        for (int i = 0; i <= Order; i++) _grams[i] = new();
        for (int i = 0; i < Order; i++) _contexts[i] = new();
    }

    /// <summary>Loads a gzip .lpk pack. <paramref name="heldOut"/> (benchmarks only) receives
    /// every <paramref name="holdoutEvery"/>-th word ranked beyond 3000, which is excluded from training.</summary>
    public static LanguagePack Load(string path, int holdoutEvery = 0, List<string>? heldOut = null)
    {
        using var file = File.OpenRead(path);
        return Load(file, holdoutEvery, heldOut);
    }

    public static LanguagePack Load(Stream file, int holdoutEvery = 0, List<string>? heldOut = null)
    {
        using var gz = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new StreamReader(gz, Encoding.UTF8);
        string header = reader.ReadLine() ?? throw new InvalidDataException("Empty pack");
        var parts = header.Split(' ');
        if (parts.Length < 4 || parts[0] != "#ArzType-LP") throw new InvalidDataException("Not an ArzType pack");
        var entries = new List<(string Word, long Count)>(50000);
        string? line;
        int index = 0;
        while ((line = reader.ReadLine()) is not null)
        {
            int tab = line.IndexOf('\t');
            if (tab <= 0) continue;
            string word = line[..tab];
            long.TryParse(line.AsSpan(tab + 1), out long count);
            index++;
            if (holdoutEvery > 0 && index > 3000 && index % holdoutEvery == 0) { heldOut?.Add(word); continue; }
            entries.Add((word, count));
        }
        return FromEntries(parts[2], entries);
    }

    public static LanguagePack FromWords(string language, IEnumerable<string> words) =>
        FromEntries(language, words.Select(w => (w, 0L)));

    public static LanguagePack FromEntries(string language, IEnumerable<(string Word, long Count)> entries)
    {
        var pack = new LanguagePack(language);
        pack.Add(entries);
        return pack;
    }

    /// <summary>Adds words (count 0 = unknown frequency). Used by packs and by user word lists.</summary>
    public void Add(IEnumerable<(string Word, long Count)> entries)
    {
        var list = new List<(string Word, long Count)>();
        foreach (var (raw, count) in entries)
        {
            string w = Text.NormalizeWord(raw);
            if (w.Length is >= 1 and <= 40 && Text.IsWordCore(w)) list.Add((w, count));
        }
        if (list.Count == 0) return;
        bool freq = list.Any(e => e.Count > 0);
        HasFrequencies |= freq;

        // Existing lexicon probabilities are recomputed from scratch over the union.
        var all = _rawCounts;
        long minCount = Math.Max(1, list.Where(e => e.Count > 0).Select(e => e.Count).DefaultIfEmpty(1).Min());
        foreach (var (w, c) in list)
        {
            long value = c > 0 ? c : minCount;
            all[w] = all.TryGetValue(w, out long old) ? Math.Max(old, value) : value;
            Train(w, HasFrequencies ? Math.Log(Math.E + value) : 1.0);
        }
        double total = all.Values.Sum(v => (double)v);
        int rank = 0;
        _lexicon.Clear();
        foreach (var (w, c) in all.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal))
            _lexicon[w] = ((float)Math.Log(c / total), ++rank);
    }

    private readonly Dictionary<string, long> _rawCounts = new(StringComparer.Ordinal);

    private void Train(string word, double weight)
    {
        string s = Bos + word + Eos;
        for (int i = 1; i < s.Length; i++)
        {
            char c = s[i];
            for (int k = 0; k < Order && k <= i; k++)
            {
                // context = s[i-k .. i-1], k chars (k = 0 → unigram)
                ulong ctx = Pack(s, i - k, k);
                ulong gram = ctx | ((ulong)c << (16 * k));
                var grams = _grams[k + 1];
                bool isNew = !grams.TryGetValue(gram, out float g);
                grams[gram] = g + (float)weight;
                if (k == 0)
                {
                    _unigramTotal += weight;
                    if (isNew) _unigramTypes++;
                }
                else
                {
                    var ctxs = _contexts[k];
                    ctxs.TryGetValue(ctx, out var info);
                    ctxs[ctx] = (info.Total + (float)weight, info.Types + (isNew ? 1 : 0));
                }
            }
        }
    }

    private static ulong Pack(string s, int start, int length)
    {
        ulong key = 0;
        for (int j = 0; j < length; j++) key |= (ulong)s[start + j] << (16 * j);
        return key;
    }

    /// <summary>Natural-log probability of a lowercase word under the character model alone.</summary>
    public double NGramLogProb(string word)
    {
        string s = Bos + word + Eos;
        double logp = 0;
        for (int i = 1; i < s.Length; i++) logp += Math.Log(CharProb(s, i));
        return logp;
    }

    private double CharProb(string s, int i)
    {
        char c = s[i];
        _grams[1].TryGetValue(c, out float uni);
        double p = (uni + _unigramTypes / FloorVocabulary) / (_unigramTotal + _unigramTypes);
        for (int k = 1; k < Order && k <= i; k++)
        {
            ulong ctx = Pack(s, i - k, k);
            if (!_contexts[k].TryGetValue(ctx, out var info)) break;
            _grams[k + 1].TryGetValue(ctx | ((ulong)c << (16 * k)), out float g);
            p = (g + info.Types * p) / (info.Total + info.Types);
        }
        return p;
    }

    public bool TryLookup(string word, out int rank)
    {
        if (_lexicon.TryGetValue(word, out var e)) { rank = e.Rank; return true; }
        rank = 0;
        return false;
    }

    /// <summary>ln P(word) under the lexicon/n-gram mixture.</summary>
    public double WordLogProb(string word, out bool inLexicon, out int rank)
    {
        double ng = NGramLogProb(word);
        inLexicon = _lexicon.TryGetValue(word, out var e);
        rank = inLexicon ? e.Rank : 0;
        double a = LexiconWeight;
        if (!inLexicon) return Math.Log(1 - a) + ng;
        // log(a*e^lex + (1-a)*e^ng) computed stably
        double x = Math.Log(a) + e.LogP, y = Math.Log(1 - a) + ng;
        double m = Math.Max(x, y);
        return m + Math.Log(Math.Exp(x - m) + Math.Exp(y - m));
    }
}
