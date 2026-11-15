using System.Text;
using System.Text.RegularExpressions;

namespace SIM800L_Flasher;

internal enum PlanIssueKind { Blocker, Unsupported, Warning, Info }

internal sealed record PlanIssue(PlanIssueKind Kind, string Message);

/// <summary>
/// اجزای یک عملیات فلش که از RAM/حافظه ماژول خوانده می‌شوند. این ساختار فقط «تنظیمات و دستورالعمل» را نگه می‌دارد؛
/// هیچ شناسه یا دستور ساختگی داخل آن ساخته نمی‌شود.
/// </summary>
internal sealed record BackupComponent(string Name, string Source, string ReadCommand, string Reason);

/// <summary>
/// برنامه فلش. مهم‌ترین کاری که این کلاس انجام می‌دهد ساختن یک «فهرست بررسی» صادقانه است:
/// هر چه که روی مدل SIM800L امکان‌پذیر نباشد به‌صورت Unsupported و هر خطر واقعی به‌صورت Blocker گزارش می‌شود به‌جای ارسال فرمان بی‌اثر.
/// </summary>
internal sealed record FlashPlan
{
    private static readonly Regex TacPattern = new(@"^\d{8}$", RegexOptions.Compiled);

    public ImeiNumber? TargetImei { get; init; }
    public FirmwarePackage? Firmware { get; init; }
    public string FirmwareVariant { get; init; } = "SIM800L 2/15";
    public bool ModuleRespondsToAt { get; init; }
    public bool HasValidBackup { get; init; }
    public bool ConsentGiven { get; init; }
    public string Notes { get; init; } = string.Empty;

    /// <summary>آنچه واقعاً در سخت‌افزار فیزیکی نوشته می‌شود در این پروژه یا خالی است یا فقط در سند برنامه.</summary>
    public bool WritesPhysicalIdentity => false;

    public IReadOnlyList<BackupComponent> BackupChecklist() =>
    [
        new("Backup کامل فلش", "External programmer / test point", "—",
            "SIM800L معمولاً در حالت boot، کنترلر OTROMA قابل خواندن است."),
        new("دامنه فرکانس", "AT+BAND?", "@BAND",
            "باندهای فعال و RAT پیش از تغییر تنظیمات ثبت شود."),
        new("پیکربندی شبکه", "AT+CFUN?", "CFUN",
            "وضعیت عملکرد رادیو (۴ یا ۱) برای بازگردانی پس از تغییر."),
        new("تغییرات باگ‌لار", "AT+USBCFG?", "USBCFG",
            "پروتکل دانلود و USB mode فقط در صورت وجود مستند ثبت شود."),
        new("پارامترهای اختصاصی", "AT+CIPHEAD?", "CIP",
            "تنظیمات TCP/IP قبل از هر بازبرنامه‌ریزی."),
        new("سطح سیگنال و ثبت", "AT+CSQ / AT+CREG?", "CSQ",
            "برای بررسی سلامت ماژول پس از فلش.")
    ];

    public IReadOnlyList<PlanIssue> Evaluate()
    {
        var issues = new List<PlanIssue>();

        if (string.IsNullOrWhiteSpace(FirmwareVariant))
            issues.Add(new(PlanIssueKind.Blocker, "نوع دقیق برد (مثلاً SIM800L 2/15 یا V2.1) مشخص نیست."));
        if (!ModuleRespondsToAt)
            issues.Add(new(PlanIssueKind.Blocker, "ماژول به AT پاسخ نمی‌دهد؛ عملیات متوقف می‌شود."));
        if (!ConsentGiven)
            issues.Add(new(PlanIssueKind.Blocker, "چک‌باکس تأیید مسئولیت و مالکیت دستگاه انتخاب نشده است."));
        if (Firmware is null)
            issues.Add(new(PlanIssueKind.Blocker, "فایل firmware انتخاب و بررسی نشده است."));

        if (TargetImei is not null && !TacPattern.IsMatch(TargetImei.Tac))
            issues.Add(new(PlanIssueKind.Blocker, "پیشوند TAC باید ۸ رقم باشد."));
        else if (TargetImei is not null)
            issues.Add(new(PlanIssueKind.Info, $"TAC به‌صورت ساختاری قابل قبول است ({TargetImei.TacFormatted}) اما در GSMA ثبت نشده است؛ مالکیت قانونی آن بررسی شود."));

        if (TargetImei is not null)
        {
            issues.Add(new(PlanIssueKind.Unsupported,
                "SIM800L از IMEI بوت‌استرپ در حافظه امن/فلش‌بوت کنترلر OTROMA استفاده می‌کند و هویت را از طریق فرمان AT یا فلش به راحتی تغییر نمی‌دهد. نوشتن IMEI مالکیت‌دار در حافظه NV این تراشه در این برنامه پیاده‌سازی نشده است."));
        }

        if (Firmware is not null && Firmware.Size < 1024L * 1024)
        {
            issues.Add(new(PlanIssueKind.Warning,
                "فایل کمتر از ۱ مگابایت است؛ احتمال دارد بسته کامل firmware (شامل بوت‌لودر و map) نباشد."));
        }

        if (Firmware is not null && Firmware.Size > 32L * 1024 * 1024)
        {
            issues.Add(new(PlanIssueKind.Warning,
                "حجم فایل بسیار بیشتر از فضای معمول SIM800L است؛ احتمال نادرست بودن فایل یا flash map وجود دارد."));
        }

        if (!HasValidBackup)
            issues.Add(new(PlanIssueKind.Blocker, "بدون پشتیبان کامل برد، امکان بازگشت به حالت قبل وجود ندارد."));

        if (Firmware is not null)
            issues.Add(new(PlanIssueKind.Info, $"SHA-256: {Firmware.Sha256}"));

        if (!string.IsNullOrWhiteSpace(Notes))
            issues.Add(new(PlanIssueKind.Info, "یادداشت: " + Notes.Trim()));

        return issues;
    }

    public bool HasBlockers => Evaluate().Any(i => i.Kind == PlanIssueKind.Blocker);

    public string ToReport()
    {
        var builder = new StringBuilder();
        builder.AppendLine("SIM800L flash plan");
        builder.AppendLine(new string('-', 40));
        builder.AppendLine($"board       : {FirmwareVariant}");
        builder.AppendLine($"firmware    : {Firmware?.FullPath ?? "—"}");
        builder.AppendLine($"sha256      : {Firmware?.Sha256 ?? "—"}");
        builder.AppendLine($"imei target : {(TargetImei is null ? "—" : TargetImei.Masked)}");
        builder.AppendLine($"identity wr : {(WritesPhysicalIdentity ? "yes" : "no")}");
        builder.AppendLine("check list  :");
        foreach (var issue in Evaluate())
            builder.AppendLine($"  [{issue.Kind}] {issue.Message}");
        return builder.ToString();
    }
}
