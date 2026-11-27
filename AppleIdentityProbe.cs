using System.Xml.Linq;

namespace SIM800L_Flasher;

/// <summary>
/// خواندن IMEI آیفون از مسیر pairing (همان lockdownd که Finder/iTunes استفاده می‌کند).
///
/// چرا این کار می‌کند: IMEI روی iOS یک شناسه «فقط خواندنی» است که lockdownd آن را در
/// دامنه global به ابزار مجاز می‌دهد؛ برخلاف اندروید که از API 29 به بعد آن را با
/// READ_PRIVILEGED_PHONE_STATE می‌بندد. مفهوم از اپ روی خود گوشی پرسیده نمی‌شود، بلکه
/// از سرویس جفت‌سازی روی کابل — به همین دلیل Trust کافی است و جیلبریک لازم نیست.
///
/// کلیدها: InternationalMobileEquipmentIdentity، ...2 (دو سیم/eSIM)،
/// EmbeddedIdentityDocument (EID)، IntegratedCircuitCardIdentity (ICCID).
/// </summary>
internal sealed record AppleIdentityResult(
    string Udid,
    string Model,
    string IosVersion,
    string Imei1,
    string Imei2,
    string Eid,
    string Iccid,
    string Serial,
    string Baseband,
    string ActivationState,
    IReadOnlyList<string> Trail,
    string Verdict)
{
    public bool FoundAny => Imei1.Length == 15 || Imei2.Length == 15;

    /// <summary>
    /// بیس‌باند غایب یعنی سخت‌افزار مودم شناسایی نشده (برد تعویض‌شده یا CP آسیب‌دیده)؛
    /// در این حالت هیچ IMEI‌ای وجود ندارد که نوشته یا خوانده شود.
    /// </summary>
    public bool BasebandMissing =>
        Baseband.Length == 0 ||
        Baseband.Contains("unknown", StringComparison.OrdinalIgnoreCase) ||
        Baseband.Contains("not", StringComparison.OrdinalIgnoreCase) ||
        Baseband == "0";
}

internal static class AppleIdentityProbe
{
    /// <summary>کلیدهای lockdownd که به IMEI مربوط‌اند، به ترتیب اهمیت.</summary>
    public static readonly (string Key, string Label)[] IdentityKeys =
    [
        ("InternationalMobileEquipmentIdentity", "IMEI 1"),
        ("InternationalMobileEquipmentIdentity2", "IMEI 2"),
        ("EmbeddedIdentityDocument", "EID"),
        ("IntegratedCircuitCardIdentity", "ICCID"),
        ("MobileEquipmentIdentifier", "MEID"),
        ("SerialNumber", "سریال"),
        ("ProductType", "مدل"),
        ("ProductVersion", "نسخه iOS"),
        ("BasebandVersion", "بیس‌بند"),
        ("ActivationState", "وضعیت فعال‌سازی")
    ];

    public static async Task<AppleIdentityResult> ReadAsync(
        string ideviceInfoPath,
        Func<string, IReadOnlyList<string>, CancellationToken, Task<MobileToolResult>> run,
        string? udid,
        CancellationToken token = default)
    {
        var trail = new List<string>();
        string Imei1 = "", Imei2 = "", Eid = "", Iccid = "", Serial = "", Model = "", Version = "";
        var baseband = "";
        var activation = "";
        var udids = udid is null ? await ListDevicesAsync(run, token) : [udid];

        if (udids.Count == 0)
            return new("-", "", "", "", "", "", "", "", "", "", "", trail,
                "دستگاه اپل در USB دیده نشد. کابل دیتا، باز بودن قفل صفحه و تأیید Trust لازم است.");
        if (udids.Count > 1 && udid is null)
            return new(udids[0], "", "", "", "", "", "", "", "", "", "", trail,
                "چند دستگاه اپل وصل است. در فهرست یکی را انتخاب کن تا IMEI همان خوانده شود.");

        var target = udids[0];
        foreach (var (key, label) in IdentityKeys)
        {
            token.ThrowIfCancellationRequested();
            var args = udid is null
                ? new[] { "-k", key }
                : ["-u", target, "-k", key];
            var result = await run(ideviceInfoPath, args, token);
            var value = result.ExitCode == 0 ? CleanValue(result.Output) : string.Empty;
            trail.Add($"{label} → {(value.Length > 0 ? value : DescribeFailure(result))}");
            switch (key)
            {
                case "InternationalMobileEquipmentIdentity": Imei1 = value; break;
                case "InternationalMobileEquipmentIdentity2": Imei2 = value; break;
                case "EmbeddedIdentityDocument": Eid = value; break;
                case "IntegratedCircuitCardIdentity": Iccid = value; break;
                case "SerialNumber": Serial = value; break;
                case "ProductType": Model = value; break;
                case "ProductVersion": Version = value; break;
                case "BasebandVersion": baseband = value; break;
                case "ActivationState": activation = value; break;
            }
        }

        var verdict = Imei1.Length == 15
            ? "IMEI از lockdownd خوانده شد (بدون جیلبریک، با Trust)."
            : trail.Any(line => line.Contains("Pairing", StringComparison.OrdinalIgnoreCase) ||
                                line.Contains("password", StringComparison.OrdinalIgnoreCase))
                ? "دستگاه Trust نشده. قفل صفحه را باز کن و Trust This Computer را بزن، بعد دوباره بخوان."
                : "lockdownd مقدار IMEI را نداد. اگر آیفون در حالت Diagnostics است، صفحه Information سریال/IMEI را نشان می‌دهد؛ همان را دستی وارد کن.";

        return new(target, Model, Version, Imei1, Imei2.Length == 15 ? Imei2 : string.Empty,
            Eid, Iccid, Serial, baseband, activation, trail, verdict);
    }

    private static async Task<List<string>> ListDevicesAsync(
        Func<string, IReadOnlyList<string>, CancellationToken, Task<MobileToolResult>> run, CancellationToken token)
    {
        try
        {
            var result = await run("idevice_id.exe", ["-l"], token);
            return result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(line => line.Length >= 24 && line.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or System.ComponentModel.Win32Exception)
        {
            return [];
        }
    }

    internal static string CleanValue(string output) =>
        output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(line => !line.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase))?.Trim().Trim('"') ?? string.Empty;

    private static string DescribeFailure(MobileToolResult result) =>
        (result.Error + " " + result.Output).Contains("Pair", StringComparison.OrdinalIgnoreCase) ? "نیازمند Trust" :
        result.ExitCode == 0 ? "(خالی)" : $"خطا (کد {result.ExitCode})";

    /// <summary>پاسخ plist کامل ideviceinfo را به دیکشنری تبدیل می‌کند (مسیر جایگزین وقتی -k جواب ندهد).</summary>
    internal static Dictionary<string, string> ParsePlist(string output)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            var doc = XDocument.Parse(output);
            var pairs = doc.Root?.Element("dict")?.Elements().ToArray() ?? [];
            for (var i = 0; i + 1 < pairs.Length; i += 2)
                if (pairs[i].Name == "key" && pairs[i + 1].Name is { LocalName: "string" or "integer" })
                    values[pairs[i].Value] = pairs[i + 1].Value.Trim();
        }
        catch (System.Xml.XmlException) { }
        return values;
    }
}
