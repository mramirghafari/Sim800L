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
    public bool AnyWritePath => EgmrWritable || SimeiSupported;

    public string Recommendation => RecommendedStrategy is null
        ? "هیچ مسیر نوشتن AT روی این firmware پیدا نشد؛ گزینه باقی‌مانده فلش بسته اختصاصی همین revision است."
        : $"پیشنهاد: «{RecommendedStrategy.Title}» را انتخاب و اجرا کنید.";
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
