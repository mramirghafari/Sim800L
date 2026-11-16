using System.Text;

namespace SIM800L_Flasher;

/// <summary>
/// شناسه سخت‌افزارهای موبایل که با «حالت سرویس» (service mode) قابل خواندن/نوشتن هستند.
///
/// نکته اساسی: IMEI در حافظه NV بیس‌باند (BB) است، نه در فایل‌سیستم اندروید و نه در iOS.
/// ابزارهایی مثل SP Flash Tool / Maui Meta / QPST همان ابزاری هستند که تعمیرکارها استفاده می‌کنند
/// و مستقیم روی BB می‌نویسند. این کلاس فقط پروفایل دستگاه و دستورهای آماده‌سازی را می‌سازد؛
/// اجرای نوشتن روی گوشی همیشه از طریق همان ابزار رسمی سازنده انجام می‌شود، نه با AT سیم‌کارت.
/// </summary>
internal enum PhoneVendor
{
    MediaTek,
    Qualcomm,
    Unisoc,
    HiSilicon,
    Apple,
    Unknown
}

internal enum DeviceAccessKind
{
    /// <summary>پورت AT روی مودم/ماژول: خواندن و نوشتن با فرمان AT.</summary>
    AtPort,

    /// <summary>پورت DIAG/امنیتی (QC Diag, MTK Com, BROM): نوشتن NV با ابزار سازنده.</summary>
    ServicePort,

    /// <summary>حالت بوت‌لودر / فست‌بوت.</summary>
    Bootloader,

    /// <summary>یواس‌بی عادی (MTP/PTP): فقط خواندن اطلاعات سطح سیستم.</summary>
    UserUsb,

    /// <summary>هیچ پورتی دیده نمی‌شود.</summary>
    None
}

internal sealed record PhoneIdentity(
    PhoneVendor Vendor,
    string Model,
    string Serial,
    string Imei,
    string Imei2,
    string Eid,
    string AndroidVersion,
    string Baseband,
    bool BootloaderUnlocked,
    bool Rooted,
    bool SecureBoot);

internal sealed record AccessTool(
    string Vendor,
    string Name,
    string Purpose,
    string Execution,
    string Preconditions,
    string Risk);

