using ArzType.Core;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using static ArzType.App.NativeMethods;

namespace ArzType.App;

/// <summary>
/// Runs the global keyboard/mouse hooks on a dedicated high-priority thread with its own message loop,
/// so nothing in the UI can ever slow typing down. Corrections happen *inside* the hook at the moment
/// Space/Enter is pressed: the key is held back, the word is replaced, and the key is re-sent — no
/// timers, no race with fast typists. Keystrokes are kept only in memory, a few words at most.
/// </summary>
internal sealed class TypingEngine : IDisposable
{
    private const uint WM_RUN = WM_APP + 1, WM_FIX = WM_APP + 2;
    // Thread timers (hwnd = 0): Windows picks the timer id, so remember what SetTimer returned.
    private nuint _rehookTimer, _fixTimer;

    private readonly WindowsKeyMapper _mapper = new();
    private readonly SafetyGate _gate;
    private readonly Corrector _corrector;
    private readonly ConcurrentQueue<Action> _inbox = new();
    private readonly HookProc _keyboardProc, _mouseProc;
    private readonly ManualResetEventSlim _started = new();
    private Thread? _thread;
    private uint _threadId;
    private nint _keyboardHook, _mouseHook;
    private Exception? _startError;
    private Focus _lastFocus;
    private int _fixAttempts;

    // Double-tap Shift detection
    private bool _shiftHeld, _otherKeySinceShift;
    private long _shiftDownAt, _firstTapAt;
    private int _shiftTaps;

    public volatile bool Enabled;
    /// <summary>Automated end-to-end tests only: treat input injected by other programs as typing.</summary>
    public volatile bool AcceptInjected;
    public volatile bool FixOnEnter = true, DoubleShiftFix = true, PauseKeyFix = true;

    /// <summary>Raised on the hook thread after an edit was sent. Handlers must return quickly.</summary>
    public event Action<Correction>? Corrected;

    public TypingEngine(LanguageModel model, UserRules rules, SafetyGate gate)
    {
        _gate = gate;
        _corrector = new Corrector(model, _mapper, rules);
        _keyboardProc = KeyboardProc;
        _mouseProc = MouseProc;
    }

