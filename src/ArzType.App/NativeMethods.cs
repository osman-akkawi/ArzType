using System.Runtime.InteropServices;
using System.Text;

namespace ArzType.App;

internal static class NativeMethods
{
    public const int WH_KEYBOARD_LL = 13, WH_MOUSE_LL = 14;
    public const int WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;
    public const int WM_LBUTTONDOWN = 0x0201, WM_RBUTTONDOWN = 0x0204, WM_MBUTTONDOWN = 0x0207, WM_XBUTTONDOWN = 0x020B;
    public const int WM_INPUTLANGCHANGEREQUEST = 0x0050, WM_TIMER = 0x0113, WM_QUIT = 0x0012, WM_APP = 0x8000;
    public const uint LLKHF_INJECTED = 0x10;
    public const uint INPUT_KEYBOARD = 1;
    public const uint KEYEVENTF_EXTENDEDKEY = 1, KEYEVENTF_KEYUP = 2, KEYEVENTF_UNICODE = 4;
    public const uint MAPVK_VK_TO_VSC = 0, MAPVK_VSC_TO_VK_EX = 3;
    public const int VK_BACK = 0x08, VK_TAB = 0x09, VK_RETURN = 0x0D, VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12;
    public const int VK_PAUSE = 0x13, VK_CAPITAL = 0x14, VK_SPACE = 0x20, VK_LWIN = 0x5B, VK_RWIN = 0x5C;
    public const int VK_LSHIFT = 0xA0, VK_RSHIFT = 0xA1, VK_LCONTROL = 0xA2, VK_RCONTROL = 0xA3, VK_LMENU = 0xA4, VK_RMENU = 0xA5;
    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000, TOKEN_QUERY = 0x0008;
    public const int TokenElevation = 20;
    /// <summary>Marks input injected by ArzType ("ARZT").</summary>
    public const nuint ArzSignature = 0x41525A54;

