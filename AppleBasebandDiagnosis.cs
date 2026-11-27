using System.Text;

namespace SIM800L_Flasher;

/// <summary>
/// تشخیص «آیفون IMEI ندارد» — تفاوت حیاتی بین سه وضعیت که ظاهرشان شبیه است:
///
/// ۱) <b>مودم شناسایی نشده</b> (BasebandVersion خالی/Unknown): سخت‌افزار مودم بالا نیامده.
///    با هیچ نرم‌افزاری نمی‌شود IMEI ساخت، چون واحدی وجود ندارد که عدد را نگه دارد.
///    در تعمیر با تعویض برد، این حالت غالب است و علتش جفت‌نشدن مودم با برد/گواهی است.
/// ۲) <b>مودم هست ولی IMEI خالی</b>: پارتیشن هویت مودم خالی شده. مسیر درست، بازگردانی رسمی
///    (سرویس اپل/نمایندگی) است، نه نوشتن عدد.
/// ۳) <b>IMEI هست ولی فعال‌سازی نشده</b>: عدد خوانده می‌شود، فقط دستگاه Activated نیست —
///    این اصلاً مشکل IMEI نیست.
///
/// این کلاس هیچ نوشتنی انجام نمی‌دهد؛ هدفش این است که تعمیرگاه بداند دقیقاً کدام‌یک از سه
/// حالت است تا وقت و هزینه روی مسیر غلط هدر نرود.
/// </summary>
internal enum AppleIdentityState
{
    /// <summary>IMEI سالم خوانده شد و دستگاه فعال است.</summary>
    Ready,

    /// <summary>IMEI خوانده می‌شود ولی دستگاه Activated نیست.</summary>
    NotActivated,

    /// <summary>مودم هست ولی IMEI خالی است (پارتیشن هویت خالی).</summary>
    EmptyIdentity,

    /// <summary>مودم اصلاً شناسایی نشده است — ساختن IMEI ممکن نیست.</summary>
    BasebandMissing,

    /// <summary>Trust/مجوز نبود، پس تشخیص قطعی نیست.</summary>
    Indeterminate
}

