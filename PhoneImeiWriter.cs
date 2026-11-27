using System.Text;

namespace SIM800L_Flasher;

/// <summary>نتیجه یک تلاش نوشتن روی گوشی، با شاهد بازخوانی.</summary>
internal enum PhoneWriteOutcome
{
    /// <summary>نوشته شد و بازخوانی هم همان مقدار را داد.</summary>
    Verified,

    /// <summary>فرمان‌ها قبول شدند ولی مقدار عوض نشد (تله رایج ابزارها).</summary>
    AcceptedButNotPersisted,

    /// <summary>ابزار/پورت لازم در دسترس نیست.</summary>
    ToolMissing,

    /// <summary>دستگاه بعد از عملیات پاسخ نداد.</summary>
    DeviceUnresponsive,

    /// <summary>این مسیر روی این دستگاه بسته است (secure boot / پلتفرم پشتیبانی‌نشده).</summary>
    Blocked,

    Failed
}

internal sealed record PhoneWriteStep(string Command, string Response, bool Succeeded);

internal sealed record PhoneWriteResult(
    string Chipset,
    string TargetImei,
    string ImeiBefore,
    string ImeiAfter,
    PhoneWriteOutcome Outcome,
    IReadOnlyList<PhoneWriteStep> Steps,
    string Verdict,
    string NextAction)
{
    public bool Success => Outcome == PhoneWriteOutcome.Verified;

    public string ToText()
    {
        var text = new StringBuilder();
        text.AppendLine($"نوشتن IMEI روی گوشی — چیپست {Chipset}");
        text.AppendLine(new string('─', 64));
        text.AppendLine($"IMEI هدف:   {TargetImei}");
        text.AppendLine($"IMEI قبل:   {(ImeiBefore.Length == 15 ? ImeiBefore : "خوانده نشد")}");
        text.AppendLine($"IMEI بعد:   {(ImeiAfter.Length == 15 ? ImeiAfter : "خوانده نشد")}");
        text.AppendLine($"نتیجه: {Outcome}");
        text.AppendLine();
        if (Steps.Count > 0)
        {
            text.AppendLine("رد فرمان‌ها:");
            foreach (var step in Steps)
                text.AppendLine($"  [{(step.Succeeded ? "OK" : "خطا")}] {step.Command} → {Shorten(step.Response)}");
            text.AppendLine();
        }
        text.AppendLine("حکم:");
        text.AppendLine(Verdict);
        text.AppendLine();
        text.AppendLine("قدم بعدی:");
        text.AppendLine(NextAction);
        return text.ToString();
    }

    private static string Shorten(string value)
    {
        var flat = string.Join(" ", value.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return flat.Length <= 100 ? flat : flat[..100] + "…";
    }
}

/// <summary>
/// فاز دوم: نوشتن/تغییر/بازگردانی IMEI روی گوشی.
///
/// مسیرهای قابل اجرا از داخل برنامه (وضعیت واقعی، نه ادعا):
/// • <b>ماژول SIM800L</b> — با AT؛ کامل و مستقل از گوشی (<see cref="ImeiWriter"/>).
/// • <b>گوشی Qualcomm/Samsung روی پورت DIAG</b> — نوشتن NV item 550 با فرمان‌های
///   QC AT (<c>AT+QNVFW</c>/<c>AT+NVITEM</c>) وقتی درایور DIAG و مجوز موجود است.
/// • <b>MediaTek</b> — ابزار META لازم است؛ برنامه پروفایل و اعتبارسنجی می‌دهد و خروجی
///   را از همان ابزار می‌خواند. نوشتن مستقیم با AT روی MTK انجام نمی‌شود.
/// • <b>iOS</b> — NV امضاشده؛ نوشتن ممکن نیست. برنامه صریح همین را می‌گوید و مسیر
///   بازگردانی/ثبت در همتا را پیشنهاد می‌دهد.
///
/// هیچ موفقیتی بدون بازخوانی اعلام نمی‌شود؛ همان قاعده‌ای که در نوشتن ماژول داریم.
/// </summary>
internal static class PhoneImeiWriter
{
    /// <summary>فرمان‌های استاندارد نوشتن NV روی مودم Qualcomm در حالت DIAG.</summary>
    public static IReadOnlyList<string> QualcommNv550Commands(string imei) =>
    [
        "AT+QNVFW=\"/nv/item_files/modem/mmode/imei\"",   // مسیر جدیدتر مودم‌های SDX
        $"AT+NVITEM=550,{imei}",                          // مسیر کلاسیک NV item 550
        "AT+QNVRESTART"
    ];

    /// <summary>اعتبارسنجی قبل از هر نوشتن: قالب، Luhn و تفاوت با مقدار فعلی.</summary>
    public static string ValidateTarget(string target, string current)
    {
        if (!ImeiNumber.TryParse(target, out var parsed, out var error))
            return $"IMEI هدف معتبر نیست: {error}";
        if (parsed!.Value == current)
            return "IMEI هدف با مقدار فعلی یکسان است؛ نوشتن لازم نیست.";
        return string.Empty;
    }

    /// <summary>
    /// تلاش نوشتن روی پورت مودم گوشی (Qualcomm/Samsung در حالت DIAG).
    /// بعد از هر فرمان، اگر IMEI فعلی برابر هدف شد همان‌جا موفق اعلام می‌شود؛ در غیر این صورت
    /// نتیجه «قبول شد ولی ذخیره نشد» یا «مسدود» گزارش می‌شود. بازخوانی همیشه بعد از ری‌استارت مودم.
    /// </summary>
    public static async Task<PhoneWriteResult> WriteViaModemAsync(
        string portName,
        int baudRate,
        string targetImei,
        Func<string, int, CancellationToken, Task<ModemProbeResult>> readCurrent,
        CancellationToken token = default)
    {
        var before = await readCurrent(portName, baudRate, token);
        if (!before.FoundAny && before.Verdict.Contains("پاسخ AT نداد", StringComparison.Ordinal))
            return new(PhoneVendor.Qualcomm.ToString(), targetImei, string.Empty, string.Empty,
                PhoneWriteOutcome.Blocked, [], before.Verdict,
                "پورت مودم آماده نیست. روی سامسونگ *#0808# → DM+MODEM+ADB را انتخاب کن و کابل را یک بار جدا/وصل کن.");

        var validation = ValidateTarget(targetImei, before.Imei1);
        if (validation.Length > 0)
            return new(PhoneVendor.Qualcomm.ToString(), targetImei, before.Imei1, before.Imei1,
                PhoneWriteOutcome.Blocked, [], validation, "IMEI دیگری انتخاب کن یا مسیر بازگردانی را بزن.");

        using var port = new System.IO.Ports.SerialPort(portName, baudRate,
            System.IO.Ports.Parity.None, 8, System.IO.Ports.StopBits.One)
        {
            ReadTimeout = 2500, WriteTimeout = 2500, NewLine = "\r\n", DtrEnable = true, RtsEnable = true
        };

        var steps = new List<PhoneWriteStep>();
        try
        {
            port.Open();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return new(PhoneVendor.Qualcomm.ToString(), targetImei, before.Imei1, before.Imei1,
                PhoneWriteOutcome.Blocked, steps, $"پورت باز نشد: {ex.Message}",
                "پورت را در Device Manager بررسی کن؛ اگر پورت DIAG نیست، حالت USB را عوض کن.");
        }

        try
        {
            foreach (var command in QualcommNv550Commands(targetImei))
            {
                token.ThrowIfCancellationRequested();
                var response = await SendAsync(port, command, token);
                var ok = response.Contains("OK", StringComparison.OrdinalIgnoreCase);
                steps.Add(new PhoneWriteStep(command, response, ok));
            }
        }
        finally
        {
            try { port.Close(); } catch (Exception ex) when (ex is IOException or InvalidOperationException) { }
        }

        // بازخوانی مستقل: مقدار باید بعد از ری‌استارت مودم هم بماند.
        await Task.Delay(4000, token);
        var after = await readCurrent(portName, baudRate, token);
        if (!after.FoundAny)
            return new(PhoneVendor.Qualcomm.ToString(), targetImei, before.Imei1, string.Empty,
                PhoneWriteOutcome.DeviceUnresponsive, steps,
                "مودم بعد از نوشتن پاسخ نداد. کابل را جدا/وصل کن؛ اگر پورت برنگشت، QCN پشتیبان را بازگردان.",
                "پشتیبان QCN را با QPST Restore کن و سپس دوباره تلاش کن.");

        if (after.Imei1 == targetImei)
            return new(PhoneVendor.Qualcomm.ToString(), targetImei, before.Imei1, after.Imei1,
                PhoneWriteOutcome.Verified, steps,
                "IMEI نوشته شد و بعد از ری‌استارت هم ماند.",
                "حالا *#06# را روی گوشی بگیر و ثبت شبکه را بررسی کن.");

        var acceptedAll = steps.Count > 0 && steps.All(step => step.Succeeded);
        return new(PhoneVendor.Qualcomm.ToString(), targetImei, before.Imei1, after.Imei1,
            acceptedAll ? PhoneWriteOutcome.AcceptedButNotPersisted : PhoneWriteOutcome.Failed, steps,
            acceptedAll
                ? "فرمان‌ها OK گرفتند ولی مقدار روی NV ننشست. علت رایج: قفل NV یا نیاز به مجوز امضاشده در همین مودم."
                : "مودم فرمان نوشتن را نپذیرفت. این مدل احتمالاً نوشتن NV را با مجوز امضاشده محدود می‌کند.",
            acceptedAll
                ? "مسیر جایگزین: پشتیبان QCN را ویرایش و Restore کن، یا از ابزار دارای امضای سازنده برای همین مدل استفاده کن."
                : "مسیر جایگزین: QCN Restore یا ابزار رسمی همان مدل. مقدار فعلی دستگاه تغییر نکرده است.");
    }

    private static async Task<string> SendAsync(System.IO.Ports.SerialPort port, string command, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        try { port.DiscardInBuffer(); } catch (Exception ex) when (ex is IOException or InvalidOperationException) { }
        port.Write(command + "\r\n");
        var deadline = DateTime.UtcNow.AddMilliseconds(2500);
        var text = new StringBuilder();
        while (DateTime.UtcNow < deadline)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var chunk = port.ReadExisting();
                if (chunk.Length > 0)
                {
                    text.Append(chunk);
                    var current = text.ToString();
                    if (current.Contains("OK", StringComparison.OrdinalIgnoreCase) ||
                        current.Contains("ERROR", StringComparison.OrdinalIgnoreCase)) break;
                }
            }
            catch (TimeoutException) { }
            await Task.Delay(60, token);
        }
        return text.ToString();
    }

    /// <summary>
    /// ساخت نتیجه نوشتن برای چیپست‌هایی که از داخل برنامه نوشتنشان ممکن نیست، با پیام
    /// دقیق «چه چیزی کم است». خروجی برای UI یکسان است تا کاربر پیام متناقض نبیند.
    /// </summary>
    public static PhoneWriteResult NotPossible(
        PhoneVendor vendor,
        string targetImei,
        string imeiBefore,
        string toolOrReason,
        string nextAction) =>
        new(vendor.ToString(), targetImei, imeiBefore, imeiBefore, PhoneWriteOutcome.Blocked,
            [], toolOrReason, nextAction);

    public static PhoneWriteResult MissingTool(PhoneVendor vendor, string targetImei, string imeiBefore, string tool, string where) =>
        new(vendor.ToString(), targetImei, imeiBefore, imeiBefore, PhoneWriteOutcome.ToolMissing,
            [], $"{tool} پیدا نشد. مسیر مورد انتظار: {where}", $"فایل‌های {tool} را در پوشه ابزارها بگذار و دوباره تلاش کن؛ " +
                                                              "تا آن زمان خواندن از مسیر مودم/ADB کار می‌کند.");

    /// <summary>نقشه نوشتن برای هر چیپست، به‌صورت متنی برای گزارش و راهنما.</summary>
    public static string BuildWriteGuide(PhoneVendor vendor, string targetImei) => vendor switch
    {
        PhoneVendor.MediaTek =>
            "MediaTek — نوشتن از داخل برنامه: ۱) درایور VCOM را نصب کن. ۲) گوشی را خاموش و در حالت BROM وصل کن. " +
            "۳) با SP Flash Tool پارتیشن‌های NVRAM/NVDATA را Read Back کن (پشتیبان). ۴) با Maui Meta مقدار " +
            $"{targetImei} را بنویس. ۵) برنامه بعد از هر مرحله بازخوانی می‌کند و فقط با تطابق، موفق اعلام می‌کند.",
        PhoneVendor.Qualcomm =>
            "Qualcomm — نوشتن از داخل برنامه: ۱) پورت DIAG را فعال کن (روت یا منوی مهندسی). ۲) پشتیبان QCN بگیر. " +
            $"۳) NV item 550 را با مقدار {targetImei} بنویس. ۴) ری‌استارت و بازخوانی خودکار.",
        PhoneVendor.Unisoc =>
            "Unisoc/SPD — نوشتن با ResearchDownload در حالت FDL و PAC همان مدل؛ برنامه بازخوانی و اعتبارسنجی را انجام می‌دهد.",
        PhoneVendor.HiSilicon =>
            "HiSilicon — ابزار عمومی برای نوشتن IMEI وجود ندارد؛ فقط سرویس رسمی سازنده.",
        PhoneVendor.Apple =>
            "iOS — IMEI در NV امضاشده بیس‌باند است. نوشتن، تغییر یا بازگردانی از داخل هیچ برنامه‌ای روی ویندوز ممکن نیست؛ " +
            "جیلبریک هم به بیس‌باند نمی‌رسد و بازنویسی، Activation را می‌شکند.",
        _ => "چیپست شناسایی نشد؛ اول مدل را بخوان تا مسیر درست انتخاب شود."
    };
}
