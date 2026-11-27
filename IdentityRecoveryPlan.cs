using System.Text;

namespace SIM800L_Flasher;

/// <summary>
/// تحلیل «IMEI ناخوانا / خالی» و مسیر بازگردانی آن.
///
/// سناریوی واقعی تعمیرگاه: برد اصلی (Mainboard) عوض شده، گوشی <c>*#06#</c> را Unknown نشان
/// میدهد و IMEI قبلی در همتا ثبت بوده. این وضعیت یعنی پارتیشن هویت (EFS/NVRAM/CP) روی برد
/// جدید خالی یا ناسازگار است — نه اینکه IMEI «گم» شده باشد.
///
/// این کلاس هیچ هویتی را بازنویسی نمیکند. کاری که میکند: ناحیه مشکل را دقیق مشخص میکند،
/// میگوید کدام مدرک لازم است، و مسیر رسمی بازگردانی را برای همان چیپست میسازد — همان مسیری
/// که مرکز خدمات سازنده با فایل کارخانه انجام میدهد.
/// </summary>
internal enum IdentityDamage
{
    /// <summary>IMEI درست خوانده شد.</summary>
    Healthy,

    /// <summary>خالی، صفر یا Unknown — پارتیشن هویت پاک/خالی است.</summary>
    Empty,

    /// <summary>۱۵ رقم ولی Luhn غلط یا TAC ناشناس — مقدار خراب یا نیمه‌نوشته.</summary>
    Corrupt,

    /// <summary>هیچ مسیری پاسخ نداد؛ ممکن است مودم اصلاً بالا نیامده باشد.</summary>
    ModemDead
}

