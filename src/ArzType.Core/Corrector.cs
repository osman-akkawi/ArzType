namespace ArzType.Core;

public enum Boundary { Space, Enter }
public enum CorrectionKind { Auto, Manual, Undo }

/// <summary>
/// An edit for the platform layer to perform: press Backspace <see cref="Backspaces"/> times, type
/// <see cref="Text"/>, then re-type the swallowed <see cref="Boundary"/> key (if any), and switch the
/// focused window to <see cref="SwitchTo"/>.
/// </summary>
public sealed record Correction(int Backspaces, string Text, Boundary? Boundary, nint SwitchTo,
    CorrectionKind Kind, string Before, string After, string FromLanguage, string ToLanguage);

/// <summary>
/// Platform-independent typing state machine. The Windows hook feeds it physical keys; it answers
/// with <see cref="Correction"/>s. Keeps at most a few words of key codes in memory, never on disk.
/// </summary>
public sealed class Corrector
{
    private sealed class Word(Stroke[] strokes, nint layout)
    {
        public Stroke[] Strokes { get; } = strokes;
        public nint Layout { get; } = layout;
        public WordVerdict? Verdict { get; set; }
    }

    private sealed record Edit(CorrectionKind Kind, Word[] Words, nint From, nint ShownLayout,
        bool WithSpace, DateTime At, IReadOnlyList<nint> Cycle, int CycleIndex);

    private readonly LanguageModel _model;
    private readonly IKeyMapper _mapper;
    private readonly UserRules _rules;
    private readonly DecisionEngine _engine;
    private IReadOnlyList<LayoutInfo> _layouts = [];
    private readonly List<Stroke> _current = [];
    private readonly List<Word> _pending = [];
    private Word? _lastWord;
    private nint _layout;
    private DateTime _lastKey;
    private Edit? _edit;

    public Corrector(LanguageModel model, IKeyMapper mapper, UserRules? rules = null, DecisionOptions? options = null)
    {
        _model = model;
        _mapper = mapper;
        _rules = rules ?? new UserRules();
        _engine = new DecisionEngine(model, options);
    }

    public DecisionOptions Options => _engine.Options;
    public bool AutoCorrect { get; set; } = true;
    public bool LearnFromManualFixes { get; set; } = true;
    public TimeSpan IdleReset { get; set; } = TimeSpan.FromSeconds(8);
    public TimeSpan UndoWindow { get; set; } = TimeSpan.FromSeconds(15);
    public int MaxPendingWords { get; set; } = 3;
    public int BufferedKeys => _current.Count + _pending.Sum(w => w.Strokes.Length);
    public IReadOnlyList<LayoutInfo> Layouts => _layouts;

    public void SetLayouts(IReadOnlyList<LayoutInfo> layouts)
    {
        _layouts = layouts.ToArray();
        Reset();
    }

    /// <summary>Forget everything (focus change, mouse click, navigation keys...).</summary>
    public void Reset()
    {
        _current.Clear();
        _pending.Clear();
        _lastWord = null;
        _edit = null;
    }

    /// <summary>A key that produces text under the active layout. Returns false (and resets) when it can't be tracked.</summary>
    public bool OnTextKey(Stroke stroke, nint layout, DateTime now)
    {
        if (_layout != layout || (_lastKey != default && now - _lastKey > IdleReset)) Reset();
        _layout = layout;
        _lastKey = now;
        string? s = _mapper.Map(stroke, layout);
        if (string.IsNullOrEmpty(s) || s.Any(char.IsWhiteSpace)) { Reset(); return false; }
        _edit = null; // typing anything ends the undo / cycle window
        _lastWord = null;
        _current.Add(stroke);
        if (_current.Count > 48) { Reset(); return false; }
        return true;
    }

    public void OnBackspace(nint layout)
    {
        _edit = null;
        _lastWord = null;
        if (_current.Count > 0 && layout == _layout && _mapper.Map(_current[^1], layout) is { } last && Text.CodePoints(last) == 1)
            _current.RemoveAt(_current.Count - 1);
        else Reset();
    }

