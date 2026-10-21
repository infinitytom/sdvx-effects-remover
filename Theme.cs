using System.Drawing.Drawing2D;

namespace SdvxSmooth;

static class Theme
{
    public static readonly Color Background = Color.FromArgb(8, 10, 15), Surface = Color.FromArgb(16, 20, 28),
        Border = Color.FromArgb(37, 44, 57), Text = Color.FromArgb(233, 239, 249), Muted = Color.FromArgb(141, 154, 175),
        Accent = Color.FromArgb(94, 223, 244), Pink = Color.FromArgb(239, 92, 171);
    public static Label Label(string text, int size = 10, bool bold = false, Color? color = null) => new()
    { Text = text, AutoSize = true, ForeColor = color ?? Text, Font = new Font("Microsoft YaHei UI", size, bold ? FontStyle.Bold : FontStyle.Regular) };
    public static Button Button(string text, EventHandler click, int width = 110, bool primary = false)
    {
        var button = new Button { Text = text, Width = width, Height = 38, Cursor = Cursors.Hand, FlatStyle = FlatStyle.Flat,
            BackColor = primary ? Accent : Surface, ForeColor = primary ? Background : Text, Margin = new Padding(0, 0, 10, 0) };
        button.FlatAppearance.BorderColor = primary ? Accent : Border;
        button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(144, 239, 251) : Color.FromArgb(31, 39, 52);
        button.Click += click; return button;
    }
    public static void Apply(Control root)
    {
        root.BackColor = Background; root.ForeColor = Text;
        foreach (Control child in root.Controls)
        {
            Apply(child);
            if (child is TextBox text) { text.BackColor = Surface; text.ForeColor = Text; text.BorderStyle = BorderStyle.FixedSingle; }
            if (child is Button button) { button.FlatStyle = FlatStyle.Flat; button.BackColor = Surface; button.FlatAppearance.BorderColor = Border; }
            if (child is Label) child.ForeColor = Muted;
        }
    }
}

sealed class OptionSwitch : CheckBox
{
    public string Subtitle { get; }
    public OptionSwitch(string title, string subtitle)
    {
        Text = title; Subtitle = subtitle; AccessibleDescription = subtitle;
        AutoSize = false; Dock = DockStyle.Fill; Height = 62; Margin = new Padding(0, 0, 0, 6);
        Cursor = Cursors.Hand; SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Theme.Surface);
        using var border = new Pen(Focused ? Theme.Accent : Theme.Border);
        g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        var textWidth = Math.Max(20, Width - 78);
        TextRenderer.DrawText(g, Text, Font, new Rectangle(14, 8, textWidth, 24), Enabled ? Theme.Text : Theme.Muted, TextFormatFlags.EndEllipsis);
        using var small = new Font(Font.FontFamily, 9);
        TextRenderer.DrawText(g, Subtitle, small, new Rectangle(14, 33, textWidth, 22), Theme.Muted, TextFormatFlags.EndEllipsis);
        var track = new Rectangle(Width - 58, (Height - 22) / 2, 40, 22);
        using var brush = new SolidBrush(Checked ? Theme.Accent : Theme.Border);
        g.FillRoundedRectangle(brush, track, 11);
        using var thumb = new SolidBrush(Checked ? Theme.Background : Theme.Muted);
        g.FillEllipse(thumb, track.X + (Checked ? 21 : 3), track.Y + 3, 16, 16);
    }
    protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
}

static class DrawingExtensions
{
    public static void FillRoundedRectangle(this Graphics g, Brush brush, Rectangle r, int radius)
    {
        int d = radius * 2; using var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90); path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure(); g.FillPath(brush, path);
    }
}

sealed class LanePreview : Control
{
    public EffectMode Mode { get; set; } = EffectMode.Original;
    public LanePreview() { DoubleBuffered = true; Dock = DockStyle.Fill; MinimumSize = new Size(230, 280); AccessibleName = "轨道效果示意图，非游戏截图"; }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Mode.Background || Mode.DeepBlack ? Color.Black : Color.FromArgb(25, 29, 46));
        float w = Width, h = Height, cx = w / 2, top = 80, bottom = h - 60;
        PointF P(float x, float t) => new(cx + x * (0.12f + t * 0.88f) * w * .43f, top + (bottom - top) * t);
        using var floor = new SolidBrush(Color.FromArgb(8, 11, 17));
        g.FillPolygon(floor, new[] { P(-1, 0), P(1, 0), P(1, 1), P(-1, 1) });
        using var grid = new Pen(Color.FromArgb(40, 48, 61));
        foreach (float x in new[] { -.5f, 0, .5f }) g.DrawLine(grid, P(x, 0), P(x, 1));
        using var blue = new Pen(Theme.Accent, 2); using var pink = new Pen(Theme.Pink, 2);
        g.DrawLine(blue, P(-1, 0), P(-1, 1)); g.DrawLine(pink, P(1, 0), P(1, 1));
        using var laser = new Pen(Color.FromArgb(120, Theme.Accent), 8);
        g.DrawLines(laser, new[] { P(-.8f, .25f), P(.1f, .48f), P(-.7f, .65f), P(-.7f, 1) });
        using var note = new Pen(Theme.Text, 5);
        foreach (var (x, t) in new[] { (.4f, .32f), (-.2f, .58f), (.65f, .78f) }) g.DrawLine(note, P(x - .18f, t), P(x + .18f, t));
        using var judgment = new Pen(Color.FromArgb(196, 212, 239), 2); g.DrawLine(judgment, 16, bottom, w - 16, bottom);
        using var glow = new SolidBrush(Color.FromArgb(65, Theme.Accent)); g.FillEllipse(glow, cx - 44, bottom - 18, 48, 36);
        g.DrawEllipse(blue, cx - 38, bottom - 12, 36, 24);
        using var title = new Font("Microsoft YaHei UI", 10, FontStyle.Bold);
        using var caption = new Font("Microsoft YaHei UI", 9);
        TextRenderer.DrawText(g, Mode.DeepBlack ? "DEEP BLACK" : "LANE PREVIEW", title, new Point(20, 20), Theme.Accent);
        TextRenderer.DrawText(g, "轨道效果示意 · 非游戏截图", caption, new Point(18, Height - 30), Theme.Muted);
        if (!Mode.Judgment) TextRenderer.DrawText(g, "CHAIN  0128", caption, new Point((int)cx - 45, (int)bottom - 58), Theme.Text);
    }
}
