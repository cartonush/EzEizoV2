using System.Drawing.Drawing2D;

namespace EzEizo.Core;

internal sealed class NativeButton : Button
{
    public bool Secondary { get; set; }
    public bool Active { get; set; }
    public bool Danger { get; set; }
    private bool hovering;

    public NativeButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
    }
    protected override void OnMouseEnter(EventArgs e) { hovering = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hovering = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new Rectangle(1, 1, Width - 3, Height - 3);
        if (bounds.Width < 2 || bounds.Height < 2) return;
        using var path = Rounded(bounds, 10);
        var fill = !Enabled ? Color.FromArgb(228, 235, 239) :
            Active || !Secondary ? hovering ? Color.FromArgb(17, 88, 127) : Color.FromArgb(30, 107, 158) :
            Danger ? Color.FromArgb(255, 243, 244) : hovering ? Color.FromArgb(232, 246, 251) : Color.White;
        using var brush = new SolidBrush(fill);
        e.Graphics.FillPath(brush, path);
        using var pen = new Pen(Active || !Secondary ? fill : Danger ? Color.FromArgb(229, 179, 189) : Color.FromArgb(200, 220, 231));
        e.Graphics.DrawPath(pen, path);
        var foreground = !Enabled ? Color.FromArgb(140, 156, 166) : Active || !Secondary ? Color.White :
            Danger ? Color.FromArgb(173, 23, 43) : Color.FromArgb(23, 102, 143);
        TextRenderer.DrawText(e.Graphics, Text, Font, bounds, foreground,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
            TextFormatFlags.EndEllipsis | TextFormatFlags.RightToLeft);
        if (Focused) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(bounds, -4, -4));
    }
    internal static GraphicsPath Rounded(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var d = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class NativeCard : Panel
{
    public NativeCard()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Color.White;
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = NativeButton.Rounded(new Rectangle(1, 1, Width - 3, Height - 3), 18);
        using var white = new SolidBrush(Color.White);
        using var border = new Pen(Color.FromArgb(227, 236, 241));
        e.Graphics.FillPath(white, path);
        e.Graphics.DrawPath(border, path);
        base.OnPaint(e);
    }
}

internal sealed class NativeSkeletonBlock : Control
{
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 75 };
    private int offset;
    public NativeSkeletonBlock(int height)
    {
        Width = 745; Height = height; Margin = new Padding(0, 0, 0, 17);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer, true);
        timer.Tick += (_, _) => { offset = (offset + 12) % 820; Invalidate(); };
        timer.Start();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = NativeButton.Rounded(new Rectangle(1, 1, Width - 3, Height - 3), 16);
        using var baseBrush = new SolidBrush(Color.FromArgb(232, 240, 244));
        e.Graphics.FillPath(baseBrush, path);
        e.Graphics.SetClip(path);
        using var shimmer = new LinearGradientBrush(new Rectangle(offset - 280, 0, 280, Height),
            Color.FromArgb(232, 240, 244), Color.FromArgb(250, 252, 253), LinearGradientMode.Horizontal);
        e.Graphics.FillRectangle(shimmer, offset - 280, 0, 280, Height);
        e.Graphics.ResetClip();
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { timer.Stop(); timer.Dispose(); }
        base.Dispose(disposing);
    }
}
