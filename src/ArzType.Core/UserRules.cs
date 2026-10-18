using System.Security.Cryptography;
using System.Text;

namespace ArzType.Core;

/// <summary>
/// What ArzType learned from you: words you undid ("never change this") and words you fixed by hand
/// ("always change this"). Only salted SHA-256 fingerprints are kept — never the words themselves.
/// Note that a fingerprint of a very short word could still be guessed by brute force; the list can be
/// cleared at any time from the settings window.
/// </summary>
public sealed class UserRules
{
    private readonly object _lock = new();
    private readonly HashSet<string> _never = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _always = new(StringComparer.Ordinal);
    public event Action? Changed;

    public static string Fingerprint(string language, string text)
    {
        string input = "ArzType/1\0" + LanguageModel.Root(language) + "\0" + Text.NormalizeWord(text);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)), 0, 12);
    }

    public bool IsNever(string language, string text)
    {
        lock (_lock) return _never.Contains(Fingerprint(language, text));
    }

    public string? AlwaysTarget(string language, string text)
    {
        lock (_lock) return _always.TryGetValue(Fingerprint(language, text), out var t) ? t : null;
    }

    public void AddNever(string language, string text)
    {
        string k = Fingerprint(language, text);
        lock (_lock) { _never.Add(k); _always.Remove(k); }
        Changed?.Invoke();
    }

    public void AddAlways(string language, string text, string targetLanguage)
    {
        if (Text.CodePoints(text) < 2) return;
        string k = Fingerprint(language, text);
        lock (_lock) { _always[k] = LanguageModel.Root(targetLanguage); _never.Remove(k); }
        Changed?.Invoke();
    }

    public void Forget(string language, string text)
    {
        string k = Fingerprint(language, text);
        lock (_lock) { _always.Remove(k); _never.Remove(k); }
        Changed?.Invoke();
    }

    public int Count { get { lock (_lock) return _never.Count + _always.Count; } }

    public void Clear()
    {
        lock (_lock) { _never.Clear(); _always.Clear(); }
        Changed?.Invoke();
    }

    public (string[] Never, Dictionary<string, string> Always) Export()
    {
        lock (_lock) return (_never.ToArray(), new Dictionary<string, string>(_always));
    }

    public void Import(IEnumerable<string>? never, IDictionary<string, string>? always)
    {
        lock (_lock)
        {
            foreach (var n in never ?? []) if (n.Length == 24) _never.Add(n);
            foreach (var (k, v) in always ?? new Dictionary<string, string>()) if (k.Length == 24) _always[k] = v;
        }
    }
}
