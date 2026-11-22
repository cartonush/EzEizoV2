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
        return Collection.Families.Single();
    }

    internal static Font Create(float points, FontStyle style = FontStyle.Regular)
    {
        return new Font(Family, points, style, GraphicsUnit.Point);
    }
}