internal sealed record AppleBasebandDiagnosis(
    AppleIdentityState State,
    string Evidence,
    IReadOnlyList<string> Checks,
    string RepairPath,
    string CanImeiBeWritten,
    string Verdict)
{
    public string ToText()
    {
        var text = new StringBuilder();
        text.AppendLine("تشخیص آیفون بدون IMEI — برد تعویض‌شده");
        text.AppendLine(new string('═', 64));
        text.AppendLine($"وضعیت: {State}");
        text.AppendLine($"شاهد: {Evidence}");
        text.AppendLine();
        text.AppendLine("۱) چه چیزی را چک کن (به همین ترتیب):");
        var index = 1;
        foreach (var check in Checks) text.AppendLine($"   {index++}) {check}");
        text.AppendLine();
        text.AppendLine("۲) مسیر تعمیر:");
        text.AppendLine(RepairPath);
        text.AppendLine();
        text.AppendLine("۳) آیا می‌شود IMEI نوشت؟");
        text.AppendLine(CanImeiBeWritten);
        text.AppendLine();
        text.AppendLine("حکم:");
        text.AppendLine(Verdict);
        return text.ToString();
    }

    public static AppleBasebandDiagnosis Build(AppleIdentityResult? identity, string model, string iosVersion)
    {
        if (identity is null)
            return new(AppleIdentityState.Indeterminate, "دستگاه اپل خوانده نشد.",
                ["کابل دیتا و باز بودن قفل صفحه را بررسی کن.", "Trust This Computer را روی گوشی بزن.",
                 "اگر macOS/Apple Devices نصب نیست، آن را نصب کن تا درایور usbmuxd برقرار شود."],
                "بدون اتصال، هیچ تشخیصی قطعی نیست.",
                "نامشخص — اول اتصال.",
                "اول باید دستگاه با Trust خوانده شود؛ بعد از آن سه حالت بالا از هم جدا می‌شوند.");

        if (identity.BasebandMissing)
            return new(AppleIdentityState.BasebandMissing,
                $"BasebandVersion={(identity.Baseband.Length == 0 ? "خالی" : identity.Baseband)}، IMEI خوانده نشد.",
                [
                    "روی آیفون: Settings → General → About → Modem Firmware. اگر خالی/`-` است، مودم شناسایی نشده.",
                    "در XAnten: «خواندن از گوشی» و سپس «برگه یکپارچگی هویت» — مقدار Baseband در گزارش ثبت می‌شود.",
                    "با *#06# چک کن: اگر صفحه اصلاً IMEI نشان نمی‌دهد یا `No SIM` و `Searching` دائمی است، همان حالت مودم غایب است."
                ],
                "برد تعویض‌شده در آیفون یعنی مودم باید با همان برد و گواهی سازگار باشد؛ آیفون چنین جفت‌سازی را " +
                "در سطح سخت‌افزار/گواهی انجام می‌دهد و از بیرون قابل بازسازی نیست. مسیر عملی: برگشت به نمایندگی مجاز اپل " +
                "یا تعویض برد با برد کامل و سالمِ همان دستگاه (Donor کامل با سریال جفت‌شده).",
                "خیر. وقتی BasebandVersion خالی است، واحدی برای نگه‌داشتن IMEI وجود ندارد. نوشتن عدد روی آیفون " +
                "با هیچ ابزاری ممکن نیست — نه جیلبریک، نه ابزار تعمیرگاهی — چون مقدار در Secure Enclave/بیس‌باند امضاشده است.",
                "این حالت «IMEI ندارد» نیست؛ «مودم ندارد» است. تا مودم برنگردد، هیچ IMEI‌ای قابل خواندن یا نوشتن نیست.");

        if (identity.Imei1.Length != 15)
            return new(AppleIdentityState.EmptyIdentity,
                $"مودم حاضر است (BasebandVersion={identity.Baseband}) ولی IMEI خالی است.",
                [
                    "با ideviceinfo کلید InternationalMobileEquipmentIdentity را مستقیم بخوان تا خالی بودن تأیید شود.",
                    "وضعیت فعال‌سازی (ActivationState) را چک کن؛ اگر Unactivated است، ممکن است IMEI فقط پس از Activation گزارش شود.",
                    "سیم‌کارت معتبر بگذار و یک بار شبکه را جست‌وجو کن؛ بعضی آیفون‌ها IMEI را فقط پس از ثبت مودم نشان می‌دهند."
                ],
                "پارتیشن هویت مودم خالی است. مسیر درست: بازگردانی رسمی از طریق Apple Service (Genius Bar/نمایندگی مجاز) " +
                "با ارائه فاکتور و شرح تعمیر. آن‌ها فایل کارخانه را روی مودم برمی‌گردانند و IMEI اصلی همان برد بازمی‌گردد.",
                "خیر. روی آیفون نوشتن IMEI با هیچ ابزار بیرونی ممکن نیست؛ مقدار در NV امضاشده بیس‌باند است و " +
                "بازنویسی، Activation را می‌شکند و دستگاه را قفل می‌کند.",
                "مودم هست، هویت خالی است. بازگردانی رسمی اپل تنها مسیر است؛ تعمیرگاه‌های مجاز همین کار را می‌کنند.");

        if (identity.ActivationState.Contains("Unactivated", StringComparison.OrdinalIgnoreCase))
            return new(AppleIdentityState.NotActivated,
                $"IMEI={identity.Imei1} خوانده شد ولی ActivationState={identity.ActivationState}.",
                ["IMEI را با *#06# مقایسه کن تا مطمئن شوی همان عدد است.", "اتصال به شبکه/سیم‌کارت را برای فعال‌سازی بررسی کن."],
                "مشکل IMEI نیست؛ دستگاه فعال نشده. با سیم‌کارت و شبکه سالم، فعال‌سازی انجام می‌شود.",
                "لازم نیست — IMEI سالم است.",
                "IMEI موجود است؛ نیازی به نوشتن نیست. اگر پیام Activation Lock می‌بینی، آن قفل مالکیت است نه IMEI.");

        return new(AppleIdentityState.Ready, $"IMEI={identity.Imei1}، BasebandVersion={identity.Baseband}",
            ["*#06# را برای تأیید بگیر.", "IMEI را در همتا استعلام کن."],
            "دستگاه سالم است؛ اگر سرویس ندارد، مشکل در ثبت همتا یا سیم‌کارت است، نه در IMEI.",
            "لازم نیست.",
            "هویت سالم خوانده شد. برای آنتن، مسیر ثبت در همتا را برو.");
    }
}
