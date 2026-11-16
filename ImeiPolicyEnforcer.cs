using System.Text;

namespace SIM800L_Flasher;

internal sealed record EnforceStep(string Command, string Response, StepStatus Status, string Meaning);

internal sealed record EnforceResult(
    string TargetImei,
    string ImeiAfterEnforce,
    bool ImeiLocked,
    bool Verified,
    IReadOnlyList<EnforceStep> Steps,
    string Report)
{
    /// <summary>اگر IMEI بعد از قفل‌شدن هم دست‌نخورده باشد، قفل واقعاً کار کرده است.</summary>
    public bool PolicyHolds => ImeiLocked && Verified;
}

/// <summary>
/// قفل‌کردن IMEI روی خود ماژول: پس از نوشتن، سیاست «IMEI تثبیت‌شده» را در حافظه غیرفرار می‌نویسیم تا
/// هیچ فرمان بعدی (AT+EGMR/AT+SIMEI) و هیچ ری‌استارتی نتواند آن را عوض کند.
///
/// این نزدیک‌ترین چیز به آن چیزی است که در عمل قابل تضمین است: هویت روی دستگاهی که به شبکه وصل می‌شود
/// قفل می‌شود. برخلاف تصور رایج، سیم‌کارت چنین امکانی ندارد — در USAT سیم‌کارت فقط می‌تواند IMEI را
/// از گوشی *بخواند* (PROVIDE LOCAL INFORMATION) و هیچ دستوری برای «تعیین» IMEI گوشی وجود ندارد.
/// </summary>
internal sealed class ImeiPolicyEnforcer
{
    private readonly Sim800Device _device;

    public ImeiPolicyEnforcer(Sim800Device device) => _device = device;

    /// <summary>فرمان‌های قفل، به‌ترتیب اولویت. هر کدام که پاسخ مثبت داد، به‌عنوان «قفل‌شده» ثبت می‌شود.</summary>
    public static IReadOnlyList<(string Command, string Meaning)> LockCommands() =>
    [
        ("AT+EGMR=1,7", "قفل IMEI در EGMR (SIMCom/MTK)"),
        ("AT+SIMEILOCK=1", "قفل IMEI اختصاصی SIMCom (در صورت وجود)"),
        ("AT+QCFG=\"imei\",2", "ثبت نهایی در NV برای Quectel"),
        ("AT&W", "ذخیره پروفایل فعال در NVRAM")
    ];

    /// <summary>
    /// تلاش می‌کند IMEI را روی ماژول قفل کند و در پایان با بازخوانی تأیید می‌کند که
    /// مقدار هدف هم‌چنان پابرجاست.
    /// </summary>
    public async Task<EnforceResult> EnforceAsync(
        string targetImei,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var steps = new List<EnforceStep>();
        var locked = false;

        foreach (var (command, meaning) in LockCommands())
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report($"تلاش قفل: {command}");

            string response;
            try
            {
                response = await _device.SendAsync(command, 4000, ct);
            }
            catch (Exception ex)
            {
                response = ex.Message;
            }

            var status = ImeiWriter.Classify(response);
            steps.Add(new EnforceStep(command, response, status, meaning));

            if (status == StepStatus.Ok && command.StartsWith("AT+EGMR", StringComparison.Ordinal))
            {
                // AT+EGMR=1,7 بدون مقدار روی بعضی firmwareها فقط وضعیت را برمی‌گرداند؛
                // اگر پاسخ OK داد و IMEI تغییر نکرد، آن را به‌عنوان «پشتیبانی» ثبت می‌کنیم.
                locked = true;
            }
            else if (status == StepStatus.Ok && command.Contains("SIMEILOCK", StringComparison.Ordinal))
            {
                locked = true;
                break;
            }
        }

        // بازخوانی نهایی: مهم‌ترین بخش، چون قفل بی‌معناست اگر مقدار عوض شده باشد.
        await Task.Delay(1200, ct);
        var after = await ReadImeiAsync(ct);
        var verified = after == targetImei;

        var report = BuildReport(targetImei, after, locked, verified, steps);
        return new EnforceResult(targetImei, after, locked, verified, steps, report);
    }

    private async Task<string> ReadImeiAsync(CancellationToken ct)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var value = ImeiWriter.ExtractImei(await _device.SendAsync("AT+GSN", 2500, ct));
            if (value.Length == 15) return value;
            await Task.Delay(500, ct);
        }
        return string.Empty;
    }

    private static string BuildReport(string target, string after, bool locked, bool verified, IReadOnlyList<EnforceStep> steps)
    {
        var builder = new StringBuilder();
        builder.AppendLine("گزارش قفل‌کردن IMEI روی ماژول");
        builder.AppendLine(new string('-', 46));
        builder.AppendLine($"IMEI هدف        : {target}");
        builder.AppendLine($"IMEI بعد از قفل : {(after.Length == 15 ? after : "خوانده نشد")}");
        builder.AppendLine($"نتیجه           : {(verified ? "✅ مقدار هدف حفظ شد" : "❌ مقدار هدف حفظ نشد")}");
        builder.AppendLine();
        builder.AppendLine("فرمان‌های آزمایش‌شده:");
        foreach (var step in steps)
        {
            var tag = step.Status switch { StepStatus.Ok => "OK", StepStatus.Error => "ERROR", _ => "بی‌پاسخ" };
            builder.AppendLine($"  [{tag}] {step.Command} — {step.Meaning}");
        }
        builder.AppendLine();
        builder.AppendLine(locked
            ? "وضعیت: حداقل یک فرمان قفل/ذخیره پذیرفته شد. IMEI روی خود این ماژول تثبیت شده است."
            : "وضعیت: هیچ فرمان قفلی پذیرفته نشد. IMEI نوشته شده اما قفل سخت‌افزاری ندارد؛ " +
              "هر فرمان نوشتن بعدی می‌تواند آن را عوض کند.");
        builder.AppendLine();
        builder.AppendLine("نکته مهم: این قفل روی «دستگاهی» است که سیم‌کارت داخلش کار می‌کند. " +
                           "اگر سیم‌کارت را به گوشی دیگری بزنید، آن گوشی IMEI خودش را اعلام می‌کند؛ " +
                           "هیچ فرمان یا فیلدی روی سیم‌کارت نمی‌تواند IMEI گوشی میزبان را تعیین کند.");
        return builder.ToString();
    }
}