    public void Start()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "ArzType keyboard", Priority = ThreadPriority.Highest };
        _thread.Start();
        _started.Wait(TimeSpan.FromSeconds(10));
        if (_startError is not null) throw _startError;
    }

    /// <summary>Runs an action on the hook thread (the only thread that touches the corrector).</summary>
    public void Post(Action action)
    {
        _inbox.Enqueue(action);
        if (_threadId != 0) PostThreadMessage(_threadId, WM_RUN, 0, 0);
    }

    public void SetLayouts(IReadOnlyList<LayoutInfo> layouts) => Post(() => _corrector.SetLayouts(layouts));
    public void Configure(Action<Corrector> change) => Post(() => change(_corrector));
    public void ResetTyping() => Post(() => { _corrector.Reset(); _gate.Invalidate(); });
    public void RequestRehook() => Post(Rehook);

    private void Run()
    {
        try
        {
            _threadId = GetCurrentThreadId();
            InstallHooks();
            _rehookTimer = SetTimer(0, 0, 10 * 60 * 1000, 0);
        }
        catch (Exception e) { _startError = e; _started.Set(); return; }
        _started.Set();

        while (GetMessage(out MSG msg, 0, 0, 0) > 0)
        {
            if (msg.hwnd == 0)
            {
                switch (msg.message)
                {
                    case WM_RUN: Drain(); continue;
                    case WM_FIX: _fixAttempts = 0; ManualFix(); continue;
                    case WM_TIMER when _rehookTimer != 0 && (nuint)msg.wParam == _rehookTimer:
                        if (_corrector.BufferedKeys == 0) Rehook();
                        continue;
                    case WM_TIMER when _fixTimer != 0 && (nuint)msg.wParam == _fixTimer:
                        KillTimer(0, _fixTimer);
                        _fixTimer = 0;
                        ManualFix();
                        continue;
                }
            }
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }
        RemoveHooks();
    }

    private void InstallHooks()
    {
        nint module = GetModuleHandle(null);
        _keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc, module, 0);
        if (_keyboardHook == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "Keyboard hook not available");
        _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, module, 0);
        if (_mouseHook == 0)
        {
            int error = Marshal.GetLastWin32Error();
            UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = 0;
            throw new Win32Exception(error, "Mouse tracking unavailable; correction disabled for safety");
        }
    }

    /// <summary>Windows silently drops hooks that ever time out; re-arming periodically keeps ArzType alive.</summary>
    private void Rehook()
    {
        nint oldK = _keyboardHook, oldM = _mouseHook;
        nint module = GetModuleHandle(null);
        nint k = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc, module, 0);
        nint m = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, module, 0);
        if (k == 0 || m == 0)
        {
            if (k != 0) UnhookWindowsHookEx(k);
            if (m != 0) UnhookWindowsHookEx(m);
            return;
        }
        _keyboardHook = k;
        _mouseHook = m;
        if (oldK != 0) UnhookWindowsHookEx(oldK);
        if (oldM != 0) UnhookWindowsHookEx(oldM);
    }

    private void RemoveHooks()
    {
        if (_keyboardHook != 0) UnhookWindowsHookEx(_keyboardHook);
        if (_mouseHook != 0) UnhookWindowsHookEx(_mouseHook);
        _keyboardHook = _mouseHook = 0;
    }

    private void Drain()
    {
        while (_inbox.TryDequeue(out var action))
        {
            try { action(); } catch { }
        }
    }

    private nint KeyboardProc(int code, nint wParam, nint lParam)
    {
        if (code >= 0)
        {
            try
            {
                if (HandleKey((int)wParam, lParam)) return 1;
            }
            catch { _corrector.Reset(); /* never disturb typing because of ArzType */ }
        }
        return CallNextHookEx(_keyboardHook, code, wParam, lParam);
    }

    private static bool IsTextKey(uint vk) => vk is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A or >= 0xBA and <= 0xC0 or >= 0xDB and <= 0xDF or 0xE2;

    private bool HandleKey(int message, nint lParam)
    {
        uint vk = (uint)Marshal.ReadInt32(lParam, 0);
        uint scan = (uint)Marshal.ReadInt32(lParam, 4);
        uint flags = (uint)Marshal.ReadInt32(lParam, 8);
        nuint extra = (nuint)Marshal.ReadIntPtr(lParam, 16);
        bool down = message is WM_KEYDOWN or WM_SYSKEYDOWN;
        if ((flags & LLKHF_INJECTED) != 0 && !(AcceptInjected && extra != ArzSignature))
        {
            if (extra != ArzSignature && down) _corrector.Reset(); // on-screen keyboards, macros…
            return false;
        }
        if (!_inbox.IsEmpty) Drain();

        bool isShift = vk is VK_SHIFT or VK_LSHIFT or VK_RSHIFT;
        if (!down)
        {
            if (isShift) OnShiftUp();
            return false;
        }
        if (isShift) { OnShiftDown(); return false; }
        _otherKeySinceShift = true;
        _shiftTaps = 0;

        if (!Enabled) return false;
        if (vk is VK_CONTROL or VK_LCONTROL or VK_RCONTROL or VK_MENU or VK_LMENU or VK_RMENU or VK_LWIN or VK_RWIN or VK_CAPITAL)
            return false;
        bool ctrl = Down(VK_CONTROL), alt = Down(VK_MENU), win = Down(VK_LWIN) || Down(VK_RWIN);
        if (vk == VK_PAUSE && PauseKeyFix && !ctrl && !alt && !win)
        {
            PostThreadMessage(_threadId, WM_FIX, 0, 0);
            return true;
        }
        if (ctrl || alt || win) { _corrector.Reset(); return false; }

        var focus = CurrentFocus();
        if (focus.Window != _lastFocus.Window || focus.Foreground != _lastFocus.Foreground)
        {
            _corrector.Reset();
            _gate.Invalidate();
            _lastFocus = focus;
        }
        if (focus.Window == 0 || WindowsKeyMapper.IsComposing(WindowsKeyMapper.Language(focus.Layout)))
        {
            _corrector.Reset();
            return false;
        }

        bool shift = Down(VK_SHIFT);
        switch (vk)
        {
            case VK_BACK:
                _corrector.OnBackspace(focus.Layout);
                return false;
            case VK_SPACE when !shift:
                return OnBoundary(Boundary.Space, focus);
            case VK_RETURN when !shift && FixOnEnter:
            {
                bool swallowed = OnBoundary(Boundary.Enter, focus);
                _corrector.Reset();
                _gate.Invalidate();
                return swallowed;
            }
            case VK_RETURN or VK_TAB:
                _corrector.Reset();
                _gate.Invalidate();
                return false;
        }
        if (IsTextKey(vk) || vk == VK_SPACE)
        {
            _gate.BeginCheck(focus);
            _corrector.OnTextKey(new Stroke(scan, shift, CapsOn), focus.Layout, DateTime.UtcNow);
            return false;
        }
        _corrector.Reset(); // arrows, Home/End, Delete, function keys, numpad…
        return false;
    }

    private bool OnBoundary(Boundary kind, Focus focus)
    {
        var state = _gate.Check(focus);
        if (state == SafetyGate.State.Unsafe) { _corrector.Reset(); return false; }
        if (state == SafetyGate.State.Unknown)
        {
            // Not verified yet: remember the word for the manual hotkey, but don't change anything.
            bool auto = _corrector.AutoCorrect;
            _corrector.AutoCorrect = false;
            _corrector.OnBoundary(kind, focus.Layout, DateTime.UtcNow);
            _corrector.AutoCorrect = auto;
            return false;
        }
        var fix = _corrector.OnBoundary(kind, focus.Layout, DateTime.UtcNow);
        return fix is not null && Execute(fix, focus);
    }

    private bool Execute(Correction fix, Focus focus)
    {
        int trailing = fix.Boundary switch { Boundary.Space => VK_SPACE, Boundary.Enter => VK_RETURN, _ => 0 };
        if (!SendEdit(fix.Backspaces, fix.Text, trailing)) { _corrector.Reset(); return false; }
        RequestLayout(focus, fix.SwitchTo);
        try { Corrected?.Invoke(fix); } catch { }
        return true;
    }

    private void ManualFix()
    {
        // Wait until Shift/Ctrl/Alt are physically released, so injected keys aren't modified.
        if (Down(VK_SHIFT) || Down(VK_CONTROL) || Down(VK_MENU))
        {
            if (_fixAttempts++ < 40 && _fixTimer == 0) _fixTimer = SetTimer(0, 0, 15, 0);
            return;
        }
        if (!Enabled) return;
        var focus = CurrentFocus();
        if (focus.Window != _lastFocus.Window) { _corrector.Reset(); return; }
        if (_gate.Check(focus) == SafetyGate.State.Unsafe) return;
        var fix = _corrector.OnManualFix(focus.Layout, DateTime.UtcNow);
        if (fix is not null) Execute(fix, focus);
    }

    private void OnShiftDown()
    {
        if (_shiftHeld) return; // auto-repeat
        _shiftHeld = true;
        _shiftDownAt = Environment.TickCount64;
        _otherKeySinceShift = false;
    }

    private void OnShiftUp()
    {
        _shiftHeld = false;
        long now = Environment.TickCount64;
        if (_otherKeySinceShift || now - _shiftDownAt > 300) { _shiftTaps = 0; return; }
        if (_shiftTaps == 1 && now - _firstTapAt <= 450)
        {
            _shiftTaps = 0;
            if (DoubleShiftFix && Enabled) PostThreadMessage(_threadId, WM_FIX, 0, 0);
        }
        else
        {
            _shiftTaps = 1;
            _firstTapAt = now;
        }
    }

    private nint MouseProc(int code, nint wParam, nint lParam)
    {
        if (code >= 0 && (int)wParam is WM_LBUTTONDOWN or WM_RBUTTONDOWN or WM_MBUTTONDOWN or WM_XBUTTONDOWN)
        {
            try { _corrector.Reset(); _gate.Invalidate(); _otherKeySinceShift = true; } catch { }
        }
        return CallNextHookEx(_mouseHook, code, wParam, lParam);
    }

    public void Dispose()
    {
        if (_threadId != 0) PostThreadMessage(_threadId, WM_QUIT, 0, 0);
        _thread?.Join(1000);
    }
}
