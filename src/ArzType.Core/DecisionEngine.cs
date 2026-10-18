namespace ArzType.Core;

public sealed record LayoutInfo(nint Id, string Language, string DisplayName = "")
{
    public string Root => LanguageModel.Root(Language);
}

public readonly record struct TokenScore(double LogProb, int CoreLength, bool InLexicon, int Rank, bool HasCore, int Junk,
    bool SuspiciousLead = false);

public sealed record Hypothesis(LayoutInfo Layout, string Text, TokenScore Score, double Llr);

public enum VerdictKind { KeepCurrent, Ambiguous, Wrong }

public sealed record WordVerdict(VerdictKind Kind, Hypothesis Current, IReadOnlyList<Hypothesis> Ranked)
{
    public Hypothesis? Best => Ranked.Count > 0 ? Ranked[0] : null;
    public Hypothesis? For(nint layout) => Ranked.FirstOrDefault(h => h.Layout.Id == layout);
}

/// <summary>Thresholds, in natural-log likelihood ratio units (6 ≈ 400:1 odds).</summary>
public sealed class DecisionOptions
{
    public double Threshold { get; set; } = 6.0;          // words of 4+ letters
    public double Threshold3 { get; set; } = 7.5;         // 3-letter words
    public double Threshold2 { get; set; } = 9.0;         // 2-letter words (must be common words)
    public int ShortWordMaxRank { get; set; } = 3000;     // 2-letter target must be this common
    public double OovExtra { get; set; } = 3.0;           // extra evidence when the target is not a dictionary word
    public int OovMinLength { get; set; } = 5;
    public double AmbiguousFloor { get; set; } = 1.5;
    public double PhraseThreshold { get; set; } = 9.0;
    public double PhraseWordFloor { get; set; } = 1.0;
    public double RetroactiveFloor { get; set; } = 3.0;   // a "valid" previous word re-read in the target language
    public double Margin { get; set; } = 1.5;             // best vs runner-up language
    public int TrustedRank { get; set; } = 40000;         // current word this common → never touched
    public int TrustedRank2 { get; set; } = 1500;         // …for 1–2 letter words (subtitle lists contain junk like "td")
    public int TrustedRank3 { get; set; } = 8000;
    /// <summary>Sensitivity -2 (careful) … +2 (aggressive) shifts all thresholds.</summary>
    public double Sensitivity { get; set; }
}

/// <summary>
/// Compares hypotheses "the user meant language A on layout A" for the same physical keys.
/// Score = ln P_A(text typed under layout A) − ln P_current(text under the current layout).
/// </summary>
public sealed class DecisionEngine(LanguageModel model, DecisionOptions? options = null)
{
    private const string ClosingChars = ".,!?:;\"')»›”’…";
    private const string OpeningChars = "\"'([{«‹„“‘¿¡<";
    private const double LnMixedCase = -6.0;
    private const double LnLead = -4.6, LnTrail = -3.5, LnInner = -7.6, LnUnknownChar = -3.4;
    public DecisionOptions Options { get; } = options ?? new DecisionOptions();

