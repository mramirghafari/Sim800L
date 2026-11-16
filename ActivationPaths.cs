using System.Text;

namespace SIM800L_Flasher;

/// <summary>
/// مقایسه مسیرهای واقعی «فعال کردن دستگاه غیرمجاز» — با هزینه، زمان، ریسک و پایداری.
/// مرتب‌سازی بر اساس «کمترین هزینه‌ای که واقعاً جواب می‌دهد».
/// </summary>
internal sealed record ActivationPath(
    int Rank,
    string Title,
    string HowItWorks,
    string Cost,
    string TimeToService,
    string Stability,
    string Risk,
    bool RestoresThisDevice);

internal static class ActivationPaths
{
    public static IReadOnlyList<ActivationPath> All(string targetDevice) =>
    [
        new(1,
            "گوشی لنگر ثبت‌شده (ارزان‌ترین راهی که واقعاً آنتن می‌دهد)",
            "یک گوشی ارزان با IMEI ثبت‌شده بخر یا از گوشی ثبت‌شده موجود استفاده کن؛ سیم‌کارت را در آن بگذار.",
            "قیمت یک گوشی ارزان (کمتر از حقوق ورودی دستگاه گران)",
            "همان روز",
            "بالا — سرویس پایدار و بدون تغییر نرم‌افزاری",
            "هیچ؛ فقط باید دو دستگاه هم‌زمان با یک IMEI روشن نباشند",
            RestoresThisDevice: false),

        new(2,
            "نوشتن IMEI ثبت‌شده روی NV خود گوشی اندروید",
            "با MTK META / SP Flash Tool یا QCOM QPST، IMEI گوشی عوض می‌شود و شبکه همان را می‌بیند.",
            "صفر (ابزار رایگان) + ریسک بریک",
            "۱ تا ۳ ساعت",
            "متوسط — فلش رسمی firmware یا ریست کارخانه مقدار را برمی‌گرداند",
            "بالا؛ بدون پشتیبان NVRAM/QCN گوشی ممکن است بی‌شبکه بماند. روی secure boot بسته است.",
            RestoresThisDevice: true),

        new(3,
            "ثبت مسافری در همتا",
            "با پاسپورت و کد رهگیری گمرک، دستگاه در سامانه ثبت و مسدودی برداشته می‌شود.",
            "حقوق ورودی بر اساس ارزش دستگاه — برای گوشی گران سنگین",
            "۲۴ تا ۷۲ ساعت",
            "بالا و قانونی؛ ماندگار",
            "هیچ",
            RestoresThisDevice: true),

        new(4,
            "نوشتن IMEI ثبت‌شده روی ماژول SIM800L (همین برنامه)",
            "IMEI روی NV ماژول نوشته می‌شود؛ سیم‌کارت در ماژول سرویس می‌گیرد.",
            "صفر",
            "دقایق",
            "پایین — فقط تا وقتی سیم در ماژول است؛ با انتقال به گوشی از بین می‌رود",
            "IMEI تکراری اگر گوشی ثبت‌شده هم روشن باشد",
            RestoresThisDevice: false),

        new(5,
            "نوشتن IMEI روی آیفون",
            "—",
            "—",
            "ممکن نیست",
            "—",
            "IMEI در NV امضاشده بیس‌باند است؛ جیلبریک هم به آن نمی‌رسد و فعال‌سازی می‌شکند.",
            RestoresThisDevice: false)
    ];

    public static string BuildReport(string targetDevice)
    {
        var text = new StringBuilder();
        text.AppendLine($"مسیرهای فعال‌سازی «{targetDevice}» — مرتب‌شده بر اساس هزینه واقعی");
        text.AppendLine(new string('─', 68));
        foreach (var path in All(targetDevice))
        {
            text.AppendLine($"{path.Rank}) {path.Title}");
            text.AppendLine($"   چطور: {path.HowItWorks}");
            text.AppendLine($"   هزینه: {path.Cost}   |   زمان تا سرویس: {path.TimeToService}");
            text.AppendLine($"   پایداری: {path.Stability}");
            text.AppendLine($"   ریسک: {path.Risk}");
            text.AppendLine($"   همین دستگاه را برمی‌گرداند؟ {(path.RestoresThisDevice ? "بله" : "خیر")}");
            text.AppendLine();
        }
        text.AppendLine("جمع‌بندی: اگر هدف «خودِ این گوشی سرویس بگیرد» است، فقط گزینه ۲ یا ۳ این کار را می‌کند. " +
                        "گزینه ۱ ارزان‌ترین راهی است که امروز سرویس می‌دهد، و گزینه ۴ فقط هویت ماژول را عوض می‌کند.");
        return text.ToString();
    }
}
