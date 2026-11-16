using System.Globalization;
using System.Text;

namespace SIM800L_Flasher;

/// <summary>
/// «برگه تأیید» قابل استعلام: خلاصه‌ای یک‌صفحه‌ای با IMEI نهایی، مدل، نسخه firmware، تعداد چرخه‌های
/// پایداری و نتیجه تکتک آزمون‌ها. برای استعلام در سامانه/ثبت یا ارسال به پشتیبانی.
/// </summary>
internal sealed record VerificationCertificate(
    string ImeiFinal,
    string ImeiOriginal,
    string ModuleModel,
    string Family,
    string FirmwareRevision,
    string PortName,
    string OperatorName,
    string IccidMasked,
    int PersistenceCycles,
    bool Persisted,
    string Strategy,
    DateTime GeneratedUtc,
    IReadOnlyList<string> Checks)
{
    public string Serial => $"{GeneratedUtc:yyyyMMddHHmmss}-{ImeiFinal[^4..]}";

    public string Render()
    {
        var builder = new StringBuilder();
        builder.AppendLine("══════════════════════════════════════════════════");
        builder.AppendLine("        برگه تأیید IMEI — XAnten");
        builder.AppendLine("══════════════════════════════════════════════════");
        builder.AppendLine($" شماره سند          : {Serial}");
        builder.AppendLine($" تاریخ (UTC)        : {GeneratedUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}");
        builder.AppendLine($" تاریخ (محلی)       : {GeneratedUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
        builder.AppendLine("──────────────────────────────────────────────────");
        builder.AppendLine($" IMEI نهایی         : {ImeiFinal}");
        builder.AppendLine($" IMEI اولیه (بکاپ)  : {(ImeiOriginal.Length == 15 ? ImeiOriginal : "خوانده نشد")}");
        builder.AppendLine($" مدل ماژول          : {ModuleModel}");
        builder.AppendLine($" خانواده            : {Family}");
        builder.AppendLine($" نسخه firmware      : {FirmwareRevision}");
        builder.AppendLine($" پورت               : {PortName}");
        builder.AppendLine($" اپراتور            : {OperatorName}");
        builder.AppendLine($" ICCID (ماسک)       : {IccidMasked}");
        builder.AppendLine("──────────────────────────────────────────────────");
        builder.AppendLine($" استراتژی نوشتن     : {Strategy}");
        builder.AppendLine($" چرخه پایداری       : {PersistenceCycles}");
        builder.AppendLine($" نتیجه پایداری      : {(Persisted ? "پایدار (در همه چرخه‌ها ثابت ماند)" : "ناپایدار / تأییدنشده")}");
        builder.AppendLine("──────────────────────────────────────────────────");
        builder.AppendLine(" آزمون‌ها:");
        foreach (var check in Checks) builder.AppendLine("   • " + check);
        builder.AppendLine("──────────────────────────────────────────────────");
        builder.AppendLine(" توجه: IMEI بالا هویت خود این ماژول است؛ همان مقداری که");
        builder.AppendLine(" هنگام اتصال به شبکه در Identity Response اعلام می‌شود.");
        builder.AppendLine(" تعویض سیم‌کارت به گوشی دیگر، IMEI آن گوشی را تغییر نمی‌دهد.");
        builder.AppendLine("══════════════════════════════════════════════════");
        return builder.ToString();
    }

    public string RenderFileName() => $"IMEI-cert-{ImeiFinal}-{GeneratedUtc:yyyyMMdd-HHmm}.txt";

    /// <summary>نسخه کوتاه برای کپی سریع در سامانه/پیام.</summary>
    public string RenderCompact() =>
        $"IMEI: {ImeiFinal} | مدل: {ModuleModel} | خانواده: {Family} | " +
        $"firmware: {FirmwareRevision} | پایداری: {(Persisted ? $"{PersistenceCycles} چرخه ✅" : "ناموفق ❌")} | " +
        $"تاریخ: {GeneratedUtc.ToLocalTime():yyyy-MM-dd HH:mm}";
}
