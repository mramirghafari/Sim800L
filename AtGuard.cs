using System.Text.RegularExpressions;

namespace SIM800L_Flasher;

internal static class AtGuard
{
    private static readonly string[] BlockedTokens =
    [
        "QIMEI", "EGMR", "SIMEI", "WIMEI", "CIMEI", "IMEI=", "WRITEIMEI", "NVWRITE", "GPRS/IMEI"
    ];

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
            reason = "نوشتن یا جعل IMEI و داده‌های NV در این برنامه غیرفعال است.";
            return false;
        }

        return true;
    }
}
