namespace EzEizo.Core;

internal sealed class NativeSignature : Control
{
    private readonly List<List<Point>> strokes = [];
    private List<Point>? active;
    private Image? initial;
    internal bool HasSignature => strokes.Count > 0 || initial is not null;

    internal NativeSignature(string? dataUrl = null)
    {
        Size = new Size(470, 115);
        BackColor = Color.White;
        Cursor = Cursors.Cross;
        DoubleBuffered = true;
        if (dataUrl?.StartsWith("data:image/png;base64,", StringComparison.Ordinal) == true)
        {
            try
            {
                using var stream = new MemoryStream(Convert.FromBase64String(dataUrl[22..]));
                using var image = Image.FromStream(stream);
                initial = new Bitmap(image);
            }
            catch { initial = null; }
        }
    }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        active = [e.Location]; strokes.Add(active); Capture = true; Invalidate();
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (active is null) return;
        active.Add(e.Location); Invalidate();
    }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); active = null; Capture = false; }
    internal void Clear() { initial?.Dispose(); initial = null; strokes.Clear(); Invalidate(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.Clear(Color.White);
        if (initial is not null) e.Graphics.DrawImage(initial, ClientRectangle);
        using var pen = new Pen(Color.FromArgb(15, 49, 75), 2.5f) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round };
        foreach (var points in strokes)
            if (points.Count == 1) e.Graphics.DrawEllipse(pen, points[0].X, points[0].Y, 1, 1);
            else if (points.Count > 1) e.Graphics.DrawLines(pen, points.ToArray());
        using var border = new Pen(Color.LightGray);
        e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
    }
    internal string DataUrl()
    {
        if (!HasSignature) return "";
        using var bitmap = new Bitmap(Width, Height);
        using var graphics = Graphics.FromImage(bitmap);
        OnPaint(new PaintEventArgs(graphics, new Rectangle(Point.Empty, Size)));
        using var stream = new MemoryStream(); bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
        return "data:image/png;base64," + Convert.ToBase64String(stream.ToArray());
    }
    protected override void Dispose(bool disposing) { if (disposing) initial?.Dispose(); base.Dispose(disposing); }
}
