using System.Text;

namespace SIM800L_Flasher;

/// <summary>
/// توضیح دقیق «چه کسی IMEI را به شبکه می‌گوید» و اینکه چرا سیم‌کارت واسطه IMEI نیست.
///
/// در پروتکل GSM/UMTS/LTE، هنگام Attach، پیام Identity Response از **ME (گوشی یا ماژول)** ارسال می‌شود
/// و IMEI/IMEISV داخل همان پیام از حافظه خود دستگاه خوانده می‌شود. سیم‌کارت فقط IMSI/Kc را برای
/// احراز هویت مشترک فراهم می‌کند. فیلد IMEI در کارت (EF 6F7E طبق TS 51.011) یک گزارش اختیاری است
/// که خود دستگاه می‌نویسد و در شناسایی شبکه استفاده نمی‌شود.
/// </summary>
internal static class IdentityFlow
{
    public static IReadOnlyList<(string Stage, string Who, string What)> Stages() =>
    [
        ("۱) روشن شدن", "گوشی", "IMEI را از حافظه امن/بوت‌لودر خودش می‌خواند — نه از سیم‌کارت."),
        ("۲) خواندن کارت", "گوشی ↔ سیم‌کارت", "IMSI، کلیدهای Kc/Ki و وضعیت PIN خوانده می‌شود؛ IMEI از کارت خوانده نمی‌شود."),
        ("۳) Attach / Location Update", "گوشی → شبکه (BTS/HLR)", "پیام Identity Response شامل IMEI خود گوشی ارسال می‌شود."),
        ("۴) بررسی EIR", "شبکه", "اپراتور IMEI را با فهرست سفید/سیاه (CEIR/EIR) تطبیق می‌دهد؛ اینجا IMEI سیم‌کارت هیچ نقشی ندارد."),
        ("۵) استفاده", "گوشی", "سیم‌کارت فقط هویت مشترک (شماره/اشتراک) را می‌دهد؛ IMEI همان است که در مرحله ۳ اعلام شد.")
    ];

    public static string Explain()
    {
        var builder = new StringBuilder();
        builder.AppendLine("سیم‌کارت واسطهٔ IMEI نیست؛ فقط واسطهٔ هویت مشترک (شماره) است:");
        builder.AppendLine();
        foreach (var (stage, who, what) in Stages())
            builder.AppendLine($"• {stage} — {who}: {what}");
        builder.AppendLine();
        builder.AppendLine("پس اگر IMEI را روی حافظه SIM800L/SIM808 بنویسید و سیم‌کارت را به گوشی دیگری بزنید،");
        builder.AppendLine("گوشی جدید IMEI خودش را اعلام می‌کند. برای اینکه «همان IMEI» در شبکه دیده شود،");
        builder.AppendLine("باید روی همان دستگاهی نوشته شود که سیم‌کارت داخلش قرار می‌گیرد.");
        return builder.ToString();
    }

    /// <summary>پاسخ کوتاه و صریح به پرسش «آیا با تعویض گوشی IMEI پاک می‌شود؟»</summary>
    public static string AnswerSwapQuestion() =>
        "خیر، IMEI روی ماژول پاک نمی‌شود؛ اما گوشی دیگر از IMEI خودش استفاده می‌کند. " +
        "IMEI فقط روی دستگاهی معنا دارد که واقعاً به شبکه وصل می‌شود.";
}
