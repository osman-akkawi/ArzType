using System.Text.Json;
using ArzType.Core;

namespace ArzType.Tests;

/// <summary>Real Windows key maps (from Unicode CLDR) so the engine can be tested without Windows.</summary>
public sealed class CldrMapper : IKeyMapper
{
    public sealed record Layout(nint Id, string Locale, string Name, string Lang,
        Dictionary<uint, string?> Base, Dictionary<uint, string?> Shift)
    {
        public LayoutInfo Info => new(Id, Lang, Name);
    }

    private readonly Dictionary<nint, Layout> _byId = [];
    public Dictionary<string, Layout> ByLocale { get; } = [];

    public CldrMapper(string fixturePath)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(fixturePath));
        nint id = 1;
        foreach (var kb in doc.RootElement.EnumerateObject())
        {
            static Dictionary<uint, string?> Read(JsonElement e)
            {
                var d = new Dictionary<uint, string?>();
                foreach (var p in e.EnumerateObject())
                    d[Convert.ToUInt32(p.Name, 16)] = p.Value.ValueKind == JsonValueKind.Null ? null : p.Value.GetString();
                return d;
            }
            var v = kb.Value;
            var layout = new Layout(id++, kb.Name, v.GetProperty("name").GetString()!, v.GetProperty("lang").GetString()!,
                Read(v.GetProperty("base")), Read(v.GetProperty("shift")));
            _byId[layout.Id] = layout;
            ByLocale[kb.Name] = layout;
        }
    }

    public Layout this[string locale] => ByLocale[locale];

    public string? Map(Stroke stroke, nint layout)
    {
        if (!_byId.TryGetValue(layout, out var l)) return null;
        var map = stroke.Shift ? l.Shift : l.Base;
        if (!map.TryGetValue(stroke.ScanCode, out var s) || s is null) return null;
        if (stroke.CapsLock && s.Length > 0 && char.IsLetter(s[0]))
            s = stroke.Shift ? s.ToLowerInvariant() : s.ToUpperInvariant();
        return s;
    }

    /// <summary>Keys a person would press on <paramref name="layout"/> to type <paramref name="word"/>, or null if impossible.</summary>
    public Stroke[]? Type(string word, Layout layout)
    {
        var reverse = new Dictionary<string, Stroke>(StringComparer.Ordinal);
        foreach (var (scan, s) in layout.Base) if (s is { Length: > 0 }) reverse.TryAdd(s, new Stroke(scan, false, false));
        foreach (var (scan, s) in layout.Shift) if (s is { Length: > 0 }) reverse.TryAdd(s, new Stroke(scan, true, false));
        var result = new List<Stroke>();
        int i = 0;
        while (i < word.Length)
        {
            // Prefer the longest key output (e.g. Arabic lam-alef is one key producing two letters).
            if (i + 1 < word.Length && reverse.TryGetValue(word.Substring(i, 2), out var two)) { result.Add(two); i += 2; continue; }
            if (!reverse.TryGetValue(word[i].ToString(), out var one)) return null;
            result.Add(one);
            i++;
        }
        return result.ToArray();
    }
}
