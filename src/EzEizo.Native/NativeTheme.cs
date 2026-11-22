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


internal sealed class NativeCheckBox : CheckBox
{
    private static readonly Color Teal = Color.FromArgb(22, 139, 121);
    private static readonly Color Ink = Color.FromArgb(21, 43, 64);
    private bool hovering;

    public NativeCheckBox()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        Cursor = Cursors.Hand;
        BackColor = Color.FromArgb(249, 251, 252);
        ForeColor = Ink;
        Padding = new Padding(12, 0, 12, 0);
    }

    protected override void OnMouseEnter(EventArgs e) { hovering = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hovering = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); Invalidate(); }

    internal static void DrawMark(Graphics g, Rectangle mark, bool isChecked, bool enabled = true)
    {
        var fillColor = isChecked ? Color.FromArgb(22, 139, 121) : Color.White;
        var borderColor = isChecked ? Color.FromArgb(22, 139, 121) : Color.FromArgb(174, 191, 201);
        using (var markPath = NativeButton.Rounded(mark, 6))
        using (var markFill = new SolidBrush(enabled ? fillColor : Color.FromArgb(240, 244, 246)))
        using (var markBorder = new Pen(enabled ? borderColor : Color.FromArgb(205, 215, 221), 1.5f))
        {
            g.FillPath(markFill, markPath);
            g.DrawPath(markBorder, markPath);
        }
        if (isChecked)
        {
            using var checkPen = new Pen(enabled ? Color.White : Color.FromArgb(126, 143, 153), 2.2f)
                { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLines(checkPen, [new Point(mark.Left + 6, mark.Top + 12),
                new Point(mark.Left + 10, mark.Top + 16), new Point(mark.Left + 18, mark.Top + 7)]);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? Color.White);

        var bounds = new Rectangle(1, 1, Width - 3, Height - 3);
        if (bounds.Width < 2 || bounds.Height < 2) return;
        var surface = !Enabled ? Color.FromArgb(240, 244, 246) :
            hovering ? Color.FromArgb(243, 249, 250) : BackColor;
        var borderColor = Checked ? Color.FromArgb(173, 216, 205) : Color.FromArgb(223, 233, 238);
        using (var path = NativeButton.Rounded(bounds, 12))
        using (var brush = new SolidBrush(surface))
        using (var pen = new Pen(borderColor))
        {
            g.FillPath(brush, path);
            g.DrawPath(pen, path);
            if (Focused && ShowFocusCues)
            {
                using var focusPen = new Pen(Color.FromArgb(79, 158, 194), 2);
                g.DrawPath(focusPen, path);
            }
        }

        const int size = 23;
        var markX = RightToLeft == RightToLeft.Yes ? Width - Padding.Right - size - 6 : Padding.Left + 6;
        var mark = new Rectangle(markX, Math.Max(4, (Height - size) / 2), size, size);
        DrawMark(g, mark, Checked, Enabled);

        var textLeft = RightToLeft == RightToLeft.Yes ? Padding.Left + 4 : mark.Right + 10;
        var textRight = RightToLeft == RightToLeft.Yes ? mark.Left - 10 : Width - Padding.Right - 4;
        var textBounds = new Rectangle(textLeft, 3, Math.Max(0, textRight - textLeft), Height - 6);
        var flags = TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis |
            (RightToLeft == RightToLeft.Yes ? TextFormatFlags.RightToLeft | TextFormatFlags.Right : TextFormatFlags.Left);
        TextRenderer.DrawText(g, Text, Font, textBounds, Enabled ? ForeColor : Color.FromArgb(126, 143, 153), flags);
    }
}

internal sealed class NativeCheckedListBox : CheckedListBox
{
    private static readonly Color Teal = Color.FromArgb(22, 139, 121);
    public NativeCheckedListBox()
    {
        DrawMode = DrawMode.OwnerDrawFixed;
        ItemHeight = 36;
        IntegralHeight = false;
        CheckOnClick = true;
        BorderStyle = BorderStyle.None;
        BackColor = Color.White;
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= Items.Count) return;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var row = Rectangle.Inflate(e.Bounds, -2, -2);
        var selected = (e.State & DrawItemState.Selected) != 0;
        using (var path = NativeButton.Rounded(row, 8))
        using (var fill = new SolidBrush(selected ? Color.FromArgb(235, 246, 249) : Color.FromArgb(249, 251, 252)))
        using (var border = new Pen(selected ? Color.FromArgb(139, 190, 207) : Color.FromArgb(225, 233, 238)))
        {
            g.FillPath(fill, path);
            g.DrawPath(border, path);
        }

        var size = 20;
        var mark = new Rectangle(RightToLeft == RightToLeft.Yes ? row.Right - 32 : row.Left + 10,
            row.Top + (row.Height - size) / 2, size, size);
        using (var markPath = NativeButton.Rounded(mark, 5))
        using (var markFill = new SolidBrush(GetItemChecked(e.Index) ? Teal : Color.White))
        using (var markBorder = new Pen(GetItemChecked(e.Index) ? Teal : Color.FromArgb(174, 191, 201), 1.4f))
        {
            g.FillPath(markFill, markPath);
            g.DrawPath(markBorder, markPath);
        }
        if (GetItemChecked(e.Index))
        {
            using var pen = new Pen(Color.White, 2) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLines(pen, [new Point(mark.Left + 5, mark.Top + 10),
                new Point(mark.Left + 9, mark.Top + 14), new Point(mark.Left + 16, mark.Top + 6)]);
        }
        var textLeft = RightToLeft == RightToLeft.Yes ? row.Left + 10 : mark.Right + 10;
        var textRight = RightToLeft == RightToLeft.Yes ? mark.Left - 10 : row.Right - 10;
        var textBounds = new Rectangle(textLeft, row.Top, Math.Max(0, textRight - textLeft), row.Height);
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
            (RightToLeft == RightToLeft.Yes ? TextFormatFlags.RightToLeft | TextFormatFlags.Right : TextFormatFlags.Left);
        TextRenderer.DrawText(g, GetItemText(Items[e.Index]), Font, textBounds,
            Enabled ? Color.FromArgb(21, 43, 64) : Color.FromArgb(126, 143, 153), flags);
        if ((e.State & DrawItemState.Focus) != 0) e.DrawFocusRectangle();
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
