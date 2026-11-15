using System.Text.RegularExpressions;

namespace SIM800L_Flasher;

/// <summary>
/// پاک‌سازی لاگ‌ها پیش از ذخیره یا اشتراک‌گذاری: IMEI/IMSI/ICCID/شماره تلفن با ستاره جایگزین می‌شوند.
/// </summary>
internal static class SessionLogger
{
    private static readonly Regex IccidLine = new(@"(CCID[^\r\n]*?)(\d{14,20})", RegexOptions.Compiled);
    private static readonly Regex ImsiLine = new(@"(CIMI[^\r\n]*?)(\d{10,15})", RegexOptions.Compiled);
    private static readonly Regex LongDigitRun = new(@"\b\d{15}\b", RegexOptions.Compiled);
    private static readonly Regex PhoneRun = new(@"\+?\d{11,13}", RegexOptions.Compiled);

    public static string Redact(string? log)
    {
        if (string.IsNullOrEmpty(log)) return string.Empty;

        var result = IccidLine.Replace(log, m => m.Groups[1].Value + Mask(m.Groups[2].Value));
        result = ImsiLine.Replace(result, m => m.Groups[1].Value + Mask(m.Groups[2].Value));
        result = LongDigitRun.Replace(result, m => Mask(m.Value));
        result = PhoneRun.Replace(result, m => Mask(m.Value));
        return result;
    }

    private static string Mask(string digits)
    {
        if (digits.Length <= 6) return new string('*', digits.Length);
        return digits[..4] + new string('*', digits.Length - 6) + digits[^2..];
    }
}
