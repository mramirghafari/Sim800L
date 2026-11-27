using System.Text;

namespace SIM800L_Flasher;

/// <summary>
/// دستیار ثبت دستگاه در همتا: با شناسه‌هایی که از خود گوشی خوانده شده، مسیر قانونی
/// فعال‌سازی را قدم‌به‌قدم می‌سازد و می‌گوید چه مدرکی لازم است.
///
/// سه حالت واقعی که سامانه همتا می‌شناسد:
/// ۱) دستگاه مسافری — پاسپورت + کد رهگیری گمرک + حقوق ورودی.
/// ۲) دستگاه خریداری‌شده از فروشنده — فروشنده باید با فاکتور رسمی ثبت کرده باشد.
/// ۳) رفع مسدودی / بازگردانی — وقتی دستگاه قبلاً ثبت بوده و مالکیتش مستند است.
///
/// این کلاس هیچ ادعایی درباره نتیجه ندارد؛ فقط مدارک و ترتیب کار را کنار شناسه‌های
/// خوانده‌شده می‌گذارد تا پیگیری قابل انجام باشد.
/// </summary>
internal sealed record HamtaRegistrationGuide(
    string Scenario,
    IReadOnlyList<string> Documents,
    IReadOnlyList<string> Steps,
    string ImeiUsed,
    string Verdict)
{
    public string ToText()
    {
        var text = new StringBuilder();
        text.AppendLine("دستیار ثبت دستگاه در همتا");
        text.AppendLine(new string('─', 62));
        text.AppendLine($"حالت پیشنهادی: {Scenario}");
        text.AppendLine($"IMEI مورد استفاده: {(ImeiUsed.Length == 15 ? ImeiUsed : "— (اول IMEI را بخوان)")}");
        text.AppendLine();
        text.AppendLine("مدارک لازم:");
        foreach (var document in Documents) text.AppendLine("  • " + document);
        text.AppendLine();
        text.AppendLine("ترتیب کار:");
        var index = 1;
        foreach (var step in Steps) text.AppendLine($"  {index++}) {step}");
        text.AppendLine();
        text.AppendLine("نتیجه:");
        text.AppendLine(Verdict);
        text.AppendLine();
        text.AppendLine("پس از تأیید سامانه، همان سیم‌کارت در همین دستگاه سرویس می‌گیرد — بدون تغییر هیچ شناسه‌ای. " +
                        "برگه یکپارچگی هویت را همراه داشته باش تا اگر IMEI خوانده‌شده با مدرک فرق داشت، همان‌جا بررسی شود.");
        return text.ToString();
    }

    public static HamtaRegistrationGuide Build(UniversalIdentity? identity, DeviceIdentityCertificate? certificate)
    {
        var imei = identity?.Imei1 ?? string.Empty;
        var known = imei.Length == 15;
        var certificateId = certificate?.DocumentId;

        return new HamtaRegistrationGuide(
            "دستگاه مسافری (واردشده شخصی) — اگر رهگیری گمرکی داری",
            [
                "پاسپورت مالک",
                "کد رهگیری گمرکی دستگاه (از اظهارنامه یا سامانه گمرک)",
                "IMEI دستگاه: " + (known ? imei : "از برگه یکپارچگی هویت"),
                "شماره سیم‌کارت و کد ملی صاحب سیم‌کارت",
                "برگه یکپارچگی هویت XAnten" + (certificateId is null ? "" : $" (سند {certificateId})")
            ],
            [
                "ورود به سامانه همتا و انتخاب «ثبت دستگاه» → «مسافری»",
                "IMEI را دقیقاً همان مقدار خوانده‌شده از دستگاه وارد کن (نه از جعبه، اگر فرق دارد)",
                "کد رهگیری گمرکی و اطلاعات پاسپورت را وارد کن",
                "پرداخت حقوق ورودی بر اساس ارزش دستگاه",
                "پیگیری وضعیت تا تأیید؛ معمولاً ۲۴ تا ۷۲ ساعت",
                "پس از تأیید، سیم‌کارت را در دستگاه بگذار و ثبت شبکه را بررسی کن"
            ],
            imei,
            known
                ? "IMEI معتبر خوانده شده؛ همین مقدار را در سامانه وارد کن. اگر سامانه گفت «قبلاً ثبت شده»، " +
                  "حالت «رفع مسدودی» را بزن و مالکیت را با فاکتور/پاسپورت مستند کن."
                : "اول IMEI را با «خواندن IMEI از هر مسیری» بگیر؛ بدون IMEI ثبت سامانه ممکن نیست. " +
                  "اگر هیچ مسیری IMEI نداد، احتمالاً IMEI دستگاه خالی است و اول باید با مرکز رسمی بازگردانی شود.");
    }
}