    public static TokenScore Score(LanguagePack? pack, string rendered)
    {
        string t = Text.NormalizeWord(rendered);
        // "hE", "tO": odd capitals mid-word mean Shift was used for another layout's letters.
        double casePenalty = 0;
        for (int i = 1; i < rendered.Length; i++)
            if (char.IsUpper(rendered[i]) && char.IsLower(rendered[i - 1])) casePenalty += LnMixedCase;
        int lead = 0, trail = 0;
        while (lead < t.Length && !Text.IsWordChar(t[lead])) lead++;
        while (trail < t.Length - lead && !Text.IsWordChar(t[t.Length - 1 - trail])) trail++;
        if (lead == t.Length) return new(LnInner * Math.Max(1, t.Length), 0, false, 0, false, t.Length);
        bool suspiciousLead = false;
        for (int i = 0; i < lead; i++) if (!OpeningChars.Contains(t[i])) suspiciousLead = true;

        string core = t.Substring(lead, t.Length - lead - trail);
        double logp = casePenalty;
        for (int i = 0; i < lead; i++) logp += OpeningChars.Contains(t[i]) ? LnLead : LnInner;
        for (int i = t.Length - trail; i < t.Length; i++) logp += ClosingChars.Contains(t[i]) ? LnTrail : LnInner;
        int junk = lead + trail, letters = 0;
        bool inLex = false;
        int rank = 0, segments = 0, start = 0;
        for (int i = 0; i <= core.Length; i++)
        {
            bool end = i == core.Length;
            if (!end && (Text.IsWordChar(core[i]) || Text.IsInnerWordChar(core[i]) && i > start && i + 1 < core.Length && Text.IsWordChar(core[i + 1])))
                continue;
            if (i > start)
            {
                string seg = core[start..i];
                letters += seg.Length;
                segments++;
                if (pack is null) logp += LnUnknownChar * (seg.Length + 1);
                else
                {
                    logp += pack.WordLogProb(seg, out bool hit, out int r);
                    if (segments == 1) { inLex = hit; rank = r; }
                }
            }
            if (!end) { logp += LnInner; junk++; }
            start = i + 1;
        }
        if (segments != 1) inLex = false;
        return new(logp, letters, inLex, rank, true, junk, suspiciousLead || segments != 1);
    }

    public WordVerdict Judge(LayoutInfo current, string currentText, IEnumerable<(LayoutInfo Layout, string? Text)> alternatives)
    {
        var curPack = model.TryGetLoaded(current.Language);
        var cur = Score(curPack, currentText);
        var curHyp = new Hypothesis(current, currentText, cur, 0);
        var ranked = new List<Hypothesis>();
        foreach (var (layout, text) in alternatives)
        {
            if (text is null || layout.Id == current.Id || layout.Root == current.Root) continue;
            if (string.Equals(Text.NormalizeWord(text), Text.NormalizeWord(currentText), StringComparison.Ordinal)) continue;
            var pack = model.TryGetLoaded(layout.Language);
            if (pack is null) continue;
            var s = Score(pack, text);
            if (!s.HasCore) continue;
            ranked.Add(new(layout, text, s, s.LogProb - cur.LogProb));
        }
        ranked.Sort((a, b) => b.Llr.CompareTo(a.Llr));

        int trusted = cur.CoreLength switch { <= 2 => Options.TrustedRank2, 3 => Options.TrustedRank3, _ => Options.TrustedRank };
        if (!cur.HasCore || cur.InLexicon && !cur.SuspiciousLead && (cur.Rank <= trusted || curPack is { HasFrequencies: false }))
            return new(VerdictKind.KeepCurrent, curHyp, ranked);
        if (ranked.Count == 0) return new(VerdictKind.KeepCurrent, curHyp, ranked);

        var best = ranked[0];
        double shift = -Options.Sensitivity;
        double need = best.Score.CoreLength switch
        {
            <= 1 => double.PositiveInfinity,
            2 => Options.Threshold2,
            3 => Options.Threshold3,
            _ => Options.Threshold
        } + shift;
        bool strongWord = best.Score.InLexicon && best.Score.Junk == 0 &&
            (best.Score.CoreLength > 2 || best.Score.Rank <= Options.ShortWordMaxRank);
        if (!strongWord)
            need = best.Score.CoreLength >= Options.OovMinLength && best.Score.Junk <= 1
                ? need + Options.OovExtra : double.PositiveInfinity;
        bool clearWinner = ranked.Count == 1 || best.Llr - ranked[1].Llr >= Options.Margin;

        if (best.Llr >= need && clearWinner) return new(VerdictKind.Wrong, curHyp, ranked);
        if (best.Llr >= Options.AmbiguousFloor + shift) return new(VerdictKind.Ambiguous, curHyp, ranked);
        return new(VerdictKind.KeepCurrent, curHyp, ranked);
    }
}
