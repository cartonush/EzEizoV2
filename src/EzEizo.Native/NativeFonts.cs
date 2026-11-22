using System.Drawing.Text;

namespace EzEizo.Core;

internal static class NativeFonts
{
    private static readonly PrivateFontCollection Collection = new();
    private static readonly FontFamily Family = Load();

    private static FontFamily Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Heebo.ttf");
        if (!File.Exists(path))
            throw new FileNotFoundException("גופן הממשק Heebo חסר מחבילת ההתקנה.", path);
        Collection.AddFontFile(path);
        // Variable TTF files can expose several named instances to GDI+.
        return Collection.Families.FirstOrDefault(f => f.Name.Contains("Heebo", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("גופן Heebo לא נטען.");
    }

    internal static Font Create(float points, FontStyle style = FontStyle.Regular)
    {
        return new Font(Family, points, Family.IsStyleAvailable(style) ? style : FontStyle.Regular, GraphicsUnit.Point);
    }
}
