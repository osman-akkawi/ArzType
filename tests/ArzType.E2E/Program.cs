// Real Windows end-to-end test: starts ArzType.exe and Notepad, types physical keys on the wrong
// layout with SendInput (scan codes), and checks Notepad's text and active layout afterwards.
// Usage: ArzType.E2E <path-to-ArzType.exe>
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

string exe = args.Length > 0 ? Path.GetFullPath(args[0]) : throw new ArgumentException("path to ArzType.exe");
nint us = Win.LoadKeyboardLayout("00000409", 1);
nint ar = Win.LoadKeyboardLayout("00000401", 1);
nint ru = Win.LoadKeyboardLayout("00000419", 1);
Console.WriteLine($"layouts: us={us:X} ar={ar:X} ru={ru:X}");

var arz = Process.Start(new ProcessStartInfo(exe, "--tray --enable --e2e") { UseShellExecute = false })!;
Thread.Sleep(6000); // hook + language packs
int failures = 0;

failures += Run("English typed on Arabic layout", ar, "today ", "today ", us);
failures += Run("Arabic typed on US layout", us, "lvpfh ", "مرحبا ", ar);
failures += Run("Arabic sentence on US layout", us, "hgsghl ugdil ", "السلام عليكم ", ar);
failures += Run("Russian typed on US layout", us, "ghbdtn ", "привет ", ru);
failures += Run("Correct English untouched", us, "hello world ", "hello world ", us);
failures += Run("Correct Arabic untouched", ar, "lvpfh ", "مرحبا ", ar);

try { arz.Kill(); } catch { }
Console.WriteLine(failures == 0 ? "E2E: all passed" : $"E2E: {failures} failed");
return failures == 0 ? 0 : 1;

int Run(string name, nint startLayout, string keys, string expected, nint expectedLayout)
{
    var notepad = Process.Start("notepad.exe")!;
    try
    {
        notepad.WaitForInputIdle(5000);
        nint hwnd = 0;
        for (int i = 0; i < 50 && hwnd == 0; i++) { Thread.Sleep(100); notepad.Refresh(); hwnd = notepad.MainWindowHandle; }
        if (hwnd == 0) { Console.WriteLine($"FAIL {name}: no notepad window"); return 1; }
        Win.SetForegroundWindow(hwnd);
        Thread.Sleep(300);
        nint edit = Win.FindEditor(hwnd);
        Win.PostMessage(edit != 0 ? edit : hwnd, 0x0050, 0, startLayout);
        Thread.Sleep(400);
        foreach (char c in keys)
        {
            var (scan, shift) = Win.UsScan(c);
            Win.Key(scan, shift);
            Thread.Sleep(90);
        }
        Thread.Sleep(900);
        string text = Win.GetText(edit != 0 ? edit : hwnd);
        uint thread = Win.GetWindowThreadProcessId(hwnd, out _);
        nint layout = Win.GetKeyboardLayout(thread);
        bool ok = text == expected && (layout & 0xFFFF) == (expectedLayout & 0xFFFF);
        Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {name}: text='{text}' layout={layout:X} (expected '{expected}', {expectedLayout:X})");
        return ok ? 0 : 1;
    }
    finally
    {
        try { notepad.Kill(); notepad.WaitForExit(2000); } catch { }
    }
}

static class Win
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern nint LoadKeyboardLayout(string id, uint flags);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(nint h);
    [DllImport("user32.dll")] public static extern bool PostMessage(nint h, uint m, nint w, nint l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern nint SendMessage(nint h, uint m, nint w, StringBuilder l);
    [DllImport("user32.dll")] public static extern nint SendMessage(nint h, uint m, nint w, nint l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern nint FindWindowEx(nint parent, nint after, string? cls, string? title);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(nint h, out uint pid);
    [DllImport("user32.dll")] public static extern nint GetKeyboardLayout(uint thread);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint n, INPUT[] i, int size);

    [StructLayout(LayoutKind.Explicit, Size = 40)] private struct INPUT { [FieldOffset(0)] public uint type; [FieldOffset(8)] public KEYBDINPUT ki; }
    [StructLayout(LayoutKind.Sequential)] private struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public nuint extra; }

    public static nint FindEditor(nint top)
    {
        foreach (var cls in new[] { "Edit", "RichEditD2DPT", "RICHEDIT50W" })
        {
            nint h = FindWindowEx(top, 0, cls, null);
            if (h != 0) return h;
        }
        // Windows 11 Notepad nests the editor one level deeper.
        nint child = 0;
        while ((child = FindWindowEx(top, child, null, null)) != 0)
        {
            nint h = FindWindowEx(child, 0, "RichEditD2DPT", null);
            if (h != 0) return h;
        }
        return 0;
    }

    public static string GetText(nint h)
    {
        int len = (int)SendMessage(h, 0x000E, 0, 0); // WM_GETTEXTLENGTH
        var sb = new StringBuilder(len + 1);
        SendMessage(h, 0x000D, sb.Capacity, sb); // WM_GETTEXT
        return sb.ToString().Replace("\r", "").Replace("\n", "");
    }

    private static readonly string Row1 = "qwertyuiop[]", Row2 = "asdfghjkl;'", Row3 = "zxcvbnm,./";
    public static (ushort Scan, bool Shift) UsScan(char c)
    {
        if (c == ' ') return (0x39, false);
        bool shift = char.IsUpper(c);
        char l = char.ToLowerInvariant(c);
        int i;
        if ((i = Row1.IndexOf(l)) >= 0) return ((ushort)(0x10 + i), shift);
        if ((i = Row2.IndexOf(l)) >= 0) return ((ushort)(0x1E + i), shift);
        if ((i = Row3.IndexOf(l)) >= 0) return ((ushort)(0x2C + i), shift);
        throw new ArgumentException($"no key for {c}");
    }

    public static void Key(ushort scan, bool shift)
    {
        const uint SCANCODE = 0x0008, UP = 0x0002;
        var list = new List<INPUT>();
        if (shift) list.Add(new INPUT { type = 1, ki = new KEYBDINPUT { wScan = 0x2A, dwFlags = SCANCODE } });
        list.Add(new INPUT { type = 1, ki = new KEYBDINPUT { wScan = scan, dwFlags = SCANCODE } });
        list.Add(new INPUT { type = 1, ki = new KEYBDINPUT { wScan = scan, dwFlags = SCANCODE | UP } });
        if (shift) list.Add(new INPUT { type = 1, ki = new KEYBDINPUT { wScan = 0x2A, dwFlags = SCANCODE | UP } });
        SendInput((uint)list.Count, list.ToArray(), Marshal.SizeOf<INPUT>());
    }
}
