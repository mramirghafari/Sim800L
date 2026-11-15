using System.Drawing.Text;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace SIM800L_Flasher;

internal static class AppFonts
{
    private const string GdiFamilyName = "Yekan Bakh XAnten";
    private static readonly string[] ResourceNames =
    [
        "SIM800L_Flasher.Assets.YekanBakh-Light.ttf",
        "SIM800L_Flasher.Assets.YekanBakh-SemiBold.ttf"
    ];
    private static readonly PrivateFontCollection Collection = new();
    private static readonly List<IntPtr> FontMemory = [];
    private static readonly List<IntPtr> GdiFontHandles = [];
    private static readonly List<IntPtr> CreatedHfonts = [];
    private static readonly List<string> GdiFontFiles = [];
    private static readonly FontFamily? EmbeddedFamily;

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr AddFontMemResourceEx(
        IntPtr fontData,
        uint fontDataSize,
        IntPtr reserved,
        ref uint numberOfFonts);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool RemoveFontMemResourceEx(IntPtr handle);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int AddFontResourceEx(string fileName, uint flags, IntPtr reserved);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool RemoveFontResourceEx(string fileName, uint flags, IntPtr reserved);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr obj);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFont(
        int height,
        int width,
        int escapement,
        int orientation,
        int weight,
        uint italic,
        uint underline,
        uint strikeOut,
        uint charSet,
        uint outputPrecision,
        uint clipPrecision,
        uint quality,
        uint pitchAndFamily,
        string faceName);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetTextFace(IntPtr hdc, int count, StringBuilder faceName);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    static AppFonts()
    {
        foreach (var resourceName in ResourceNames)
        {
            try
            {
                using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
                if (stream is null) continue;

                using var buffer = new MemoryStream();
                stream.CopyTo(buffer);
                var bytes = buffer.ToArray();
                var memory = Marshal.AllocHGlobal(bytes.Length);
                Marshal.Copy(bytes, 0, memory, bytes.Length);
                Collection.AddMemoryFont(memory, bytes.Length);

                var fontDirectory = Path.Combine(Path.GetTempPath(), "XAnten", "Fonts");
                Directory.CreateDirectory(fontDirectory);
                var fontHash = Convert.ToHexString(SHA256.HashData(bytes))[..12];
                var fontPath = Path.Combine(
                    fontDirectory,
                    $"{Path.GetFileNameWithoutExtension(resourceName)}-{fontHash}.ttf");
                if (!File.Exists(fontPath)) File.WriteAllBytes(fontPath, bytes);
                const uint privateFont = 0x10; // FR_PRIVATE: فقط برای همین پردازش
                if (AddFontResourceEx(fontPath, privateFont, IntPtr.Zero) > 0)
                {
                    GdiFontFiles.Add(fontPath);
                }
                FontMemory.Add(memory);
            }
            catch
            {
                // اگر یک وزن بارگذاری نشد، وزن دیگر یا Segoe UI به‌عنوان fallback استفاده می‌شود.
            }
        }

        EmbeddedFamily = Collection.Families.FirstOrDefault();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => ReleaseFontResources();
    }

    public static Font Ui(float size, FontStyle style = FontStyle.Regular)
    {
        if (EmbeddedFamily is not null)
        {
            var supportedStyle = EmbeddedFamily.IsStyleAvailable(style) ? style : FontStyle.Regular;
            return new Font(EmbeddedFamily, size, supportedStyle, GraphicsUnit.Point);
        }

        return new Font("Segoe UI", size, style, GraphicsUnit.Point);
    }

    public static string ActiveFamilyName => EmbeddedFamily?.Name ?? "Segoe UI";

    public static bool IsRegisteredWithGdi => GdiFontFiles.Count == ResourceNames.Length;

    /// <summary>
    /// نام فونتی را برمی‌گرداند که GDI واقعاً برای یک کنترل WinForms انتخاب می‌کند؛
    /// این تست fallback خاموش ویندوز به Segoe UI را هم آشکار می‌کند.
    /// </summary>
    public static string GetActualGdiFaceName(Font font)
    {
        var hfont = CreateNativeHfont(font.Size, font.Style);
        if (hfont == IntPtr.Zero) return string.Empty;

        var hdc = CreateCompatibleDC(IntPtr.Zero);
        if (hdc == IntPtr.Zero)
        {
            DeleteObject(hfont);
            return string.Empty;
        }

        var previous = IntPtr.Zero;
        try
        {
            previous = SelectObject(hdc, hfont);
            var faceName = new StringBuilder(128);
            return GetTextFace(hdc, faceName.Capacity, faceName) > 0
                ? faceName.ToString()
                : string.Empty;
        }
        finally
        {
            if (previous != IntPtr.Zero) SelectObject(hdc, previous);
            DeleteObject(hfont);
            DeleteDC(hdc);
        }
    }

    /// <summary>
    /// کنترل‌های native مثل TextBox و ComboBox فونت PrivateFontCollection را به HFONT
    /// تبدیل نمی‌کنند. با WM_SETFONT همان face ثبت‌شده مستقیماً به handle آن‌ها داده می‌شود.
    /// </summary>
    public static void ApplyNativeFonts(Control root)
    {
        Apply(root);

        static void Apply(Control control)
        {
            void SetNativeFont()
            {
                var hfont = CreateNativeHfont(control.Font.Size, control.Font.Style);
                if (hfont == IntPtr.Zero) return;

                CreatedHfonts.Add(hfont);
                const uint setFont = 0x0030; // WM_SETFONT
                SendMessage(control.Handle, setFont, hfont, new IntPtr(1));
            }

            control.HandleCreated += (_, _) => SetNativeFont();
            if (control.IsHandleCreated) SetNativeFont();

            control.ControlAdded += (_, args) =>
            {
                if (args.Control is not null) Apply(args.Control);
            };
            foreach (Control child in control.Controls) Apply(child);
        }
    }

    private static IntPtr CreateNativeHfont(float size, FontStyle style)
    {
        if (EmbeddedFamily is null) return IntPtr.Zero;

        var height = -(int)Math.Round(size * 96f / 72f);
        return CreateFont(
            height,
            0,
            0,
            0,
            style.HasFlag(FontStyle.Bold) ? 700 : 350,
            style.HasFlag(FontStyle.Italic) ? 1u : 0u,
            style.HasFlag(FontStyle.Underline) ? 1u : 0u,
            style.HasFlag(FontStyle.Strikeout) ? 1u : 0u,
            1, // DEFAULT_CHARSET
            4, // OUT_TT_PRECIS
            0, // CLIP_DEFAULT_PRECIS
            5, // CLEARTYPE_QUALITY
            0, // DEFAULT_PITCH | FF_DONTCARE
            GdiFamilyName);
    }

    private static void ReleaseFontResources()
    {
        foreach (var hfont in CreatedHfonts)
        {
            DeleteObject(hfont);
        }

        const uint privateFont = 0x10;
        foreach (var file in GdiFontFiles)
        {
            RemoveFontResourceEx(file, privateFont, IntPtr.Zero);
        }

        foreach (var handle in GdiFontHandles)
        {
            RemoveFontMemResourceEx(handle);
        }

        foreach (var memory in FontMemory)
        {
            Marshal.FreeHGlobal(memory);
        }
    }
}
