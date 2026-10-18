using System.Collections.Concurrent;

namespace ArzType.Core;

/// <summary>
/// Registry of offline language packs. Packs are loaded lazily (normally only the languages of the
/// keyboard layouts actually installed), so memory stays small even with 70+ languages on disk.
/// </summary>
public sealed class LanguageModel
{
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["no"] = "nb", ["nn"] = "nb", ["tl"] = "fil", ["iw"] = "he", ["in"] = "id", ["ji"] = "yi",
        ["pt-br"] = "pt", ["sh"] = "sr",
    };

    private readonly ConcurrentDictionary<string, LanguagePack> _loaded = new(StringComparer.OrdinalIgnoreCase);
    private sealed record Source(string Name, bool IsPack, Func<Stream> Open);
    private readonly Dictionary<string, List<Source>> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _loadLock = new();

    public LanguageModel(string? packDirectory = null)
    {
        AddDirectory(packDirectory);
    }

    /// <summary>Registers every <c>xx.lpk</c> pack and <c>xx.txt</c> word list in a folder.</summary>
    public void AddDirectory(string? packDirectory)
    {
        if (packDirectory is null || !Directory.Exists(packDirectory)) return;
        foreach (string path in Directory.EnumerateFiles(packDirectory))
            AddSource(Path.GetFileName(path), () => File.OpenRead(path));
    }

    /// <summary>Registers a pack from any stream source (e.g. resources embedded in the executable).
    /// Packs registered earlier win; .txt word lists are always merged.</summary>
    public void AddSource(string fileName, Func<Stream> open)
    {
        string ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (ext is not (".lpk" or ".txt")) return;
        string tag = Root(Path.GetFileNameWithoutExtension(fileName));
        if (!IsSafeTag(tag)) return;
        if (!_files.TryGetValue(tag, out var list)) _files[tag] = list = [];
        list.Add(new Source(fileName, ext == ".lpk", open));
    }

    /// <summary>Languages that have a pack on disk or in memory.</summary>
    public IReadOnlyCollection<string> Available => _files.Keys.Union(_loaded.Keys, StringComparer.OrdinalIgnoreCase).ToArray();

    public static string Root(string tag)
    {
        string root = tag.Split('-', '_')[0].ToLowerInvariant();
        return Aliases.TryGetValue(root, out var a) ? a : root;
    }

    private static bool IsSafeTag(string tag) => tag.Length is >= 2 and <= 8 && tag.All(char.IsAsciiLetterLower);

    public bool HasLanguage(string language)
    {
        string root = Root(language);
        return _loaded.ContainsKey(root) || _files.ContainsKey(root);
    }

    /// <summary>Non-blocking: returns the pack only when it is already in memory (safe in keyboard hooks).</summary>
    public LanguagePack? TryGetLoaded(string language) =>
        _loaded.TryGetValue(Root(language), out var p) ? p : null;

    /// <summary>Blocking load (use from a background thread).</summary>
    public LanguagePack? Get(string language)
    {
        string root = Root(language);
        if (_loaded.TryGetValue(root, out var pack)) return pack;
        if (!_files.TryGetValue(root, out var files)) return null;
        lock (_loadLock)
        {
            if (_loaded.TryGetValue(root, out pack)) return pack;
            foreach (var source in files.OrderBy(f => f.IsPack ? 0 : 1))
            {
                try
                {
                    if (source.IsPack)
                    {
                        if (pack is null) { using var s = source.Open(); pack = LanguagePack.Load(s); } // first pack wins
                    }
                    else
                    {
                        using var s = source.Open();
                        using var reader = new StreamReader(s);
                        var words = new List<(string, long)>();
                        string? line;
                        while ((line = reader.ReadLine()) is not null)
                            if (!line.StartsWith('#')) words.Add((line, 0L));
                        if (pack is null) pack = LanguagePack.FromEntries(root, words);
                        else pack.Add(words);
                    }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException) { }
            }
            if (pack is not null) _loaded[root] = pack;
            return pack;
        }
    }

    public void Preload(IEnumerable<string> languages)
    {
        foreach (var l in languages.Select(Root).Distinct()) Get(l);
    }

    /// <summary>Adds words to a language in memory. Not thread-safe against concurrent scoring:
    /// use before the pack is in use (tests, start-up). User .txt lists are merged in <see cref="Get"/>.</summary>
    public void AddWords(string language, IEnumerable<string> words)
    {
        string root = Root(language);
        if (!IsSafeTag(root)) throw new ArgumentException("Invalid language tag", nameof(language));
        var pack = Get(root);
        var entries = words.Select(w => (w, 0L)).ToList();
        if (pack is null) _loaded[root] = LanguagePack.FromEntries(root, entries);
        else lock (_loadLock) pack.Add(entries);
    }

    /// <summary>Registers an already-built pack (tests and benchmarks).</summary>
    public void Register(LanguagePack pack) => _loaded[Root(pack.Language)] = pack;
}
