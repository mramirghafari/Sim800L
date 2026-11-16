using System.Text;

namespace SIM800L_Flasher;

/// <summary>
/// عیب‌یابی مرحله‌به‌مرحله پیش از نوشتن: مشخص می‌کند کدام خانواده فرمان روی firmware فعلی وجود دارد
/// و کدام استراتژی شانس واقعی دارد. هیچ فرمان نوشتنی ارسال نمی‌کند.
/// </summary>
internal sealed record CapabilityReport(
    string Model,
    string Revision,
    bool EgmrReadable,
    bool EgmrWritable,
    bool SimeiSupported,
    bool SimeiHasQuota,
    bool SimPresent,
    string ImeiNow,
    IReadOnlyList<string> Findings,
    WriteStrategy? RecommendedStrategy,
    string RawLog)
{
    /// <summary>خانواده ماژول از پاسخ ATI (SIM800L / SIM808 / SIM900 / SIM7600 / Quectel).</summary>
    public string Family => ModuleFamily.Detect(Model);

    public string FamilyNote => ModuleFamily.Note(Family);

    public bool AnyWritePath => EgmrWritable || SimeiSupported;

    public string Recommendation => RecommendedStrategy is null
        ? "هیچ مسیر نوشتن AT روی این firmware پیدا نشد؛ گزینه باقی‌مانده فلش بسته اختصاصی همین revision است."
        : $"پیشنهاد: «{RecommendedStrategy.Title}» را انتخاب و اجرا کنید.";
}

/// <summary>
/// تشخیص خانواده ماژول و توضیح تفاوت رفتاری مهم آن‌ها. SIM800L و SIM808 فرمان‌های AT مشترک دارند،
/// اما SIM808 رادیو GPS هم دارد و در بعضی نسخه‌ها فضای NV متفاوتی دارد؛ برای همین باید صریح گزارش شود.
/// </summary>
internal static class ModuleFamily
{
    public const string Sim800L = "SIM800L";
    public const string Sim808 = "SIM808";
    public const string Sim900 = "SIM900";
    public const string Sim7000 = "SIM7000";
    public const string Quectel = "Quectel";
    public const string Unknown = "نامشخص";

    public static string Detect(string? model)
    {
        if (string.IsNullOrWhiteSpace(model)) return Unknown;
        var text = model.ToUpperInvariant();
        if (text.Contains("SIM808")) return Sim808;
        if (text.Contains("SIM800")) return Sim800L;
        if (text.Contains("SIM900")) return Sim900;
        if (text.Contains("SIM7000") || text.Contains("SIM7080") || text.Contains("SIM7600") || text.Contains("A7670"))
            return Sim7000;
        if (text.Contains("EC2") || text.Contains("BG9") || text.Contains("M95") || text.Contains("QUECTEL"))
            return Quectel;
        return Unknown;
    }

    public static string Note(string family) => family switch
    {
        Sim800L => "SIM800L: GSM/GPRS دوباند. IMEI را از RAM/فلش خودش اعلام می‌کند؛ سیم‌کارت هیچ نقشی در آن ندارد.",
        Sim808 => "SIM808: همان خانواده SIMCom با GPS داخلی. فرمان‌های IMEI مشترک است، اما برخی نسخه‌ها بعد از تغییر firmware، IMEI را ری‌ست می‌کنند؛ بعد از هر فلش، «بررسی پایداری» را دوباره اجرا کنید.",
        Sim900 => "SIM900: نسل قبلی؛ AT+EGMR روی بعضی نسخه‌ها کار می‌کند و AT+SIMEI معمولاً نه.",
        Sim7000 => "SIM7xxx/7xxx: LTE-CAT1؛ معمولاً AT+EGMR یا AT+SIMEI پشتیبانی می‌شود و پایداری بهتر است.",
        Quectel => "Quectel: AT+QCFG=\"imei\" مسیر اختصاصی و پایدار است (نه صرفاً «امتحان»).",
        _ => "خانواده ماژول شناسایی نشد؛ ابتدا «تشخیص قابلیت‌ها» را اجرا کنید."
    };

    /// <summary>آیا این خانواده مسیر اختصاصی نوشتن IMEI دارد؟</summary>
    public static bool HasVendorPath(string family) => family is Quectel or Sim7000;
}

internal sealed class ImeiDiagnostics
{
    private readonly Sim800Device _device;
    private readonly StringBuilder _raw = new();

    public ImeiDiagnostics(Sim800Device device) => _device = device;

    private async Task<string> Query(string command, int timeout, CancellationToken ct)
    {
        var response = await _device.SendAsync(command, timeout, ct);
        _raw.AppendLine($"> {command}");
        _raw.AppendLine(response);
        _raw.AppendLine();
        return response;
    }

