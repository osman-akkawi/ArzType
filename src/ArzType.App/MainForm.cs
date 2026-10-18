using ArzType.Core;
using System.Diagnostics;
using System.Reflection;

namespace ArzType.App;

internal sealed class MainForm : Form
{
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly LanguageModel _model = new();
    private readonly UserRules _rules = new();
    private readonly SafetyGate _gate = new();
    private readonly TypingEngine _engine;
    private readonly bool _startHidden;

    private readonly ToggleSwitch _toggle = new();
    private readonly Label _toggleLabel = new();
    private readonly Label _status = new();
    private readonly FlowLayoutPanel _keyboards = new();
    private readonly Label _stats = new();
    private readonly Button _forget = new();
    private readonly NotifyIcon _tray;
    private readonly ToolStripMenuItem _trayToggle;
    private readonly ToolStripMenuItem _trayExclude;
    private readonly TrayBadge _badge = new();
    private readonly System.Windows.Forms.Timer _poll = new() { Interval = 300 };
    private readonly System.Windows.Forms.Timer _saveLater = new() { Interval = 3000 };
    private IReadOnlyList<nint> _installed = [];
    private readonly HashSet<string> _loading = new(StringComparer.OrdinalIgnoreCase);
    private string _lastApp = "";
    private int _sessionFixes, _pollCount;
    private bool _exiting;
    private static readonly uint ShowMessage = NativeMethods.RegisterWindowMessage("ArzType.ShowWindow");

    public MainForm(bool startHidden, bool forceEnable = false, bool e2e = false)
    {
        _startHidden = startHidden;
        if (forceEnable) _settings.Enabled = true;
        string userPacks = Path.Combine(AppSettings.Folder, "packs");
        _model.AddDirectory(userPacks);                                      // user packs first: they win
        _model.AddDirectory(Path.Combine(AppContext.BaseDirectory, "language-packs"));
        var asm = Assembly.GetExecutingAssembly();
        foreach (string name in asm.GetManifestResourceNames().Where(n => n.StartsWith("packs/")))
            _model.AddSource(name["packs/".Length..], () => asm.GetManifestResourceStream(name)!);

        _rules.Import(_settings.NeverFingerprints, _settings.AlwaysFingerprints);
        _rules.Changed += () => BeginInvokeSafe(() => { PersistRules(); UpdateStats(); });
        _gate.SetExclusions(_settings.ExcludedApps);
        _gate.AllowUnknownControls = _settings.AllApps;
        _engine = new TypingEngine(_model, _rules, _gate);
        _engine.Corrected += fix => BeginInvokeSafe(() => OnCorrected(fix));
        _engine.AcceptInjected = e2e;
        ApplyEngineSettings();

        Text = "ArzType";
        Icon = LoadAppIcon();
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(720, 760);
        MinimumSize = new Size(600, 560);
        BackColor = Theme.Back;
        ForeColor = Theme.Text;
        Font = new Font(Theme.FontName, 9.75f);
        AutoScaleMode = AutoScaleMode.Dpi;
        if (S.Ar) { RightToLeft = RightToLeft.Yes; RightToLeftLayout = true; }

        BuildUi();

        var menu = new ContextMenuStrip { RenderMode = ToolStripRenderMode.System };
        if (S.Ar) menu.RightToLeft = RightToLeft.Yes;
        menu.Items.Add(S.TrayShow, null, (_, _) => ShowWindow());
        _trayToggle = new ToolStripMenuItem(S.TrayToggle) { CheckOnClick = true };
        _trayToggle.Click += (_, _) => _toggle.Checked = _trayToggle.Checked;
        menu.Items.Add(_trayToggle);
        _trayExclude = new ToolStripMenuItem("");
        _trayExclude.Click += (_, _) => ExcludeApp(_lastApp);
        menu.Items.Add(_trayExclude);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(S.TrayQuit, null, (_, _) => { _exiting = true; Close(); });
        menu.Opening += (_, _) =>
        {
            _trayExclude.Visible = _lastApp.Length > 0;
            _trayExclude.Text = S.TrayExclude(_lastApp);
        };
        _tray = new NotifyIcon { Icon = _badge.Render("..", false), Visible = true, Text = "ArzType", ContextMenuStrip = menu };
        _tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowWindow(); };

