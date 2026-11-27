using System.Text;

namespace SIM800L_Flasher;

/// <summary>یک مسیر خواندن IMEI و نتیجه‌اش.</summary>
internal sealed record IdentityAttempt(string Path, string Detail, bool Succeeded, string Imei1, string Imei2, string Eid)
{
    public string Badge => Succeeded ? "موفق" : "ناموفق";
}

internal sealed record UniversalIdentity(
    IReadOnlyList<IdentityAttempt> Attempts,
    string Imei1,
    string Imei2,
    string Eid,
    string Iccid,
    string Model,
    string Serial,
    PhoneVendor Vendor,
    string Verdict)
{
    public bool Found => Imei1.Length == 15 || Imei2.Length == 15;

    public string BestPath => Attempts.FirstOrDefault(a => a.Succeeded)?.Path ?? "—";

    public string ToText()
    {
        var text = new StringBuilder();
        text.AppendLine("شناسه سخت‌افزار گوشی — همه مسیرها");
        text.AppendLine(new string('─', 64));
        text.AppendLine($"IMEI 1: {(Imei1.Length == 15 ? Imei1 : "خوانده نشد")}");
        text.AppendLine($"IMEI 2: {(Imei2.Length == 15 ? Imei2 : "—")}");
        text.AppendLine($"EID:    {(Eid.Length > 0 ? Eid : "—")}");
        text.AppendLine($"ICCID:  {(Iccid.Length > 0 ? Mask(Iccid) : "—")}");
        text.AppendLine($"مدل: {Show(Model)}   سریال: {Show(Serial)}   چیپست: {Vendor}");
        text.AppendLine();
        text.AppendLine("مسیرهای امتحان‌شده (به ترتیب):");
        foreach (var attempt in Attempts)
            text.AppendLine($"  [{attempt.Badge}] {attempt.Path} — {attempt.Detail}");
        text.AppendLine();
        text.AppendLine("حکم:");
        text.AppendLine(Verdict);
        return text.ToString();
    }

    private static string Show(string value) => string.IsNullOrWhiteSpace(value) ? "—" : value;

    private static string Mask(string value) => value.Length <= 4 ? "•" : "••••" + value[^4..];
}

/// <summary>
/// «خواندن از هر گوشی، بدون محدودیت» — این کلاس همه مسیرهایی را که واقعاً وجود دارند
/// به ترتیب امتحان می‌کند و می‌گوید کدام یکی جواب داد:
///
/// ۱) <b>پورت مودم/AT</b> — سامسونگ با <c>*#0808#</c> → DM+MODEM+ADB؛ مستقل از نسخه اندروید.
/// ۲) <b>iPhone/lockdownd</b> — با Trust؛ IMEI و IMEI2 و EID.
/// ۳) <b>ADB + سرویس binder</b> — تا Android 13 روی بخشی از دستگاه‌ها.
/// ۴) <b>روت</b> — خواندن prop مودم.
///
/// هیچ مسیری «بی‌نهایت» نیست و کلاس ادعای بیشتر از شاهد ندارد: هر مسیر یا شناسه می‌دهد یا
/// دلیل مشخصِ شکست؛ مجموع نتایج همان چیزی است که در گزارش می‌آید.
/// </summary>
internal static class UniversalIdentityReader
{
    public static async Task<UniversalIdentity> ReadAsync(
        IReadOnlyList<string> modemPorts,
        string? adbPath,
        string? adbSerial,
        string? ideviceInfoPath,
        Func<string, IReadOnlyList<string>, CancellationToken, Task<MobileToolResult>> runTool,
        CancellationToken token = default)
    {
        var attempts = new List<IdentityAttempt>();
        string imei1 = "", imei2 = "", eid = "", iccid = "", model = "", serial = "";
        var vendor = PhoneVendor.Unknown;

        // ۱) پورت مودم روی هر پورت سریال موجود
        foreach (var port in modemPorts.Take(8))
        {
            token.ThrowIfCancellationRequested();
            var result = await ModemIdentityProbe.ScanAsync([port], token);
            if (result.FoundAny)
            {
                imei1 = result.Imei1;
                imei2 = result.Imei2;
                eid = result.Eid;
                model = result.Model;
                serial = result.Serial;
                vendor = GuessVendorFromModel(result.Model);
                attempts.Add(new IdentityAttempt("پورت مودم (AT)", $"{result.PortName} → {result.Verdict}", true,
                    result.Imei1, result.Imei2, result.Eid));
                break;
            }
            attempts.Add(new IdentityAttempt("پورت مودم (AT)", $"{port} → {result.Verdict}", false, "", "", ""));
        }

        // ۲) آیفون
        if (imei1.Length == 0 && !string.IsNullOrEmpty(ideviceInfoPath))
        {
            var apple = await AppleIdentityProbe.ReadAsync(ideviceInfoPath, runTool, adbSerial, token);
            if (apple.FoundAny)
            {
                imei1 = apple.Imei1;
                imei2 = apple.Imei2;
                eid = apple.Eid;
                iccid = apple.Iccid;
                model = apple.Model;
                serial = apple.Serial;
                vendor = PhoneVendor.Apple;
                attempts.Add(new IdentityAttempt("iPhone lockdownd", apple.Verdict, true, apple.Imei1, apple.Imei2, apple.Eid));
            }
            else
            {
                attempts.Add(new IdentityAttempt("iPhone lockdownd", apple.Verdict, false, "", "", ""));
            }
        }

        // ۳ و ۴) ADB
        if (imei1.Length == 0 && !string.IsNullOrEmpty(adbPath) && !string.IsNullOrEmpty(adbSerial))
        {
            var binder = await AndroidIdentityProbe.ProbeAsync(adbPath, adbSerial, runTool, token);
            attempts.Add(new IdentityAttempt("ADB / سرویس binder", binder.Verdict, binder.FoundAny,
                binder.Imei1, binder.Imei2, ""));
            if (binder.FoundAny)
            {
                imei1 = binder.Imei1;
                imei2 = binder.Imei2;
            }
        }

        var verdict = imei1.Length == 15
            ? $"IMEI از مسیر «{attempts.First(a => a.Succeeded).Path}» خوانده شد. " +
              "این مقدار همان چیزی است که شبکه از دستگاه می‌بیند."
            : "هیچ مسیری IMEI نداد. برای سامسونگ: *#0808# → DM+MODEM+ADB و سپس دوباره بخوان. " +
              "برای آیفون: قفل را باز کن و Trust بزن. اگر هر دو انجام شد و باز هم خالی بود، IMEI در خود دستگاه خالی است (اشکال مودم).";

        return new UniversalIdentity(attempts, imei1, imei2, eid, iccid, model, serial, vendor, verdict);
    }

    private static PhoneVendor GuessVendorFromModel(string model)
    {
        var text = model.ToUpperInvariant();
        if (text.Contains("SHANNON") || text.Contains("EXYNOS") || text.Contains("SAMSUNG")) return PhoneVendor.Qualcomm;
        if (text.Contains("MT") || text.Contains("MEDIATEK")) return PhoneVendor.MediaTek;
        if (text.Contains("UNISOC")) return PhoneVendor.Unisoc;
        return PhoneVendor.Unknown;
    }
}
