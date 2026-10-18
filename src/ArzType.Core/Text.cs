using System.Globalization;
using System.Text;

namespace ArzType.Core;

public static class Text
{
    public static bool IsWordChar(char c)
    {
        var cat = char.GetUnicodeCategory(c);
        return cat is UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or
            UnicodeCategory.TitlecaseLetter or UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter or
            UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark ||
            char.IsSurrogate(c);
    }

    /// <summary>Joiners and apostrophes count as letters only inside a word.</summary>
    public static bool IsInnerWordChar(char c) => c is '\'' or '‌' or '‍';

    public static bool IsWordCore(string w)
    {
        if (w.Length == 0) return false;
        for (int i = 0; i < w.Length; i++)
        {
            char c = w[i];
            if (IsWordChar(c)) continue;
            if (IsInnerWordChar(c) && i > 0 && i < w.Length - 1) continue;
            return false;
        }
        return true;
    }

    public static string NormalizeWord(string w)
    {
        w = w.Trim();
        if (!w.IsNormalized(NormalizationForm.FormC)) w = w.Normalize(NormalizationForm.FormC);
        w = w.ToLowerInvariant();
        if (w.Contains('’') || w.Contains('ʼ')) w = w.Replace('’', '\'').Replace('ʼ', '\'');
        return w;
    }

    /// <summary>Number of Backspace presses needed to delete <paramref name="s"/> (one per code point).</summary>
    public static int CodePoints(string s)
    {
        int n = 0;
        foreach (char c in s) if (!char.IsLowSurrogate(c)) n++;
        return n;
    }
}
