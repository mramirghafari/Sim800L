using System.Text.RegularExpressions;

namespace SIM800L_Flasher;

internal static class AtGuard
{
    /// <summary>
    /// فرمان‌هایی که به‌طور پیش‌فرض مسدود می‌مانند: نوشتن خام NV و دستکاری نامشخص حافظه.
    /// فرمان‌های شناخته‌شده نوشتن IMEI از این فهرست خارج شده‌اند و از مسیر کنترل‌شده <see cref="ImeiCommandPolicy"/> عبور می‌کنند.
    /// </summary>
    private static readonly string[] BlockedTokens =
    [
        "WRITEIMEI", "NVWRITE", "NV_WRITE", "GPRS/IMEI", "AT+NVCLEAR", "NVDEFAULT", "FSNV", "QCN", "DIAG_WRITE"
    ];

    /// <summary>نوشتن IMEI فقط وقتی مجاز است که کاربر آن را در رابط برنامه فعال کرده باشد.</summary>
    public static bool ImeiWriteUnlocked { get; set; }

    public static bool IsAllowed(string? command, out string reason)
    {
        reason = string.Empty;
        if (string.IsNullOrWhiteSpace(command))
        {
            reason = "فرمان خالی است.";
            return false;
        }

        var normalized = Regex.Replace(command.Trim().ToUpperInvariant(), @"\s+", string.Empty);
        if (!normalized.StartsWith("AT", StringComparison.Ordinal))
        {
            reason = "فرمان باید با AT شروع شود.";
            return false;
        }

        if (BlockedTokens.Any(normalized.Contains))
        {
            reason = "این فرمان نوشتن خام NV یا حافظه است و در این برنامه غیرفعال است.";
            return false;
        }

        if (ImeiCommandPolicy.IsImeiMutation(normalized) && !ImeiWriteUnlocked)
        {
            reason = "نوشتن IMEI قفل است. برای فعال‌سازی، در تب «نوشتن IMEI» گزینه تأیید و باز کردن قفل را انتخاب کنید.";
            return false;
        }

        return true;
    }
}

/// <summary>
/// دسته‌بندی فرمان‌های مربوط به IMEI. خواندن همیشه مجاز است؛ نوشتن فقط در حالت «نوشتن» و با تأیید صریح.
/// </summary>
internal static class ImeiCommandPolicy
{
    private static readonly Regex Mutation = new(
        @"^(AT\+EGMR=1,7,|AT\+SIMEI=|AT\+QCFG=""IMEI"",|AT\+QIMEI=)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex Read = new(
        @"^(AT\+GSN|AT\+CGSN|AT\+SIMEI\?|AT\+EGMR=0,7|AT\+EGMR=2,7|AT\+CSN)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static bool IsImeiMutation(string normalizedCommand) =>
        Mutation.IsMatch(normalizedCommand.Replace(" ", string.Empty));

    public static bool IsImeiRead(string normalizedCommand) =>
        Read.IsMatch(normalizedCommand.Replace(" ", string.Empty));

    public static bool ContainsImei(string command) =>
        IsImeiMutation(Regex.Replace(command, @"\s+", string.Empty)) ||
        IsImeiRead(Regex.Replace(command, @"\s+", string.Empty));
}