        Resize += (_, _) => { if (WindowState == FormWindowState.Minimized) Hide(); };
        FormClosing += (_, e) =>
        {
            if (!_exiting && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                _tray.ShowBalloonTip(1500, "ArzType", S.RunningInTray, ToolTipIcon.Info);
            }
        };
        FormClosed += (_, _) =>
        {
            _poll.Stop();
            SaveNow();
            _engine.Dispose();
            _gate.Dispose();
            _tray.Visible = false;
            _tray.Dispose();
            _badge.Dispose();
        };
        Microsoft.Win32.SystemEvents.PowerModeChanged += (_, e) =>
        {
            if (e.Mode == Microsoft.Win32.PowerModes.Resume) _engine.RequestRehook();
        };

        _toggle.Checked = _settings.Enabled;
        _toggle.CheckedChanged += (_, _) =>
        {
            _settings.Enabled = _toggle.Checked;
            ApplyEngineSettings();
            _engine.ResetTyping();
            UpdateStatus();
            SaveSoon();
        };
        _saveLater.Tick += (_, _) => { _saveLater.Stop(); SaveNow(); };
        _poll.Tick += (_, _) => Poll();
        UpdateStatus();
        UpdateStats();

        Load += (_, _) => Initialize();
    }

    private bool _initialized;
    private void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        {
            try { _engine.Start(); }
            catch (Exception e)
            {
                _toggle.Checked = false;
                MessageBox.Show(S.HookFailed(e.Message), "ArzType", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            RefreshLayouts(force: true);
            _poll.Start();
            if (_settings.StartWithWindows) AppSettings.ApplyStartup(true);
            if (!_settings.FirstRunDone)
            {
                _settings.FirstRunDone = true;
                SaveSoon();
                _tray.ShowBalloonTip(4000, "ArzType", S.Welcome, ToolTipIcon.Info);
            }
        }
    }

    protected override void SetVisibleCore(bool value)
    {
        if (_startHidden && !IsHandleCreated)
        {
            CreateHandle();
            value = false;
            Initialize();
        }
        base.SetVisibleCore(value);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == (int)ShowMessage && ShowMessage != 0) ShowWindow();
        base.WndProc(ref m);
    }

    private static Icon LoadAppIcon()
    {
        try { return Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? SystemIcons.Application; }
        catch { return SystemIcons.Application; }
    }

    // ───────────────────────── UI ─────────────────────────

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, AutoScroll = true, Padding = new Padding(22, 18, 22, 14), BackColor = Theme.Back
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Controls.Add(root);

        // Header
        var header = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, Height = 96, Margin = new Padding(0, 0, 0, 10) };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var titleBox = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, Dock = DockStyle.Fill, WrapContents = false };
        var title = new Label { Text = "ArzType", AutoSize = true, Font = new Font(Theme.FontName, 26f, FontStyle.Bold), ForeColor = Theme.Text, Margin = new Padding(0) };
        var tagline = new Label { Text = S.Tagline, AutoSize = true, MaximumSize = new Size(470, 0), ForeColor = Theme.Muted, Font = new Font(Theme.FontName, 10.5f), Margin = new Padding(2, 2, 0, 0) };
        titleBox.Controls.Add(title);
        titleBox.Controls.Add(tagline);
        header.Controls.Add(titleBox, 0, 0);
        var toggleBox = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Margin = new Padding(0, 14, 0, 0) };
        _toggle.Margin = new Padding(0, 0, 0, 4);
        _toggleLabel.AutoSize = true;
        _toggleLabel.Font = new Font(Theme.FontName, 9f, FontStyle.Bold);
        toggleBox.Controls.Add(_toggle);
        toggleBox.Controls.Add(_toggleLabel);
        header.Controls.Add(toggleBox, 1, 0);
        root.Controls.Add(header);

        _status.AutoSize = true;
        _status.Font = new Font(Theme.FontName, 10f, FontStyle.Bold);
        _status.Margin = new Padding(2, 0, 0, 12);
        root.Controls.Add(_status);

        // How it works
        var how = new Card { Title = S.HowTitle, Dock = DockStyle.Top, Height = 150 };
        var howList = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Theme.Card };
        foreach (var (badge, text) in new[] { ("⌨", S.How1), ("⇧⇧", S.How2), ("★", S.How3) })
            howList.Controls.Add(Tip(badge, text));
        how.Controls.Add(howList);
        root.Controls.Add(how);

        // Keyboards
        var kb = new Card { Title = S.KeyboardsTitle, Dock = DockStyle.Top, Height = 170 };
        _keyboards.Dock = DockStyle.Fill;
        _keyboards.FlowDirection = FlowDirection.TopDown;
        _keyboards.WrapContents = false;
        _keyboards.AutoScroll = true;
        _keyboards.BackColor = Theme.Card;
        kb.Controls.Add(_keyboards);
        root.Controls.Add(kb);

        // Options
        var opts = new Card { Title = S.OptionsTitle, Dock = DockStyle.Top, Height = 214 };
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, BackColor = Theme.Card };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        grid.Controls.Add(Option(S.StartWithWindows, _settings.StartWithWindows, v => { _settings.StartWithWindows = v; AppSettings.ApplyStartup(v); }));
        grid.Controls.Add(Option(S.FixOnEnter, _settings.FixOnEnter, v => _settings.FixOnEnter = v));
        grid.Controls.Add(Option(S.DoubleShift, _settings.DoubleShiftFix, v => _settings.DoubleShiftFix = v));
        grid.Controls.Add(Option(S.PauseKey, _settings.PauseKeyFix, v => _settings.PauseKeyFix = v));
        grid.Controls.Add(Option(S.Learn, _settings.LearnFromFixes, v => _settings.LearnFromFixes = v));
        var all = Option(S.AllApps, _settings.AllApps, v => { _settings.AllApps = v; _gate.AllowUnknownControls = v; });
        grid.Controls.Add(all);
        var sens = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, BackColor = Theme.Card, Margin = new Padding(0, 8, 0, 0) };
        sens.Controls.Add(new Label { Text = S.Sensitivity, AutoSize = true, ForeColor = Theme.Text, Margin = new Padding(0, 6, 10, 0) });
        sens.Controls.Add(new Label { Text = S.Careful, AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(0, 6, 4, 0) });
        var track = new TrackBar { Minimum = -2, Maximum = 2, Value = Math.Clamp(_settings.Sensitivity, -2, 2), TickStyle = TickStyle.None, Width = 160, BackColor = Theme.Card };
        track.ValueChanged += (_, _) => { _settings.Sensitivity = track.Value; ApplyEngineSettings(); SaveSoon(); };
        sens.Controls.Add(track);
        sens.Controls.Add(new Label { Text = S.Eager, AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(4, 6, 0, 0) });
        grid.Controls.Add(sens);
        grid.SetColumnSpan(sens, 2);
        opts.Controls.Add(grid);
        root.Controls.Add(opts);

        // Footer
        _stats.AutoSize = true;
        _stats.ForeColor = Theme.Accent;
        _stats.Font = new Font(Theme.FontName, 9.5f, FontStyle.Bold);
        _stats.Margin = new Padding(2, 0, 0, 8);
        root.Controls.Add(_stats);

        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0, 0, 0, 8) };
        buttons.Controls.Add(Btn(S.ExcludedApps, (_, _) => EditExclusions()));
        StyleButton(_forget);
        _forget.Click += (_, _) =>
        {
            if (MessageBox.Show(S.ConfirmForget, "ArzType", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                _rules.Clear();
        };
        buttons.Controls.Add(_forget);
        buttons.Controls.Add(Btn(S.PacksFolder, (_, _) =>
        {
            string dir = Path.Combine(AppSettings.Folder, "packs");
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
        }));
        root.Controls.Add(buttons);

        root.Controls.Add(new Label
        {
            Text = S.Privacy, AutoSize = true, MaximumSize = new Size(660, 0), ForeColor = Theme.Muted,
            Font = new Font(Theme.FontName, 8.75f), Margin = new Padding(2, 4, 0, 0)
        });
    }

    private static Control Tip(string badge, string text)
    {
        var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = Theme.Card, Margin = new Padding(0, 0, 0, 6) };
        row.Controls.Add(new Label
        {
            Text = badge, Width = 36, Height = 24, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Theme.Accent,
            Font = new Font(Theme.FontName, 11f, FontStyle.Bold), Margin = new Padding(0, 0, 8, 0)
        });
        row.Controls.Add(new Label { Text = text, AutoSize = true, MaximumSize = new Size(590, 0), ForeColor = Theme.Text, Margin = new Padding(0, 3, 0, 0) });
        return row;
    }

    private Control Option(string text, bool value, Action<bool> set)
    {
        var box = new CheckBox
        {
            Text = text, Checked = value, AutoSize = true, ForeColor = Theme.Text, BackColor = Theme.Card,
            FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 4, 8, 4), MaximumSize = new Size(320, 0)
        };
        box.FlatAppearance.BorderColor = Theme.Muted;
        box.FlatAppearance.CheckedBackColor = Theme.Accent;
        box.CheckedChanged += (_, _) => { set(box.Checked); ApplyEngineSettings(); SaveSoon(); };
        return box;
    }

    private static Button Btn(string text, EventHandler click)
    {
        var b = new Button { Text = text };
        StyleButton(b);
        b.Click += click;
        return b;
    }

    private static void StyleButton(Button b)
    {
        b.AutoSize = true;
        b.FlatStyle = FlatStyle.Flat;
        b.BackColor = Theme.Card;
        b.ForeColor = Theme.Text;
        b.FlatAppearance.BorderColor = Theme.CardBorder;
        b.FlatAppearance.MouseOverBackColor = Theme.CardBorder;
        b.Padding = new Padding(8, 3, 8, 3);
        b.Margin = new Padding(0, 0, 8, 0);
        b.Cursor = Cursors.Hand;
    }

    // ───────────────────────── behaviour ─────────────────────────

    private void ApplyEngineSettings()
    {
        _engine.Enabled = _settings.Enabled;
        _engine.FixOnEnter = _settings.FixOnEnter;
        _engine.DoubleShiftFix = _settings.DoubleShiftFix;
        _engine.PauseKeyFix = _settings.PauseKeyFix;
        bool learn = _settings.LearnFromFixes;
        int sensitivity = _settings.Sensitivity;
        _engine.Configure(c => { c.LearnFromManualFixes = learn; c.Options.Sensitivity = sensitivity; });
    }

    private void BeginInvokeSafe(Action a)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try { BeginInvoke(a); } catch (InvalidOperationException) { }
    }

    private void OnCorrected(Correction fix)
    {
        if (fix.Kind != CorrectionKind.Undo)
        {
            _sessionFixes++;
            _settings.TotalFixes++;
            _settings.TotalKeysSaved += fix.Backspaces + ArzType.Core.Text.CodePoints(fix.After);
        }
        UpdateStats();
        SaveSoon();
    }

    private void Poll()
    {
        var focus = NativeMethods.CurrentFocus();
        if (focus.ProcessId != 0 && focus.ProcessId != (uint)Environment.ProcessId)
        {
            string name = _gate.ProcessName(focus.ProcessId);
            if (name is not ("" or "explorer" or "ShellExperienceHost" or "SearchHost" or "StartMenuExperienceHost")) _lastApp = name;
        }
        string code = focus.Layout != 0 ? WindowsKeyMapper.Language(focus.Layout) : "..";
        var icon = _badge.Render(code, _toggle.Checked);
        if (!ReferenceEquals(_tray.Icon, icon)) _tray.Icon = icon;
        if (++_pollCount % 10 == 0) RefreshLayouts(force: false);
    }

    private void RefreshLayouts(bool force)
    {
        var current = NativeMethods.InstalledLayouts();
        if (!force && current.SequenceEqual(_installed)) return;
        _installed = current;
        var names = InputLanguage.InstalledInputLanguages.Cast<InputLanguage>().ToDictionary(l => l.Handle, l => l.LayoutName);
        var infos = new List<LayoutInfo>();
        foreach (nint hkl in current)
        {
            string lang = WindowsKeyMapper.Language(hkl);
            if (WindowsKeyMapper.IsComposing(lang)) continue;
            infos.Add(new LayoutInfo(hkl, lang, names.TryGetValue(hkl, out var n) ? n : ""));
        }
        _engine.SetLayouts(infos);
        var toLoad = infos.Select(i => i.Root).Distinct().Where(r => _model.HasLanguage(r) && _model.TryGetLoaded(r) is null).ToList();
        if (toLoad.Count > 0)
        {
            foreach (var l in toLoad) _loading.Add(l);
            Task.Run(() =>
            {
                foreach (var l in toLoad)
                {
                    try { _model.Get(l); } catch { }
                    BeginInvokeSafe(() => { _loading.Remove(l); RenderKeyboards(); });
                }
            });
        }
        RenderKeyboards();
    }

    private void RenderKeyboards()
    {
        _keyboards.SuspendLayout();
        _keyboards.Controls.Clear();
        var names = InputLanguage.InstalledInputLanguages.Cast<InputLanguage>().ToDictionary(l => l.Handle, l => l.LayoutName);
        foreach (nint hkl in _installed)
        {
            string lang = WindowsKeyMapper.Language(hkl);
            string root = LanguageModel.Root(lang);
            bool ime = WindowsKeyMapper.IsComposing(lang);
            bool smart = !ime && _model.HasLanguage(root);
            string state = ime ? S.Ime : smart ? (_loading.Contains(root) ? S.Smart + " — " + S.Loading : S.Smart) : S.ManualOnly;
            Color color = ime ? Theme.Off : smart ? Theme.Accent : Theme.Warn;
            var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = Theme.Card, Margin = new Padding(0, 0, 0, 4) };
            row.Controls.Add(new Label
            {
                Text = lang.ToUpperInvariant(), Width = 40, Height = 22, TextAlign = ContentAlignment.MiddleCenter,
                BackColor = color, ForeColor = Color.White, Font = new Font(Theme.FontName, 8.5f, FontStyle.Bold), Margin = new Padding(0, 0, 10, 0)
            });
            string layoutName = names.TryGetValue(hkl, out var n) ? n : "";
            row.Controls.Add(new Label { Text = $"{WindowsKeyMapper.LanguageName(hkl)}  ·  {layoutName}", AutoSize = true, ForeColor = Theme.Text, Margin = new Padding(0, 3, 10, 0) });
            row.Controls.Add(new Label { Text = state, AutoSize = true, ForeColor = color, Margin = new Padding(0, 3, 0, 0) });
            _keyboards.Controls.Add(row);
        }
        if (_installed.Count < 2)
            _keyboards.Controls.Add(new Label { Text = S.AddKeyboardHint, AutoSize = true, MaximumSize = new Size(620, 0), ForeColor = Theme.Warn, Margin = new Padding(0, 6, 0, 0) });
        _keyboards.ResumeLayout();
    }

    private void UpdateStatus()
    {
        bool on = _toggle.Checked;
        _toggleLabel.Text = $"{S.AutoFix}: {(on ? S.On : S.Off)}";
        _toggleLabel.ForeColor = on ? Theme.Accent : Theme.Muted;
        _status.Text = (on ? "●  " : "○  ") + (on ? S.Active : S.Paused);
        _status.ForeColor = on ? Theme.Accent : Theme.Muted;
        _trayToggle.Checked = on;
        _tray.Text = "ArzType — " + (on ? S.On : S.Off);
    }

    private void UpdateStats()
    {
        _stats.Text = S.Stats(_sessionFixes, _settings.TotalFixes, _settings.TotalKeysSaved);
        _forget.Text = S.Forget(_rules.Count);
    }

    private void ExcludeApp(string app)
    {
        if (app.Length == 0 || _settings.ExcludedApps.Contains(app, StringComparer.OrdinalIgnoreCase)) return;
        _settings.ExcludedApps.Add(app);
        _gate.SetExclusions(_settings.ExcludedApps);
        _engine.ResetTyping();
        SaveSoon();
    }

    private void EditExclusions()
    {
        using var dlg = new Form
        {
            Text = S.ExcludedApps.TrimEnd('…'), ClientSize = new Size(440, 420), StartPosition = FormStartPosition.CenterParent,
            BackColor = Theme.Back, ForeColor = Theme.Text, Font = Font, MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false,
            RightToLeft = RightToLeft, RightToLeftLayout = RightToLeftLayout
        };
        var label = new Label { Text = S.ExcludedTitle, Dock = DockStyle.Top, Height = 44, Padding = new Padding(10, 10, 10, 0) };
        var box = new TextBox
        {
            Multiline = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, BackColor = Theme.Card, ForeColor = Theme.Text,
            BorderStyle = BorderStyle.FixedSingle, Font = new Font("Consolas", 10f), RightToLeft = RightToLeft.No,
            Text = string.Join(Environment.NewLine, _settings.ExcludedApps)
        };
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        var save = Btn(S.Save, (_, _) => dlg.DialogResult = DialogResult.OK);
        var cancel = Btn(S.Cancel, (_, _) => dlg.DialogResult = DialogResult.Cancel);
        bar.Controls.Add(save);
        bar.Controls.Add(cancel);
        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 0, 10, 0) };
        body.Controls.Add(box);
        dlg.Controls.Add(body);
        dlg.Controls.Add(bar);
        dlg.Controls.Add(label);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        _settings.ExcludedApps = box.Lines.Select(l => l.Trim()).Where(l => l.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        _gate.SetExclusions(_settings.ExcludedApps);
        _engine.ResetTyping();
        SaveSoon();
    }

    private void ShowWindow()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private void PersistRules()
    {
        var (never, always) = _rules.Export();
        _settings.NeverFingerprints = [.. never];
        _settings.AlwaysFingerprints = always;
        SaveSoon();
    }

    private void SaveSoon() { _saveLater.Stop(); _saveLater.Start(); }
    private void SaveNow() { _saveLater.Stop(); _settings.Save(); }
}
