namespace ArzType.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        using var single = new Mutex(true, @"Local\ArzType.SingleInstance", out bool first);
        if (!first)
        {
            // Already running: ask the existing instance to show its window.
            uint msg = NativeMethods.RegisterWindowMessage("ArzType.ShowWindow");
            NativeMethods.PostMessage(0xFFFF /* HWND_BROADCAST */, msg, 0, 0);
            return;
        }
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        if (!Environment.Is64BitProcess)
        {
            MessageBox.Show("ArzType supports 64-bit Windows only.", "ArzType");
            return;
        }
        bool tray = args.Any(a => a.Equals("--tray", StringComparison.OrdinalIgnoreCase));
        bool enable = args.Any(a => a.Equals("--enable", StringComparison.OrdinalIgnoreCase));
        bool e2e = args.Any(a => a.Equals("--e2e", StringComparison.OrdinalIgnoreCase));
        Application.Run(new MainForm(tray, enable, e2e));
    }
}