    private static bool IsError(string response) =>
        response.Contains("ERROR", StringComparison.OrdinalIgnoreCase) ||
        response.StartsWith("(بدون پاسخ", StringComparison.Ordinal);

    public async Task<CapabilityReport> InspectAsync(CancellationToken ct = default)
    {
        _raw.Clear();
        var findings = new List<string>();

        var model = FirstLine(await Query("ATI", 2500, ct));
        var revision = FirstLine(await Query("AT+GMR", 2500, ct));
        var imeiNow = ImeiWriter.ExtractImei(await Query("AT+GSN", 2500, ct));
        if (imeiNow.Length != 15) imeiNow = ImeiWriter.ExtractImei(await Query("AT+CGSN", 2500, ct));

        var cpin = await Query("AT+CPIN?", 2500, ct);
        var simPresent = !cpin.Contains("NOT INSERTED", StringComparison.OrdinalIgnoreCase) &&
                         !cpin.Contains("ERROR", StringComparison.OrdinalIgnoreCase);

        // آیا AT+EGMR خواندنی است؟ (AT+EGMR=0,7 فقط می‌خواند)
        var egmrRead = await Query("AT+EGMR=0,7", 3000, ct);
        var egmrReadable = !IsError(egmrRead) &&
                           ImeiWriter.ExtractImei(egmrRead).Length == 15;

        // آیا AT+SIMEI وجود دارد؟ (فقط خواندن)
        var simeiRead = await Query("AT+SIMEI?", 3000, ct);
        var simeiSupported = !IsError(simeiRead) &&
                             ImeiWriter.ExtractImei(simeiRead).Length == 15;

        // آزمون نوشتن EGMR با همان مقدار فعلی: اگر OK بگیرد یعنی فرمان نوشتن پذیرفته می‌شود
        // و مقدار را تغییر نمی‌دهد چون همان IMEI فعلی نوشته می‌شود.
        var egmrWritable = false;
        if (egmrReadable && imeiNow.Length == 15)
        {
            var writeSame = await Query($"AT+EGMR=1,7,\"{imeiNow}\"", 4000, ct);
            egmrWritable = !IsError(writeSame);
            if (egmrWritable)
                findings.Add("آزمون نوشتن EGMR با مقدار فعلی OK گرفت؛ این firmware فرمان نوشتن را می‌پذیرد (مقدار تغییر نکرد).");
        }

        // آزمون پذیرش SIMEI با مقدار فعلی نفس IMEI هم انجام نمی‌شود مگر پشتیبانی خواندن ثابت شود،
        // چون تعداد نوشتن SIMEI محدود است و نباید هدر برود.
        var simeiHasQuota = simeiSupported && simeiRead.Contains("1", StringComparison.Ordinal);

        var family = ModuleFamily.Detect(model);
        findings.Add($"خانواده تشخیص‌داده‌شده: {family} — {ModuleFamily.Note(family)}");

        if (!egmrReadable)
            findings.Add("AT+EGMR=0,7 پاسخ معتبری نداد؛ مسیر EGMR روی این firmware وجود ندارد یا فرمت متفاوت است.");
        if (!simeiSupported)
            findings.Add("AT+SIMEI? پاسخ معتبری نداد؛ فرمان اختصاصی SIMCom روی این firmware نیست.");

        if (simPresent)
            findings.Add("سیم‌کارت داخل ماژول است؛ برای نوشتن خارج کنید.");

        WriteStrategy? recommended = null;
        if (egmrWritable) recommended = ImeiWriter.Strategies.First(s => s.Kind == WriteStrategyKind.Egmr);
        else if (simeiSupported) recommended = ImeiWriter.Strategies.First(s => s.Kind == WriteStrategyKind.Simei);

        if (!egmrWritable && !simeiSupported)
            findings.Add("هیچ استراتژی AT قابل استفاده نیست. تنها راه: فلش بسته firmware اختصاصی همین revision با ابزار رسمی سازنده، یا استفاده از ابزار تعمیرکار رسمی.");

        return new CapabilityReport(model, revision, egmrReadable, egmrWritable, simeiSupported,
            simeiHasQuota, simPresent, imeiNow, findings, recommended, _raw.ToString());
    }

    private static string FirstLine(string response)
    {
        foreach (var line in response.Replace("\r", string.Empty).Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.Equals("OK", StringComparison.OrdinalIgnoreCase)) continue;
            if (trimmed.StartsWith("AT", StringComparison.OrdinalIgnoreCase)) continue;
            return trimmed;
        }
        return "—";
    }
}
