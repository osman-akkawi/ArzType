using System.Collections.Concurrent;
using System.Diagnostics;
using System.Windows.Automation;

namespace ArzType.App;

/// <summary>
/// Decides whether the focused control may be edited. UI Automation is slow (milliseconds) and can
/// re-enter message loops, so it never runs inside the keyboard hook: a check is started when a word
/// begins and is normally finished before the word ends. No answer yet = no correction.
/// </summary>
internal sealed class SafetyGate : IDisposable
{
    public static readonly string[] DefaultExclusions =
    [
        // terminals and code editors: rewriting commands or code is harmful
        "WindowsTerminal", "wt", "conhost", "cmd", "powershell", "pwsh", "OpenConsole", "mintty", "putty", "kitty",
        "alacritty", "wezterm-gui", "Code", "Code - Insiders", "devenv", "rider64", "idea64", "pycharm64", "webstorm64",
        "clion64", "goland64", "phpstorm64", "datagrip64", "studio64", "sublime_text", "Cursor", "zed",
        // password managers
        "KeePass", "KeePassXC", "1Password", "Bitwarden", "LastPass", "Dashlane", "Enpass", "RoboForm",
        // remote / virtual machines (keys belong to another computer)
        "mstsc", "msrdc", "vmconnect", "VirtualBoxVM", "vmware-vmx", "vmware", "TeamViewer", "AnyDesk", "parsecd",
    ];

    public enum State { Unknown, Safe, Unsafe }
    private sealed record Result(nint Window, uint Pid, State State, DateTime At, string Reason);

    private readonly BlockingCollection<NativeMethods.Focus> _queue = new(new ConcurrentQueue<NativeMethods.Focus>(), 8);
    private readonly ConcurrentDictionary<uint, (string Name, bool Elevated, DateTime At)> _processes = new();
    private readonly Thread _worker;
    private readonly bool _selfElevated;
    private readonly uint _selfPid = (uint)Environment.ProcessId;
    private volatile Result? _last;
    private volatile HashSet<string> _excluded = new(DefaultExclusions, StringComparer.OrdinalIgnoreCase);

    /// <summary>When true, controls that do not report themselves as text fields are allowed (never passwords).</summary>
    public volatile bool AllowUnknownControls;

    public SafetyGate()
    {
        _selfElevated = NativeMethods.IsElevated((uint)Environment.ProcessId);
        _worker = new Thread(Work) { IsBackground = true, Name = "ArzType safety checks" };
        _worker.SetApartmentState(ApartmentState.MTA);
        _worker.Start();
    }

    public void SetExclusions(IEnumerable<string> names) =>
        _excluded = new HashSet<string>(names.Select(n => n.Trim().Replace(".exe", "", StringComparison.OrdinalIgnoreCase))
            .Where(n => n.Length > 0), StringComparer.OrdinalIgnoreCase);

    public string ProcessName(uint pid)
    {
        if (_processes.TryGetValue(pid, out var p) && DateTime.UtcNow - p.At < TimeSpan.FromMinutes(2)) return p.Name;
        string name;
        try { name = Process.GetProcessById((int)pid).ProcessName; } catch { name = ""; }
        bool elevated = !_selfElevated && NativeMethods.IsElevated(pid);
        _processes[pid] = (name, elevated, DateTime.UtcNow);
        return name;
    }

    /// <summary>Called from the hook when a word starts. Cheap; never blocks.</summary>
    public void BeginCheck(NativeMethods.Focus focus)
    {
        var last = _last;
        var now = DateTime.UtcNow;
        if (last is not null && last.Window == focus.Window && now - last.At < TimeSpan.FromSeconds(1.5)) return;
        if (_pendingWindow == focus.Window && now - _pendingSince < TimeSpan.FromSeconds(1)) return;
        _pendingWindow = focus.Window;
        _pendingSince = now;
        _queue.TryAdd(focus);
    }

    public State Check(NativeMethods.Focus focus)
    {
        var last = _last;
        if (last is null || last.Window != focus.Window || last.Pid != focus.ProcessId) return State.Unknown;
        if (DateTime.UtcNow - last.At > TimeSpan.FromSeconds(4)) return State.Unknown;
        return last.State;
    }

    public void Invalidate() { _last = null; _pendingWindow = 0; }
    private nint _pendingWindow; // touched only by the hook thread
    private DateTime _pendingSince;

    private void Work()
    {
        try { _ = AutomationElement.FocusedElement; } catch { } // warm up UI Automation (first call is slow)
        foreach (var focus in _queue.GetConsumingEnumerable())
        {
            var (state, reason) = Evaluate(focus);
            var now = NativeMethods.CurrentFocus();
            // Discard if focus moved while we were looking.
            if (now.Window == focus.Window) _last = new Result(focus.Window, focus.ProcessId, state, DateTime.UtcNow, reason);
        }
    }

    private (State, string) Evaluate(NativeMethods.Focus focus)
    {
        try
        {
            if (focus.ProcessId == 0 || focus.ProcessId == _selfPid) return (State.Unsafe, "self");
            string name = ProcessName(focus.ProcessId);
            if (name.Length == 0 || _excluded.Contains(name)) return (State.Unsafe, "excluded app");
            if (_processes.TryGetValue(focus.ProcessId, out var p) && p.Elevated) return (State.Unsafe, "administrator window");
            var element = AutomationElement.FocusedElement;
            if (element is null) return (AllowUnknownControls ? State.Safe : State.Unsafe, "no element");
            var info = element.Current;
            if (info.ProcessId != focus.ProcessId) return (State.Unsafe, "focus mismatch");
            if (info.IsPassword) return (State.Unsafe, "password");
            if (!info.IsEnabled) return (State.Unsafe, "disabled");
            if (info.ControlType == ControlType.Edit || info.ControlType == ControlType.Document) return (State.Safe, "text field");
            if (info.ControlType == ControlType.ComboBox) return (State.Safe, "combo box");
            return (AllowUnknownControls ? State.Safe : State.Unsafe, "not a text field");
        }
        catch (Exception)
        {
            return (State.Unsafe, "accessibility error");
        }
    }

    public void Dispose() => _queue.CompleteAdding();
}
