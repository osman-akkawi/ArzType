using ArzType.Core;
using System.Globalization;
using System.Text;

namespace ArzType.App;

/// <summary>Maps physical keys to text with Windows' own layout tables (ToUnicodeEx), cached per key.</summary>
internal sealed class WindowsKeyMapper : IKeyMapper
{
    private readonly Dictionary<(nint Layout, uint Scan, bool Shift, bool Caps), string?> _cache = [];
    private readonly byte[] _state = new byte[256];
    private readonly StringBuilder _buffer = new(8);
    private readonly object _lock = new();

    public string? Map(Stroke stroke, nint layout)
    {
        var key = (layout, stroke.ScanCode, stroke.Shift, stroke.CapsLock);
        lock (_lock)
        {
            if (_cache.TryGetValue(key, out var cached)) return cached;
            string? result = null;
            uint vk = NativeMethods.MapVirtualKeyEx(stroke.ScanCode, NativeMethods.MAPVK_VSC_TO_VK_EX, layout);
            if (vk != 0)
            {
                Array.Clear(_state);
                if (stroke.Shift) { _state[NativeMethods.VK_SHIFT] = 0x80; _state[NativeMethods.VK_LSHIFT] = 0x80; }
                if (stroke.CapsLock) _state[NativeMethods.VK_CAPITAL] = 1;
                _buffer.Clear();
                // Flag 0x4: do not change the kernel's dead-key state (Windows 10 1607+).
                int n = NativeMethods.ToUnicodeEx(vk, stroke.ScanCode, _state, _buffer, 8, 0x4, layout);
                if (n > 0)
                {
                    string s = _buffer.ToString(0, Math.Min(n, _buffer.Length));
                    if (s.Length > 0 && s.All(c => c >= 0x20 && c != 0x7F)) result = s;
                }
            }
            if (_cache.Count > 20000) _cache.Clear();
            _cache[key] = result;
            return result;
        }
    }

    public void Clear() { lock (_lock) _cache.Clear(); }

    public static string Language(nint hkl)
    {
        try { return CultureInfo.GetCultureInfo((int)((long)hkl & 0xFFFF)).TwoLetterISOLanguageName; }
        catch (CultureNotFoundException) { return "und"; }
    }

    public static string LanguageName(nint hkl)
    {
        try { return CultureInfo.GetCultureInfo((int)((long)hkl & 0xFFFF)).NativeName; }
        catch (CultureNotFoundException) { return "Unknown"; }
    }

    /// <summary>Layouts driven by an IME / composition, which ArzType must not touch.</summary>
    public static bool IsComposing(string language) => language is "ja" or "ko" or "zh" or "am" or "ti" or "ii" or "chr";
}