    internal delegate nint HookProc(int code, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    internal struct MSG
    {
        public nint hwnd;
        public uint message;
        public nint wParam, lParam;
        public uint time;
        public int ptX, ptY;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct GuiThreadInfo
    {
        public uint cbSize, flags;
        public nint hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret;
        public RECT rcCaret;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct RECT { public int left, top, right, bottom; }

    // 64-bit layout: the INPUT union starts at offset 8.
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    internal struct INPUT
    {
        [FieldOffset(0)] public uint type;
        [FieldOffset(8)] public KEYBDINPUT ki;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct KEYBDINPUT
    {
        public ushort wVk, wScan;
        public uint dwFlags, time;
        public nuint dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern nint SetWindowsHookEx(int idHook, HookProc lpfn, nint hMod, uint dwThreadId);
    [DllImport("user32.dll")] internal static extern bool UnhookWindowsHookEx(nint hhk);
    [DllImport("user32.dll")] internal static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern nint GetModuleHandle(string? lpModuleName);
    [DllImport("kernel32.dll")] internal static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] internal static extern int GetMessage(out MSG msg, nint hwnd, uint min, uint max);
    [DllImport("user32.dll")] internal static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32.dll")] internal static extern nint DispatchMessage(ref MSG msg);
    [DllImport("user32.dll")] internal static extern bool PostThreadMessage(uint threadId, uint msg, nint wParam, nint lParam);
    [DllImport("user32.dll")] internal static extern nuint SetTimer(nint hwnd, nuint id, uint ms, nint proc);
    [DllImport("user32.dll")] internal static extern bool KillTimer(nint hwnd, nuint id);
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);
    [DllImport("user32.dll")] internal static extern bool GetGUIThreadInfo(uint idThread, ref GuiThreadInfo pgui);
    [DllImport("user32.dll")] internal static extern nint GetKeyboardLayout(uint threadId);
    [DllImport("user32.dll")] internal static extern int GetKeyboardLayoutList(int nBuff, [Out] nint[]? lpList);
    [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int vKey);
    [DllImport("user32.dll")] internal static extern short GetKeyState(int vKey);
    [DllImport("user32.dll")] internal static extern uint MapVirtualKeyEx(uint uCode, uint uMapType, nint dwhkl);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int ToUnicodeEx(uint wVirtKey, uint wScanCode, byte[] lpKeyState,
        [Out] StringBuilder pwszBuff, int cchBuff, uint wFlags, nint dwhkl);
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint SendInput(uint cInputs, INPUT[] pInputs, int cbSize);
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool PostMessage(nint hwnd, uint msg, nint wParam, nint lParam);
    [DllImport("user32.dll")] internal static extern bool DestroyIcon(nint hIcon);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern uint RegisterWindowMessage(string name);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern nint OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] internal static extern bool CloseHandle(nint h);
    [DllImport("advapi32.dll", SetLastError = true)] internal static extern bool OpenProcessToken(nint process, uint access, out nint token);
    [DllImport("advapi32.dll", SetLastError = true)]
    internal static extern bool GetTokenInformation(nint token, int infoClass, out int info, int length, out int returned);

    public static bool Down(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
    public static bool CapsOn => (GetKeyState(VK_CAPITAL) & 1) != 0;

    public static IReadOnlyList<nint> InstalledLayouts()
    {
        int count = GetKeyboardLayoutList(0, null);
        if (count <= 0 || count > 64) return [];
        nint[] data = new nint[count];
        int filled = GetKeyboardLayoutList(count, data);
        return data.Take(Math.Max(0, filled)).Distinct().ToArray();
    }

    internal readonly record struct Focus(nint Foreground, nint Window, nint Layout, uint ProcessId, uint ThreadId);

    public static Focus CurrentFocus()
    {
        nint foreground = GetForegroundWindow();
        if (foreground == 0) return default;
        uint thread = GetWindowThreadProcessId(foreground, out uint pid);
        if (thread == 0) return default;
        var info = new GuiThreadInfo { cbSize = (uint)Marshal.SizeOf<GuiThreadInfo>() };
        nint focus = GetGUIThreadInfo(thread, ref info) && info.hwndFocus != 0 ? info.hwndFocus : foreground;
        return new(foreground, focus, GetKeyboardLayout(thread), pid, thread);
    }

    public static bool IsElevated(uint pid)
    {
        nint process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process == 0) return true; // cannot inspect → treat as protected
        try
        {
            if (!OpenProcessToken(process, TOKEN_QUERY, out nint token)) return true;
            try { return GetTokenInformation(token, TokenElevation, out int elevated, 4, out _) && elevated != 0; }
            finally { CloseHandle(token); }
        }
        finally { CloseHandle(process); }
    }

    private static INPUT Key(int vk, bool up) => new()
    {
        type = INPUT_KEYBOARD,
        ki = new KEYBDINPUT
        {
            wVk = (ushort)vk, wScan = (ushort)MapVirtualKeyEx((uint)vk, MAPVK_VK_TO_VSC, 0),
            dwFlags = up ? KEYEVENTF_KEYUP : 0, dwExtraInfo = ArzSignature
        }
    };

    private static INPUT Unicode(char c, bool up) => new()
    {
        type = INPUT_KEYBOARD,
        ki = new KEYBDINPUT { wScan = c, dwFlags = KEYEVENTF_UNICODE | (up ? KEYEVENTF_KEYUP : 0), dwExtraInfo = ArzSignature }
    };

    /// <summary>Backspace × n, then the text as Unicode, then an optional real key (Space/Enter). Never touches the clipboard.</summary>
    public static bool SendEdit(int backspaces, string text, int trailingVirtualKey = 0)
    {
        var inputs = new List<INPUT>(backspaces * 2 + text.Length * 2 + 2);
        for (int i = 0; i < backspaces; i++) { inputs.Add(Key(VK_BACK, false)); inputs.Add(Key(VK_BACK, true)); }
        foreach (char c in text) { inputs.Add(Unicode(c, false)); inputs.Add(Unicode(c, true)); }
        if (trailingVirtualKey != 0) { inputs.Add(Key(trailingVirtualKey, false)); inputs.Add(Key(trailingVirtualKey, true)); }
        if (inputs.Count is 0 or > 1000) return false;
        return SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>()) == (uint)inputs.Count;
    }

    public static void RequestLayout(Focus focus, nint layout)
    {
        PostMessage(focus.Window, WM_INPUTLANGCHANGEREQUEST, 0, layout);
        if (focus.Foreground != focus.Window) PostMessage(focus.Foreground, WM_INPUTLANGCHANGEREQUEST, 0, layout);
    }
}
