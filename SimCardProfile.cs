using System.Text;

namespace SIM800L_Flasher;

/// <summary>
/// شناسایی ابزاری که جلوی برنامه قرار می‌گیرد. SIM800L فقط یک کارت فیزیکی از طریق AT+ICCID/AT+CIMI می‌خواند؛
/// eSIM/eUICC و MFF2 با پروتکل LPA/APDU کار می‌کنند و فرمان‌های گروه AT+CCHO، AT+CCHC، AT+CGLA و AT+CSIM در
/// بیشتر نسخه‌های SIM800L پشتیبانی نمی‌شوند. این کلاس تفاوت را شفاف می‌کند تا کاربر مسیر اشتباه نرود.
/// </summary>
internal static class SimCardProfile
{
    public static IReadOnlyList<string> UiccProbeCommands() =>
    [
        "AT+CPIN?",   // وضعیت PIN
        "AT+CCID",    // سریال کارت
        "AT+CIMI",    // IMSI
        "AT+CCHO",    // eUICC logical channel (معمولاً ERROR روی SIM800L)
        "AT+CSIM"     // APDU خام (معمولاً ERROR روی SIM800L)
    ];

    public static string Describe(string iccid, string imsi)
    {
        var builder = new StringBuilder();
        var hasIccid = !string.IsNullOrWhiteSpace(iccid) && iccid != "—";
        var hasImsi = !string.IsNullOrWhiteSpace(imsi) && imsi != "—";

        builder.AppendLine(hasIccid
            ? $"کارت فیزیکی شناسایی شد. ICCID: {iccid}"
            : "هیچ پاسخ ICCID دریافت نشد؛ کارت فیزیکی نیست یا در جای خود قرار نگرفته است.");
        builder.AppendLine(hasImsi
            ? $"IMSI: {imsi}"
            : "IMSI خوانده نشد؛ ثبت شبکه و اعتبار سیم‌کارت را بررسی کنید.");

        builder.AppendLine();
        builder.AppendLine("مسیرهای پشتیبانی‌شده در این برنامه:");
        builder.AppendLine("• خواندن شناسه‌های کارت (ICCID/IMSI/اپراتور) از طریق AT — پشتیبانی می‌شود.");
        builder.AppendLine("• نوشتن داده روی حافظه کارت — برای کارت فیزیکی از طریق SIM800L پشتیبانی نمی‌شود.");
        builder.AppendLine("• eSIM/eUICC — نیازمند تجهیزات و ابزار LPA/APDU سازگار، نه SIM800L معمولی.");
        return builder.ToString();
    }

    /// <summary>هشدارهای امنیتی که باید قبل از هر کار روی کارت خوانده شود.</summary>
    public static IReadOnlyList<string> SafetyNotes() =>
    [
        "اگر PIN فعال باشد، ابتدا AT+CPIN? را بررسی کنید؛ سه بار اشتباه، کارت را با PUK قفل می‌کند.",
        "برنامه هیچ فرمان AT+CLCK/AT+CPWD یا تغییر PIN/PUK ارسال نمی‌کند.",
        "خواندن ICCID و IMSI داده شخصی است؛ لاگ‌ها را با احتیاط نگه دارید.",
        "نوشتن IMEI یا شناسه جعلی در بسیاری از کشورها (از جمله ایران و اتحادیه اروپا) جرم است و دستگاه را از شبکه اپراتور محروم می‌کند."
    ];
}