internal sealed record PhoneAccessPlan(
    PhoneIdentity Identity,
    DeviceAccessKind Detected,
    IReadOnlyList<string> DetectionTrail,
    IReadOnlyList<AccessTool> Tools,
    IReadOnlyList<string> WriteSteps,
    IReadOnlyList<string> VerifySteps,
    string Verdict,
    string IosStatement)
{
    public bool WritableOnDevice => Identity.Vendor is PhoneVendor.MediaTek or PhoneVendor.Qualcomm
        or PhoneVendor.Unisoc;

    public string ToText()
    {
        var text = new StringBuilder();
        text.AppendLine("پروفایل دسترسی به هویت روی خود گوشی");
        text.AppendLine(new string('─', 64));
        text.AppendLine($"سازنده/چیپست: {Identity.Vendor}");
        text.AppendLine($"مدل: {Show(Identity.Model)}   سریال/UDID: {Show(Identity.Serial)}");
        text.AppendLine($"IMEI فعلی: {Show(Identity.Imei)}   IMEI2: {Show(Identity.Imei2)}");
        if (Identity.Eid.Length > 0) text.AppendLine($"EID (eSIM): {Identity.Eid}");
        text.AppendLine($"اندروید: {Show(Identity.AndroidVersion)}   بیس‌باند: {Show(Identity.Baseband)}");
        text.AppendLine($"بوت‌لودر باز: {Yes(Identity.BootloaderUnlocked)}   روت: {Yes(Identity.Rooted)}   Secure Boot: {Yes(Identity.SecureBoot)}");
        text.AppendLine($"نوع دسترسی شناسایی‌شده: {Detected}");
        text.AppendLine();
        text.AppendLine("رد شناسایی:");
        foreach (var line in DetectionTrail) text.AppendLine("  • " + line);
        text.AppendLine();
        text.AppendLine("ابزار و نحوه اجرا:");
        foreach (var tool in Tools)
        {
            text.AppendLine($"  [{tool.Vendor}] {tool.Name} — {tool.Purpose}");
            text.AppendLine($"      اجرا: {tool.Execution}");
            text.AppendLine($"      پیش‌نیاز: {tool.Preconditions}");
            text.AppendLine($"      ریسک: {tool.Risk}");
        }
        text.AppendLine();
        text.AppendLine("ترتیب کار روی گوشی:");
        var index = 1;
        foreach (var step in WriteSteps) text.AppendLine($"  {index++}) {step}");
        text.AppendLine();
        text.AppendLine("تأیید بعد از نوشتن:");
        index = 1;
        foreach (var step in VerifySteps) text.AppendLine($"  {index++}) {step}");
        text.AppendLine();
        text.AppendLine("حکم:");
        text.AppendLine(Verdict);
        if (IosStatement.Length > 0)
        {
            text.AppendLine();
            text.AppendLine("درباره iOS:");
            text.AppendLine(IosStatement);
        }
        return text.ToString();
    }

    private static string Show(string value) => string.IsNullOrWhiteSpace(value) ? "—" : value;
    private static string Yes(bool value) => value ? "بله" : "خیر";

    public static PhoneAccessPlan Build(PhoneIdentity identity, DeviceAccessKind detected, IEnumerable<string> trail)
    {
        var tools = new List<AccessTool>();
        var writeSteps = new List<string>();
        var verifySteps = new List<string>();
        var verdict = string.Empty;
        var ios = string.Empty;

        switch (identity.Vendor)
        {
            case PhoneVendor.MediaTek:
                tools.Add(new AccessTool("MediaTek", "SP Flash Tool / MTK Client",
                    "پشتیبان‌گیری و فلش پارتیشن nvram/nvdata و nvcfg که IMEI در آن‌هاست",
                    "گوشی خاموش → نگه‌داشتن کلید ولوم + اتصال USB (حالت BROM) → Read Back پارتیشن‌های NVRAM/NVDATA → ویرایش IMEI با Maui Meta یا SP Meta → Write Back",
                    "درایور VCOM نصب باشد؛ برای چیپست‌های جدید فایل auth (DA) لازم است",
                    "اشتباه در پارتیشن = گوشی بدون شبکه یا بریک؛ همیشه Read Back قبل از Write"));
                tools.Add(new AccessTool("MediaTek", "Maui Meta / SP Meta (META mode)",
                    "نوشتن IMEI در حالت META بدون باز کردن گوشی",
                    "گوشی روشن با کابل USB → انتخاب پورت COM META → تب IMEI → وارد کردن ۱۵ رقم → Write IMEI → Reboot",
                    "فقط روی چیپست‌هایی که META lock ندارند (اغلب MT67xx قدیمی‌تر)",
                    "روی چیپست‌های secure، ابزار خطای auth می‌دهد"));
                writeSteps.AddRange([
                    "خاموش کردن گوشی و برداشتن سیم‌کارت",
                    "نصب درایور VCOM و اتصال در حالت BROM",
                    "Read Back پارتیشن‌های NVRAM / NVDATA / NVCFG و ذخیره فایل پشتیبان",
                    "نوشتن IMEI قرض‌دهنده با Maui Meta یا Write Back پارتیشن ویرایش‌شده",
                    "Reboot و انتظار برای ثبت شبکه"
                ]);
                break;

            case PhoneVendor.Qualcomm:
                tools.Add(new AccessTool("Qualcomm", "QPST / QXDM / QFIL",
                    "دسترسی به NV item 550 (IMEI) و رستور QCN",
                    "فعال‌سازی پورت DIAG (روت + setprop یا کد کارخانه) → QPST Software Download → Backup QCN → QXDM NV Browser → NV 550 → نوشتن IMEI → QCN Restore در صورت خرابی",
                    "پورت DIAG فعال؛ روی SDM845 و بالاتر امضای امنیتی لازم است",
                    "بدون پشتیبان QCN بازیابی سخت است؛ IMEI دو سیم در NV 550 و 550+ است"));
                writeSteps.AddRange([
                    "فعال‌سازی پورت DIAG (روت یا منوی مهندسی)",
                    "پشتیبان‌گیری کامل QCN با QPST",
                    "باز کردن NV item 550 در QXDM و نوشتن IMEI قرض‌دهنده",
                    "برای سیم دوم: NV item متناظر همان اسلات",
                    "ری‌استارت و بررسی ثبت شبکه"
                ]);
                break;

            case PhoneVendor.Unisoc:
                tools.Add(new AccessTool("Unisoc/SPD", "ResearchDownload / Uni-AS + PAC",
                    "فلش پارتیشن‌های NV و نوشتن IMEI در حالت FDL",
                    "گوشی خاموش → حالت FDL با کلید ولوم → انتخاب PAC همان مدل → Write NV → Reboot",
                    "PAC دقیقاً مربوط به همان مدل و نسخه باشد",
                    "PAC اشتباه = بریک کامل"));
                writeSteps.AddRange([
                    "خاموش کردن و برداشتن سیم‌کارت",
                    "اتصال در حالت FDL و شناسایی با ResearchDownload",
                    "پشتیبان‌گیری پارتیشن NV",
                    "نوشتن IMEI با ابزار Unisoc",
                    "Reboot و بررسی"
                ]);
                break;

            case PhoneVendor.HiSilicon:
                verdict = "پلتفرم HiSilicon در حالت عادی امکان نوشتن IMEI با ابزار عمومی ندارد؛ فقط ابزار سطح کارخانه سازنده.";
                break;

            case PhoneVendor.Apple:
                ios = "IMEI در آیفون داخل NV امضاشده بیس‌باند و گره‌خورده به سریال است. جیلبریک فقط لایه iOS را عوض می‌کند و به بیس‌باند نمی‌رسد؛ هر تلاش بازنویسی، فعال‌سازی (Activation) را می‌شکند. روی آیفون نوشتن IMEI با هیچ ابزار عمومی ممکن نیست.";
                verdict = "روی آیفون مسیر نوشتن IMEI بسته است. تنها راه‌های واقعی: ثبت همان IMEI در همتا، یا استفاده از گوشی دیگری که IMEIاش ثبت است.";
                break;

            default:
                verdict = "چیپست شناسایی نشد. با دستورهای تشخیص در همین تب (ATI/AT+CGMM یا ADB) مدل را مشخص کن تا ابزار درست انتخاب شود.";
                break;
        }

        if (identity.Vendor is PhoneVendor.MediaTek or PhoneVendor.Qualcomm or PhoneVendor.Unisoc)
        {
            if (identity.SecureBoot)
                verdict = "Secure Boot فعال است؛ ابزار عمومی معمولاً auth لازم دارد. اگر ابزار خطای authorization داد، مسیر نوشتن روی همین دستگاه بسته است.";
            else if (!identity.BootloaderUnlocked && identity.Vendor == PhoneVendor.Qualcomm)
                verdict = "بوت‌لودر قفل است؛ برای Qualcomm معمولاً باید پورت DIAG با روت باز شود، وگرنه QPST دسترسی نمی‌دهد.";

            verifySteps.AddRange([
                "*#06# را در گوشی بگیر و تطابق IMEI را ببین",
                "ری‌استارت و دوباره *#06# — مقدار باید بماند",
                "سیم‌کارت را بگذار و ثبت شبکه را بررسی کن (آنتن/سرویس)",
                "IMEI را با مقدار قرض‌دهنده در دفتر IMEI مقایسه کن"
            ]);
        }

        if (detected is DeviceAccessKind.UserUsb or DeviceAccessKind.None && identity.Vendor is not PhoneVendor.Apple)
            verdict = string.IsNullOrEmpty(verdict)
                ? "فقط یواس‌بی عادی دیده می‌شود؛ برای نوشتن باید در حالت سرویس (BROM/META/DIAG/FDL) وصل شود."
                : verdict + " اکنون فقط یواس‌بی عادی دیده می‌شود؛ برای اجرا باید حالت سرویس فعال شود.";

        return new PhoneAccessPlan(identity, detected, trail.ToList(), tools, writeSteps, verifySteps, verdict, ios);
    }
}
