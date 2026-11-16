using System.Diagnostics;
using System.Text;

namespace SIM800L_Flasher;

/// <summary>
/// تشخیص خواندنی گوشی اندروید با ADB. فقط خواندن — هیچ فرمان نوشتنی روی گوشی اجرا نمی‌شود.
/// با این اطلاعات، ابزار درست (MTK/QCOM/Unisoc) انتخاب می‌شود و می‌شود فهمید گوشی در حالت
/// سرویس است یا فقط یواس‌بی عادی.
/// </summary>
internal static class PhoneDetector
{
    public sealed record DetectResult(PhoneIdentity? Identity, IReadOnlyList<string> Trail, string Error);

    public static DetectResult Detect(string adbPath = "adb", int timeoutMs = 6000)
    {
        var trail = new List<string>();
        string Run(string arguments)
        {
            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = adbPath,
                        Arguments = arguments,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                if (!process.Start()) return string.Empty;
                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();
                if (!process.WaitForExit(timeoutMs))
                {
                    try { process.Kill(true); } catch { }
                    return string.Empty;
                }
                var text = (output + "\n" + error).Trim();
                trail.Add($"adb {arguments} → {(text.Length == 0 ? "(خالی)" : Shorten(text))}");
                return text;
            }
            catch (Exception ex)
            {
                trail.Add($"adb {arguments} → خطا: {ex.Message}");
                return string.Empty;
            }
        }

        var devices = Run("devices -l");
        if (devices.Length == 0 || !devices.Contains("device ", StringComparison.Ordinal))
            return new DetectResult(null, trail, "گوشی با ADB دیده نشد. USB Debugging را روشن کن، کابل را وصل کن و اجازه Debugging را در گوشی بده.");

        var model = Run("shell getprop ro.product.model").Split('\n').FirstOrDefault()?.Trim() ?? string.Empty;
        var manufacturer = Run("shell getprop ro.product.manufacturer").Split('\n').FirstOrDefault()?.Trim() ?? string.Empty;
        var serial = Run("shell getprop ro.serialno").Split('\n').FirstOrDefault()?.Trim() ?? string.Empty;
        var platform = Run("shell getprop ro.board.platform").Split('\n').FirstOrDefault()?.Trim() ?? string.Empty;
        var hardware = Run("shell getprop ro.hardware").Split('\n').FirstOrDefault()?.Trim() ?? string.Empty;
        var android = Run("shell getprop ro.build.version.release").Split('\n').FirstOrDefault()?.Trim() ?? string.Empty;
        var baseband = Run("shell getprop gsm.version.baseband").Split('\n').FirstOrDefault()?.Trim() ?? string.Empty;
        var bootState = Run("shell getprop ro.boot.verifiedbootstate").Split('\n').FirstOrDefault()?.Trim() ?? string.Empty;
        var flashLocked = Run("shell getprop ro.boot.flash.locked").Split('\n').FirstOrDefault()?.Trim() ?? string.Empty;
        var rootTest = Run("shell su -c id");

        var vendor = GuessVendor(platform + " " + hardware + " " + baseband);
        trail.Add($"تشخیص: platform={Show(platform)} hardware={Show(hardware)} → {vendor}");
        // IMEI و EID را ADB معمولاً برنمی‌گرداند (Android 10+ و iOS)؛ کاربر از *#06# وارد می‌کند.
        var imeiFromAdb = Run("shell service call iphonesubinfo 1");
        trail.Add("IMEI از ADB معمولاً مسدود است؛ مقدار را از *#06# وارد کن.");

        var identity = new PhoneIdentity(
            vendor,
            string.Join(' ', new[] { manufacturer, model }.Where(p => p.Length > 0)),
            serial,
            LooksLikeImei(imeiFromAdb) ? imeiFromAdb.Trim() : string.Empty,
            string.Empty,
            string.Empty,
            android,
            baseband,
            BootloaderUnlocked: flashLocked == "0" || bootState.Contains("orange", StringComparison.OrdinalIgnoreCase),
            Rooted: rootTest.Contains("uid=0", StringComparison.Ordinal),
            SecureBoot: bootState.Contains("green", StringComparison.OrdinalIgnoreCase) || flashLocked == "1");

        return new DetectResult(identity, trail, string.Empty);
    }

    private static PhoneVendor GuessVendor(string fingerprint)
    {
        var text = fingerprint.ToLowerInvariant();
        if (text.Contains("mt") || text.Contains("mediatek") || text.Contains("helio"))
            return PhoneVendor.MediaTek;
        if (text.Contains("sm") || text.Contains("msm") || text.Contains("qcom") || text.Contains("qualcomm") ||
            text.Contains("snapdragon") || text.Contains("sdm"))
            return PhoneVendor.Qualcomm;
        if (text.Contains("unisoc") || text.Contains("spreadtrum") || text.Contains("ums") || text.Contains("sc98"))
            return PhoneVendor.Unisoc;
        if (text.Contains("kirin") || text.Contains("hi3") || text.Contains("hisilicon"))
            return PhoneVendor.HiSilicon;
        return PhoneVendor.Unknown;
    }

    private static bool LooksLikeImei(string text) =>
        text.Length >= 15 && text.Count(char.IsAsciiDigit) >= 15;

    private static string Show(string value) => string.IsNullOrWhiteSpace(value) ? "—" : value;

    private static string Shorten(string value)
    {
        var flat = string.Join(" ", value.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return flat.Length <= 90 ? flat : flat[..90] + "…";
    }

    /// <summary>ورودی فارسی/عربی را به رقم لاتین تبدیل می‌کند.</summary>
    public static string NormalizeDigits(string input)
    {
        var map = new Dictionary<char, char>
        {
            ['۰'] = '0', ['۱'] = '1', ['۲'] = '2', ['۳'] = '3', ['۴'] = '4',
            ['۵'] = '5', ['۶'] = '6', ['۷'] = '7', ['۸'] = '8', ['۹'] = '9'
        };
        var digits = new StringBuilder();
        foreach (var ch in input)
            if (char.IsDigit(ch)) digits.Append(char.IsAsciiDigit(ch) ? ch : map.GetValueOrDefault(ch, ch));
        return digits.ToString();
    }
}
