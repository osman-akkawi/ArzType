using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace ArzType.App;

internal static class Theme
{
    public static readonly Color Back = Color.FromArgb(15, 18, 26);
    public static readonly Color Card = Color.FromArgb(24, 29, 41);
    public static readonly Color CardBorder = Color.FromArgb(38, 45, 62);
    public static readonly Color Text = Color.FromArgb(236, 240, 248);
    public static readonly Color Muted = Color.FromArgb(150, 160, 180);
    public static readonly Color Accent = Color.FromArgb(56, 189, 148);   // green-teal: "fixed"
    public static readonly Color Accent2 = Color.FromArgb(99, 140, 255);  // blue
    public static readonly Color Warn = Color.FromArgb(240, 180, 70);
    public static readonly Color Off = Color.FromArgb(90, 98, 116);
    public static readonly string FontName = "Segoe UI";

    public static GraphicsPath Round(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        float d = radius * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}

/// <summary>Rounded card with a title.</summary>
internal sealed class Card : Panel
{
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Title { get; set; } = "";
    public Card()
    {
        DoubleBuffered = true;
        BackColor = Theme.Back;
        Padding = new Padding(18, 40, 18, 14);
        Margin = new Padding(0, 0, 0, 12);
        SetStyle(ControlStyles.ResizeRedraw, true);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Theme.Round(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 12);
        using var fill = new SolidBrush(Theme.Card);
        using var pen = new Pen(Theme.CardBorder);
        g.FillPath(fill, path);
        g.DrawPath(pen, path);
        using var f = new Font(Theme.FontName, 10.5f, FontStyle.Bold);
        var flags = TextFormatFlags.Left | (RightToLeft == RightToLeft.Yes ? TextFormatFlags.RightToLeft | TextFormatFlags.Right : 0);
        TextRenderer.DrawText(g, Title, f, new Rectangle(18, 12, Width - 36, 24), Theme.Text, flags);
    }
    protected override void OnPaintBackground(PaintEventArgs e) => e.Graphics.Clear(Theme.Back);
}

/// <summary>iOS-style switch.</summary>
internal sealed class ToggleSwitch : Control
{
    private bool _checked;
    public event EventHandler? CheckedChanged;
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Checked
    {
        get => _checked;
        set { if (_checked == value) return; _checked = value; Invalidate(); CheckedChanged?.Invoke(this, EventArgs.Empty); }
    }
    public ToggleSwitch()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
        Size = new Size(64, 34);
        Cursor = Cursors.Hand;
        TabStop = true;
    }
    protected override void OnClick(EventArgs e) { Checked = !Checked; base.OnClick(e); }
    protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode is Keys.Space or Keys.Enter) Checked = !Checked; base.OnKeyDown(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? Theme.Back);
        var r = new RectangleF(1, 1, Width - 3, Height - 3);
        using var track = Theme.Round(r, r.Height / 2);
        using var fill = new SolidBrush(_checked ? Theme.Accent : Theme.Off);
        g.FillPath(fill, track);
        float d = r.Height - 6;
        float x = _checked ? r.Right - d - 3 : r.X + 3;
        using var knob = new SolidBrush(Color.White);
        g.FillEllipse(knob, x, r.Y + 3, d, d);
        if (Focused) { using var pen = new Pen(Theme.Accent2, 2); g.DrawPath(pen, track); }
    }
}

/// <summary>Draws the tray icon: the current input language code on a coloured badge.</summary>
internal sealed class TrayBadge : IDisposable
{
    private Icon? _icon;
    private nint _handle;
    private string _key = "";

    public Icon Render(string code, bool active)
    {
        string key = code + active;
        if (_icon is not null && key == _key) return _icon;
        _key = key;
        int size = Math.Max(16, SystemInformation.SmallIconSize.Width) * 2;
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);
            using var path = Theme.Round(new RectangleF(0, size * 0.08f, size - 1, size * 0.84f), size * 0.22f);
            using var fill = new SolidBrush(active ? Theme.Accent : Theme.Off);
            g.FillPath(fill, path);
            string text = code.Length > 2 ? code[..2] : code;
            using var font = new Font(Theme.FontName, size * 0.40f, FontStyle.Bold, GraphicsUnit.Pixel);
            using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(text.ToUpperInvariant(), font, Brushes.White, new RectangleF(0, size * 0.04f, size, size), sf);
        }
        nint h = bmp.GetHicon();
        var icon = Icon.FromHandle(h);
        var old = _handle;
        _icon = icon;
        _handle = h;
        if (old != 0) NativeMethods.DestroyIcon(old);
        return icon;
    }

    public void Dispose()
    {
        if (_handle != 0) NativeMethods.DestroyIcon(_handle);
        _handle = 0;
    }
}
