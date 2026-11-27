using System.Text;

namespace SIM800L_Flasher;

/// <summary>
/// پشتیبان‌گیری پارتیشن‌های هویت (EFS/NVRAM/persist) قبل از هر تعمیر نرم‌افزاری.
///
/// این کار استاندارد تعمیرگاه است و دقیقاً جلوی همان اتفاقی را می‌گیرد که کاربر تجربه کرده:
/// بعد از فلش یا ریست، IMEI خالی می‌شود و دستگاه بی‌سیم می‌ماند. پشتیبان سالم یعنی همان
/// دستگاه می‌تواند هویت کارخانه خودش را برگرداند — بدون هیچ تغییر هویتی.
/// </summary>
internal sealed record IdentityBackupPlan(
    string DeviceModel,
    string DeviceSerial,
    IReadOnlyList<string> Commands,
    IReadOnlyList<string> Partitions,
    string RestoreHint,
    string Verdict)
{
    public string ToText()
    {
        var text = new StringBuilder();
        text.AppendLine("پشتیبان‌گیری هویت دستگاه (EFS / NVRAM)");
        text.AppendLine(new string('─', 60));
        text.AppendLine($"مدل: {DeviceModel}   سریال: {DeviceSerial}");
        text.AppendLine();
        text.AppendLine("پارتیشن‌های هدف:");
        foreach (var partition in Partitions) text.AppendLine("  • " + partition);
        text.AppendLine();
        text.AppendLine("فرمان‌های اجرا (با روت):");
        foreach (var command in Commands) text.AppendLine("  " + command);
        text.AppendLine();
        text.AppendLine("بازگردانی:");
        text.AppendLine(RestoreHint);
        text.AppendLine();
        text.AppendLine(Verdict);
        return text.ToString();
    }

    /// <summary>مسیرهای رایج EFS/NVRAM روی اندروید (Qualcomm، MediaTek و سامسونگ).</summary>
    public static readonly string[] CommonPartitions =
    [
        "/dev/block/bootdevice/by-name/modemst1",
        "/dev/block/bootdevice/by-name/modemst2",
        "/dev/block/bootdevice/by-name/fsg",
        "/dev/block/bootdevice/by-name/persist",
        "/dev/block/bootdevice/by-name/efs",
        "/dev/block/bootdevice/by-name/nvram",
        "/dev/block/bootdevice/by-name/nvdata",
        "/dev/block/bootdevice/by-name/protect1",
        "/dev/block/bootdevice/by-name/protect2"
    ];

    public static IdentityBackupPlan BuildForAndroid(string model, string serial)
    {
        var commands = CommonPartitions.Select((partition, index) =>
            $"dd if={partition} of={{OUT}}/backup-{index + 1:D2}-{partition.Split('/')[^1]}.img 2>/dev/null || true").ToList();
        commands.Add("getprop | grep -E 'gsm.version.baseband|ro.serialno|ril.serialnumber' > {OUT}/props.txt");
        commands.Add("ls -l /dev/block/bootdevice/by-name/ > {OUT}/partition-list.txt");

        return new IdentityBackupPlan(
            string.IsNullOrWhiteSpace(model) ? "اندروید" : model,
            serial,
            commands,
            CommonPartitions,
            "بازگردانی فقط با همان دستگاه و همان پارتیشن انجام می‌شود: " +
            "`dd if=<فایل پشتیبان> of=<همان پارتیشن>`. پارتیشن EFS/NVRAM دستگاه دیگر را روی این دستگاه ننویس؛ " +
            "هویت دو دستگاه قاطی می‌شود و هر دو بی‌سیم می‌مانند.",
            "پشتیبان با روت یا حالت سرویس (BROM/DIAG) گرفته می‌شود. اگر روت نیست، مسیر درست: SP Flash Tool Read Back " +
            "برای MTK یا QPST Backup QCN برای Qualcomm — همان ابزار رسمی سازنده، بدون تغییر محتوا.");
    }
}