    /// <summary>
    /// Space or Enter was pressed (and is being held back by the caller). Returns a correction, or null
    /// to let the key through unchanged.
    /// </summary>
    public Correction? OnBoundary(Boundary kind, nint layout, DateTime now)
    {
        if (_current.Count == 0 || layout != _layout) { Reset(); return null; }
        if (now - _lastKey > IdleReset) { Reset(); return null; }
        var word = new Word(_current.ToArray(), layout);
        _current.Clear();
        _edit = null;
        _lastKey = now;
        var current = Find(layout);
        string? curText = current is null ? null : Render(word, layout);
        if (current is null || curText is null) { Reset(); return null; }

        Correction? result = AutoCorrect ? Decide(word, current, curText, kind, now) : null;
        if (result is null)
        {
            if (kind == Boundary.Space) _lastWord = word;
            else { _lastWord = null; _pending.Clear(); }
        }
        return result;
    }

    private Correction? Decide(Word word, LayoutInfo current, string curText, Boundary kind, DateTime now)
    {
        if (_rules.IsNever(current.Language, curText)) { _pending.Clear(); return null; }
        if (_pending.Count > 0 && _pending[^1].Layout != current.Id) _pending.Clear();
        if (_rules.AlwaysTarget(current.Language, curText) is { } learned &&
            _layouts.FirstOrDefault(l => l.Root == learned) is { } learnedLayout && Render(word, learnedLayout.Id) is not null)
            return MakeAuto([word], current, learnedLayout, kind, now);

        var verdict = _engine.Judge(current, curText, _layouts.Select(l => (l, Render(word, l.Id))));
        word.Verdict = verdict;
        switch (verdict.Kind)
        {
            case VerdictKind.KeepCurrent:
                Remember(word);
                return null;
            case VerdictKind.Wrong:
            {
                var target = verdict.Best!.Layout;
                var span = new List<Word> { word };
                for (int i = _pending.Count - 1; i >= 0; i--)
                {
                    // Earlier words join the fix when they also read better in the target language
                    // (a short real word like "فخ" for "to" needs clearer evidence).
                    var v = _pending[i].Verdict;
                    var h = v?.For(target.Id);
                    double floor = v?.Kind == VerdictKind.KeepCurrent ? Options.RetroactiveFloor : Options.PhraseWordFloor;
                    if (h is null || h.Llr < floor) break;
                    span.Insert(0, _pending[i]);
                }
                return MakeAuto(span, current, target, kind, now);
            }
            default:
            {
                Remember(word);
                var target = verdict.Best!.Layout;
                double sum = 0;
                int count = 0;
                for (int i = _pending.Count - 1; i >= 0; i--)
                {
                    var h = _pending[i].Verdict?.For(target.Id);
                    if (h is null || h.Llr < Options.PhraseWordFloor || _pending[i].Verdict!.Kind == VerdictKind.KeepCurrent) break;
                    sum += h.Llr;
                    count++;
                }
                if (count >= 2 && sum >= Options.PhraseThreshold - Options.Sensitivity)
                {
                    var span = _pending.Skip(_pending.Count - count).ToList();
                    return MakeAuto(span, current, target, kind, now);
                }
                return null;
            }
        }
    }

    private void Remember(Word word)
    {
        _pending.Add(word);
        while (_pending.Count > MaxPendingWords) _pending.RemoveAt(0);
    }

    private Correction? MakeAuto(List<Word> span, LayoutInfo from, LayoutInfo to, Boundary kind, DateTime now)
    {
        string? before = Join(span, from.Id), after = Join(span, to.Id);
        if (before is null || after is null) { Reset(); return null; }
        _pending.Clear();
        _lastWord = null;
        _edit = new Edit(CorrectionKind.Auto, span.ToArray(), from.Id, to.Id, kind == Boundary.Space, now, [], -1);
        _layout = to.Id;
        return new(Text.CodePoints(before), after, kind, to.Id, CorrectionKind.Auto, before, after, from.Language, to.Language);
    }

