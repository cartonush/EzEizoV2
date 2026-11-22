using System.Drawing.Drawing2D;
using System.ComponentModel;

namespace EzEizo.Core;

internal sealed class NativeButton : Button
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Secondary { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Active { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Danger { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool OnDark { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string? TileTitle { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string? TileDescription { get; set; }
    private bool hovering;

    public NativeButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                 ControlStyles.Opaque, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        Cursor = Cursors.Hand;
    }
    protected override void OnMouseEnter(EventArgs e) { hovering = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hovering = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        // A native Button can leave the unpainted rounded corners black on some
        // Windows themes. Paint the entire surface before drawing the rounded face.
        e.Graphics.Clear(Parent?.BackColor ?? Color.White);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new Rectangle(1, 1, Width - 3, Height - 3);
        if (bounds.Width < 2 || bounds.Height < 2) return;
        using var path = Rounded(bounds, 10);
        var fill = !Enabled ? Color.FromArgb(228, 235, 239) :
            OnDark ? hovering ? Color.FromArgb(48, 99, 132) : Color.FromArgb(34, 79, 110) :
            Active || !Secondary ? hovering ? Color.FromArgb(17, 88, 127) : Color.FromArgb(30, 107, 158) :
            Danger ? Color.FromArgb(255, 243, 244) : hovering ? Color.FromArgb(232, 246, 251) : Color.White;
        using var brush = new SolidBrush(fill);
        e.Graphics.FillPath(brush, path);
        using var pen = new Pen(Focused && ShowFocusCues ? Color.FromArgb(79, 158, 194) :
            OnDark ? Color.FromArgb(113, 150, 173) :
            Active || !Secondary ? fill : Danger ? Color.FromArgb(229, 179, 189) : Color.FromArgb(200, 220, 231));
        e.Graphics.DrawPath(pen, path);
        var foreground = !Enabled ? Color.FromArgb(140, 156, 166) : OnDark || Active || !Secondary ? Color.White :
            Danger ? Color.FromArgb(173, 23, 43) : Color.FromArgb(23, 102, 143);
        if (TileTitle is not null)
        {
            var textFlags = TextFormatFlags.RightToLeft | TextFormatFlags.Right |
                TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis;
            using var titleFont = NativeFonts.Create(13.5f, FontStyle.Bold);
            using var descriptionFont = NativeFonts.Create(9.75f);
            TextRenderer.DrawText(e.Graphics, TileTitle, titleFont,
                new Rectangle(bounds.Left + 17, bounds.Top + 19, bounds.Width - 34, 29),
                Color.FromArgb(23, 59, 93), textFlags);
            TextRenderer.DrawText(e.Graphics, TileDescription ?? "", descriptionFont,
                new Rectangle(bounds.Left + 17, bounds.Top + 51, bounds.Width - 34, bounds.Height - 54),
                Color.FromArgb(96, 118, 134), textFlags);
        }
        else
            TextRenderer.DrawText(e.Graphics, Text, Font, bounds, foreground,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis | TextFormatFlags.RightToLeft);
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

internal static class NativeCheckPainter
{
    public static void Draw(Graphics graphics, Rectangle bounds, bool isChecked, bool enabled = true)
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var shape = NativeButton.Rounded(bounds, 6);
        using var fill = new SolidBrush(!enabled ? Color.FromArgb(238, 243, 245) :
            isChecked ? Color.FromArgb(22, 139, 121) : Color.White);
        using var edge = new Pen(isChecked ? Color.FromArgb(22, 139, 121) : Color.FromArgb(189, 207, 217), 1.5f);
        graphics.FillPath(fill, shape);
        graphics.DrawPath(edge, shape);
        if (!isChecked) return;
        using var tick = new Pen(Color.White, 2.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        graphics.DrawLines(tick, [new Point(bounds.Left + 5, bounds.Top + 11),
            new Point(bounds.Left + 9, bounds.Top + 15), new Point(bounds.Left + 17, bounds.Top + 6)]);
    }
}

internal sealed class NativeCheckBox : CheckBox
{
    public NativeCheckBox()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Hand;
        BackColor = Color.White;
        Font = NativeFonts.Create(9.75f);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        var square = new Rectangle(Width - 30, (Height - 23) / 2, 22, 22);
        NativeCheckPainter.Draw(e.Graphics, square, Checked, Enabled);
        var bounds = new Rectangle(8, 3, Math.Max(1, Width - 48), Height - 6);
        TextRenderer.DrawText(e.Graphics, Text, Font, bounds,
            Enabled ? Color.FromArgb(21, 43, 64) : Color.Gray,
            TextFormatFlags.RightToLeft | TextFormatFlags.Right | TextFormatFlags.VerticalCenter |
            TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, ClientRectangle);
    }
}

internal sealed class NativeCheckedListBox : CheckedListBox
{
    public NativeCheckedListBox()
    {
        DrawMode = DrawMode.OwnerDrawFixed;
        ItemHeight = 34;
        CheckOnClick = true;
        BorderStyle = BorderStyle.FixedSingle;
        BackColor = Color.FromArgb(251, 253, 254);
        Font = NativeFonts.Create(9.5f);
    }
    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        using var background = new SolidBrush((e.State & DrawItemState.Selected) != 0
            ? Color.FromArgb(229, 244, 249) : BackColor);
        e.Graphics.FillRectangle(background, e.Bounds);
        NativeCheckPainter.Draw(e.Graphics,
            new Rectangle(e.Bounds.Right - 31, e.Bounds.Y + (e.Bounds.Height - 22) / 2, 21, 21), GetItemChecked(e.Index), Enabled);
        TextRenderer.DrawText(e.Graphics, Items[e.Index]?.ToString() ?? "", Font,
            new Rectangle(e.Bounds.X + 8, e.Bounds.Y, Math.Max(1, e.Bounds.Width - 48), e.Bounds.Height),
            Color.FromArgb(21, 43, 64), TextFormatFlags.RightToLeft | TextFormatFlags.Right |
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        e.DrawFocusRectangle();
    }
}

internal sealed class NativeSelectFrame : Panel
{
    public NativeSelectFrame()
    {
        Height = 43;
        BackColor = Color.White;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var shape = NativeButton.Rounded(new Rectangle(1, 1, Width - 3, Height - 3), 9);
        using var fill = new SolidBrush(Color.FromArgb(251, 253, 254));
        using var edge = new Pen(Color.FromArgb(207, 222, 230), 1.3f);
        e.Graphics.FillPath(fill, shape); e.Graphics.DrawPath(edge, shape);
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

internal sealed class NativeHeader : Panel
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Manager { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Heading { get; set; } = "";
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Subtitle { get; set; } = "";
    public NativeHeader()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Color.FromArgb(13, 48, 75);
    }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        using var gradient = new LinearGradientBrush(ClientRectangle,
            Manager ? Color.FromArgb(16, 54, 79) : Color.FromArgb(10, 35, 59),
            Manager ? Color.FromArgb(18, 82, 120) : Color.FromArgb(16, 58, 93),
            LinearGradientMode.Horizontal);
        e.Graphics.FillRectangle(gradient, ClientRectangle);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var left = Width - 595;
        const TextFormatFlags flags = TextFormatFlags.RightToLeft | TextFormatFlags.Right |
            TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
        using var eyebrowFont = NativeFonts.Create(8.25f, FontStyle.Bold);
        using var headingFont = NativeFonts.Create(14.25f, FontStyle.Bold);
        using var subtitleFont = NativeFonts.Create(9);
        TextRenderer.DrawText(e.Graphics, "כנף 1 - טייסת תחזוקה", eyebrowFont,
            new Rectangle(left, 5, 500, 17), Color.FromArgb(176, 203, 221), flags);
        TextRenderer.DrawText(e.Graphics, Heading, headingFont,
            new Rectangle(left, 23, 500, 27), Color.White, flags);
        TextRenderer.DrawText(e.Graphics, Subtitle, subtitleFont,
            new Rectangle(left, 52, 500, 18), Color.FromArgb(195, 216, 229), flags);
    }
}

internal sealed class NativeBrandMark : Control
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Manager { get; set; }
    public NativeBrandMark()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Opaque, true);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? Color.FromArgb(13, 48, 75));
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = NativeButton.Rounded(new Rectangle(1, 1, Width - 3, Height - 3), 12);
        using var gradient = new LinearGradientBrush(ClientRectangle,
            Manager ? Color.FromArgb(162, 223, 201) : Color.FromArgb(104, 211, 221),
            Manager ? Color.FromArgb(79, 181, 165) : Color.FromArgb(41, 129, 162),
            LinearGradientMode.ForwardDiagonal);
        e.Graphics.FillPath(gradient, path);
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle,
            Color.FromArgb(8, 35, 60), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}

internal sealed class NativeBadge : Control
{
    public NativeBadge()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Opaque, true);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? Color.FromArgb(13, 48, 75));
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = NativeButton.Rounded(new Rectangle(1, 1, Width - 3, Height - 3), Height / 2);
        using var brush = new SolidBrush(Color.FromArgb(29, 73, 103));
        using var border = new Pen(Color.FromArgb(83, 119, 143));
        e.Graphics.FillPath(brush, path);
        e.Graphics.DrawPath(border, path);
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle,
            Color.FromArgb(221, 238, 246), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
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