internal sealed record IdentityRecoveryPlan(
    IdentityDamage Damage,
    string Evidence,
    string Chipset,
    IReadOnlyList<string> IdentifySteps,
    IReadOnlyList<string> OfficialPath,
    IReadOnlyList<string> Documents,
    string WhyNotRewrite,
    string Verdict)
{
    public bool NeedsOfficialRestore => Damage is not IdentityDamage.Healthy;

    public string ToText()
    {
        var text = new StringBuilder();
        text.AppendLine("تحلیل IMEI ناخوانا و مسیر بازگردانی");
        text.AppendLine(new string('═', 62));
        text.AppendLine($"وضعیت: {Damage}");
        text.AppendLine($"شاهد: {Evidence}");
        text.AppendLine($"چیپست: {Chipset}");
        text.AppendLine();
        text.AppendLine("۱) قطعی‌کردن محل خرابی:");
        var index = 1;
        foreach (var step in IdentifySteps) text.AppendLine($"   {index++}) {step}");
        text.AppendLine();
        text.AppendLine("۲) مسیر رسمی بازگردانی:");
        index = 1;
        foreach (var step in OfficialPath) text.AppendLine($"   {index++}) {step}");
        text.AppendLine();
        text.AppendLine("۳) مدارکی که همراه دستگاه ببر:");
        foreach (var document in Documents) text.AppendLine("   • " + document);
        text.AppendLine();
        text.AppendLine("چرا نوشتن دستی روی برد جدید جواب نمی‌دهد:");
        text.AppendLine(WhyNotRewrite);
        text.AppendLine();
        text.AppendLine(Verdict);
        return text.ToString();
    }

    public static IdentityRecoveryPlan Build(string imei, string chipset, string model, string certificateId)
    {
        var damage = Classify(imei);
        var evidence = damage switch
        {
            IdentityDamage.Healthy => $"IMEI خوانده‌شده: {imei}",
            IdentityDamage.Empty => "پاسخ دستگاه خالی، صفر یا Unknown بود (پارتیشن هویت خالی است).",
            IdentityDamage.Corrupt => $"مقدار «{imei}» قالب ۱۵ رقمی دارد ولی Luhn آن معتبر نیست.",
            _ => "هیچ مسیری پاسخ نداد؛ مودم بالا نیامده یا پورت مودم بسته است."
        };

        var identify = new List<string>
        {
            "روی گوشی *#06# را بگیر: خالی، Unknown یا 00000000000000؟ همین یک پاسخ، نصف تشخیص است.",
            "در XAnten «خواندن IMEI از هر مسیری» را بزن و مسیرهای موفق/ردشده را ببین (پورت مودم در اولویت).",
            "اگر IMEI2 یا EID خوانده شد ولی IMEI1 نه، مودم سالم است و فقط ناحیه IMEI1 آسیب دیده.",
            "اگر هیچ‌کدام خوانده نشد: بیس‌بند در Device Manager/ATI را چک کن (AT+CGMR). بیس‌بند خالی = مشکل CP، نه IMEI."
        };

        var official = chipset switch
        {
            "Qualcomm" or "MediaTek" => new List<string>
            {
                "با «پشتیبان هویت (EFS/NVRAM)» از برد قدیمی (اگر فایلش را داری) یا از یک برد سالم هم‌مدل، نسخه پشتیبان سالم تهیه کن.",
                chipset == "Qualcomm"
                    ? "با QPST/QFIL فایل QCN سالم را روی برد جدید Restore کن — این کار پارتیشن هویت را از حالت خالی درمی‌آورد."
                    : "با SP Flash Tool پارتیشن NVRAM/NVDATA سالم را روی برد جدید بنویس.",
                "اگر پشتیبان نداری، مرکز خدمات همان برند با فایل کارخانه (CP/EFS) پارتیشن را بازسازی می‌کند.",
                "بعد از بازگردانی، IMEI کارخانه‌ای که با همان برد آمده خوانده می‌شود و همان را در همتا ثبت می‌کنی."
            },
            "Apple" => new List<string>
            {
                "آیفون با برد تعویض‌شده معمولاً در حالت Activation Lock یا Baseband نامعتبر می‌ماند؛ Apple Store/نمایندگی مجاز تنها مسیر بازگردانی رسمی است.",
                "برگه یکپارچگی هویت را همراه دستگاه ببر تا سریال/UDID و نبود IMEI مستند باشد."
            },
            _ => new List<string>
            {
                "چیپست را اول مشخص کن (ATI/AT+CGMM یا ADB) تا مسیر درست انتخاب شود."
            }
        };

        var documents = new List<string>
        {
            "فاکتور خرید برد جدید (برای اثبات تعویض سخت‌افزار)",
            "مدرک ثبت IMEI قبلی در همتا (تیکت/پیام تأیید)",
            certificateId.Length > 0 ? $"برگه یکپارچگی هویت XAnten (سند {certificateId})" : "برگه یکپارچگی هویت XAnten",
            "شرح تعمیر: چه قطعه‌ای عوض شد و چه زمانی"
        };

        var whyNotRewrite =
            "روی برد جدید، ناحیه هویت امضاشده است: Qualcomm و MediaTek مقدار را با کلید/گواهی همان برد اعتبارسنجی می‌کنند و " +
            "iOS مقدار را در Secure Enclave نگه می‌دارد. نوشتن عدد دلخواه روی این ناحیه، امضا را می‌شکند؛ نتیجه‌اش " +
            "«OK بدون تغییر» است یا No Service دائمی. به همین دلیل مسیر درست، بازگردانی ناحیه از پشتیبان سالم است، نه نوشتن عدد.";

        var verdict = damage switch
        {
            IdentityDamage.Healthy => "IMEI سالم خوانده شد؛ نیازی به بازگردانی ناحیه هویت نیست.",
            IdentityDamage.Empty => "پارتیشن هویت برد جدید خالی است. اولویت: بازگردانی CP/EFS از پشتیبان یا فایل کارخانه؛ " +
                                    "بعد از آن، IMEI کارخانه‌ای که با برد آمده را در همتا ثبت کن.",
            IdentityDamage.Corrupt => "مقدار موجود معتبر نیست. با پشتیبان سالم بازنویسی کن (Restore)، نه با عدد دستی.",
            _ => "مودم پاسخ نمی‌دهد. اول بیس‌بند/CP را درست کن؛ نوشتن IMEI روی مودم خاموش معنا ندارد."
        };

        return new IdentityRecoveryPlan(damage, evidence, chipset, identify, official, documents, whyNotRewrite, verdict);
    }

    internal static IdentityDamage Classify(string imei)
    {
        if (string.IsNullOrWhiteSpace(imei)) return IdentityDamage.Empty;
        var digits = new string(imei.Where(char.IsAsciiDigit).ToArray());
        if (digits.Length == 0) return IdentityDamage.Empty;
        if (digits.All(c => c == '0')) return IdentityDamage.Empty;
        if (digits.Length != 15) return IdentityDamage.Corrupt;
        return ImeiNumber.MatchesLuhn(digits) ? IdentityDamage.Healthy : IdentityDamage.Corrupt;
    }
}
