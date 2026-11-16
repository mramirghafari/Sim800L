using System.Reflection;

namespace SIM800L_Flasher;

internal static class BrandAssets
{
    public static Image IconImage { get; } = LoadImage("SIM800L_Flasher.Assets.XAnten_Icon_Transparent.png");
    public static Image WordmarkImage { get; } = LoadImage("SIM800L_Flasher.Assets.XAnten_Wordmark_Transparent.png");
    public static Icon WindowIcon { get; } = LoadIcon("SIM800L_Flasher.Assets.XAnten.ico");

    private static Image LoadImage(string resourceName)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"منبع تصویری پیدا نشد: {resourceName}");
        using var source = Image.FromStream(stream);
        return new Bitmap(source);
    }

    private static Icon LoadIcon(string resourceName)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"آیکون برنامه پیدا نشد: {resourceName}");
        using var source = new Icon(stream);
        return (Icon)source.Clone();
    }
}