    /// <summary>
    /// The "fix it" hotkey: undoes the last automatic correction, otherwise converts the word being typed
    /// (or the word just finished) to the most likely other layout. Pressing again cycles through layouts.
    /// Works for every installed layout, even languages without a pack.
    /// </summary>
    public Correction? OnManualFix(nint layout, DateTime now)
    {
        if (_edit is { Kind: CorrectionKind.Auto } auto && layout == _layout && now - auto.At <= UndoWindow && _current.Count == 0)
        {
            _edit = null;
            if (!auto.WithSpace) return null; // Enter already submitted the text; use the app's own undo
            var from = Find(auto.From);
            string? before = Join(auto.Words, auto.From), after = Join(auto.Words, auto.ShownLayout);
            if (from is null || before is null || after is null) { Reset(); return null; }
            foreach (var w in auto.Words)
                if (Render(w, auto.From) is { } t) _rules.AddNever(from.Language, t);
            _layout = auto.From;
            var to = Find(auto.ShownLayout);
            Reset();
            return new(Text.CodePoints(after) + 1, before + " ", null, auto.From, CorrectionKind.Undo,
                after, before, to?.Language ?? "", from.Language);
        }

        if (_edit is { Kind: CorrectionKind.Manual } manual && layout == _layout && now - manual.At <= UndoWindow)
            return Step(manual.Words[0], manual.From, manual.ShownLayout, manual.WithSpace, manual.Cycle, manual.CycleIndex + 1, now);

        if (_current.Count > 0 && layout == _layout && now - _lastKey <= IdleReset)
        {
            var word = new Word(_current.ToArray(), layout);
            var cycle = Cycle(word, layout);
            return cycle.Count < 2 ? null : Step(word, layout, layout, false, cycle, 0, now);
        }

        if (_lastWord is { } last && layout == _layout && now - _lastKey <= UndoWindow)
        {
            var cycle = Cycle(last, layout);
            return cycle.Count < 2 ? null : Step(last, layout, layout, true, cycle, 0, now);
        }
        return null;
    }

    private Correction? Step(Word word, nint origin, nint shown, bool withSpace, IReadOnlyList<nint> cycle, int index, DateTime now)
    {
        index %= cycle.Count;
        nint target = cycle[index];
        string? before = Render(word, shown), after = Render(word, target);
        var fromInfo = Find(origin);
        var toInfo = Find(target);
        if (before is null || after is null || fromInfo is null || toInfo is null) { Reset(); return null; }
        if (LearnFromManualFixes && withSpace && Render(word, origin) is { } originText)
        {
            if (target == origin) _rules.Forget(fromInfo.Language, originText);
            else _rules.AddAlways(fromInfo.Language, originText, toInfo.Language);
        }
        _pending.Clear();
        _lastWord = null;
        _layout = target;
        if (!withSpace)
        {
            _current.Clear();
            _current.AddRange(word.Strokes); // the word continues in the new layout
        }
        _edit = new Edit(CorrectionKind.Manual, [word], origin, target, withSpace, now, cycle, index);
        string suffix = withSpace ? " " : "";
        var shownInfo = Find(shown);
        return new(Text.CodePoints(before) + suffix.Length, after + suffix, null, target, CorrectionKind.Manual,
            before, after, shownInfo?.Language ?? "", toInfo.Language);
    }

    /// <summary>Other layouts ordered by how likely they are, then the original layout last.</summary>
    private List<nint> Cycle(Word word, nint origin)
    {
        var current = Find(origin);
        string? curText = Render(word, origin);
        if (current is null || curText is null) return [];
        var seen = new HashSet<string>(StringComparer.Ordinal) { curText };
        var options = new List<(nint Id, double Score)>();
        var curScore = DecisionEngine.Score(_model.TryGetLoaded(current.Language), curText).LogProb;
        foreach (var l in _layouts)
        {
            if (l.Id == origin || Render(word, l.Id) is not { } t || !seen.Add(t)) continue;
            var pack = _model.TryGetLoaded(l.Language);
            double s = pack is null ? -1e6 : DecisionEngine.Score(pack, t).LogProb - curScore;
            options.Add((l.Id, s));
        }
        var list = options.OrderByDescending(o => o.Score).Select(o => o.Id).ToList();
        if (list.Count > 0) list.Add(origin);
        return list;
    }

    private LayoutInfo? Find(nint id) => _layouts.FirstOrDefault(l => l.Id == id);

    private string? Render(Word word, nint layout)
    {
        var sb = new System.Text.StringBuilder(word.Strokes.Length + 2);
        foreach (var s in word.Strokes)
        {
            string? t = _mapper.Map(s, layout);
            if (string.IsNullOrEmpty(t)) return null;
            sb.Append(t);
        }
        return sb.ToString();
    }

    private string? Join(IEnumerable<Word> words, nint layout)
    {
        var parts = new List<string>();
        foreach (var w in words)
        {
            if (Render(w, layout) is not { } t) return null;
            parts.Add(t);
        }
        return string.Join(" ", parts);
    }
}
