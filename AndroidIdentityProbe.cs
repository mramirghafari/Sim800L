using System.Text.RegularExpressions;

namespace SIM800L_Flasher;

/// <summary>
/// خواندن IMEI اندروید از خودِ گوشی، بدون روت.
///
/// واقعیت فنی: از Android 10 شناسه‌های غیرقابل‌تغییر (IMEI/MEID/سریال) پشت
/// <c>READ_PRIVILEGED_PHONE_STATE</c> هستند و ADB معمولی آن را ندارد. اما سه راه باقی می‌ماند:
///
/// ۱) <b>سرویس iphonesubinfo</b> از طریق binder — در بسیاری از دستگاه‌ها تا Android 13
///    پاسخ می‌دهد؛ اینجا چند تراکنش رایج امتحان می‌شود.
/// ۲) <b>پارتیشن‌های modem</b> با روت یا <c>su</c> (EFS/radio) — اگر روت باشد، مستقیم خوانده می‌شود.
/// ۳) <b>پورت مودم/DIAG</b> روی همان گوشی (کلاس <see cref="ModemIdentityProbe"/>) — این مسیر
///    مستقل از نسخه اندروید است.
///
/// هیچ فرمان نوشتنی اجرا نمی‌شود. مقدار خوانده‌شده همیشه با Luhn بررسی می‌شود تا پاسخ
/// تکراری/هگزادسیمال سرویس، به‌عنوان IMEI گزارش نشود.
/// </summary>
internal sealed record AndroidBinderProbeResult(
    string Imei1,
    string Imei2,
    IReadOnlyList<string> Trail,
    string Verdict)
{
    public bool FoundAny => Imei1.Length == 15 || Imei2.Length == 15;
}

internal static class AndroidIdentityProbe
{
    /// <summary>تراکنش‌های شناخته‌شده iphonesubinfo در نسخه‌های مختلف اندروید.</summary>
    public static readonly (string Transaction, string Meaning)[] KnownTransactions =
    [
        ("1", "getDeviceId (IMEI)"),
        ("2", "getDeviceIdForSubscriber"),
        ("3", "getImeiForSubscriber — Android 8"),
        ("4", "getDeviceSvn"),
        ("5", "getSubscriberId (IMSI)"),
        ("6", "getIccSerialNumber")
    ];

    public static async Task<AndroidBinderProbeResult> ProbeAsync(
        string adbPath,
        string serial,
        Func<string, IReadOnlyList<string>, CancellationToken, Task<MobileToolResult>> run,
        CancellationToken token = default)
    {
        var trail = new List<string>();
        var found = new List<string>();

        // مسیر ۱: سرویس binder
        foreach (var (transaction, meaning) in KnownTransactions)
        {
            token.ThrowIfCancellationRequested();
            var result = await run(adbPath, ["-s", serial, "shell", "service", "call", "iphonesubinfo", transaction], token);
            var decoded = result.ExitCode == 0 ? DecodeHexDump(result.Output) : string.Empty;
            trail.Add($"iphonesubinfo {transaction} ({meaning}) → {(decoded.Length > 0 ? decoded : "خالی/مسدود")}");
            if (decoded.Length == 15 && !found.Contains(decoded)) found.Add(decoded);
            if (found.Count >= 2) break;
        }

        // مسیر ۲: روت و پارتیشن‌های مودم
        if (found.Count == 0)
        {
            var root = await run(adbPath, ["-s", serial, "shell", "su", "-c", "id"], token);
            if (root.ExitCode == 0 && root.Output.Contains("uid=0", StringComparison.Ordinal))
            {
                trail.Add("روت در دسترس است؛ خواندن از پارتیشن مودم…");
                var props = await run(adbPath, ["-s", serial, "shell", "su", "-c",
                    "getprop | grep -i -E 'ril\\.imei|persist.radio.imei|gsm.sim.imei'"], token);
                foreach (var match in Regex.Matches(props.Output, @"(\d{15})").Cast<Match>())
                {
                    var candidate = match.Value;
                    if (ImeiNumber.MatchesLuhn(candidate) && !found.Contains(candidate)) found.Add(candidate);
                }
                trail.Add($"prop مودم → {(found.Count > 0 ? string.Join(", ", found) : "چیزی پیدا نشد")}");
            }
            else
            {
                trail.Add("روت نیست؛ مسیر پارتیشن مودم در دسترس نیست.");
            }
        }

        var verdict = found.Count switch
        {
            >= 2 => "دو شناسه معتبر با Luhn از خود گوشی خوانده شد (IMEI و IMEI2).",
            1 => "یک شناسه معتبر از خود گوشی خوانده شد.",
            _ => "ADB از نسخه اندروید ۱۰ به بعد IMEI را مسدود می‌کند. از پورت مودم استفاده کن: " +
                 "روی سامسونگ *#0808# → DM+MODEM+ADB، روی سایر برندها حالت DIAG یا AT."
        };

        return new AndroidBinderProbeResult(
            found.Count > 0 ? found[0] : string.Empty,
            found.Count > 1 ? found[1] : string.Empty,
            trail,
            verdict);
    }

    /// <summary>
    /// خروجی <c>service call</c> به‌شکل هگز دامپ است و ویندوز/ابزار آن را کلمه‌به‌کلمه چاپ می‌کند:
    /// <c>Result: Parcel(00000000 0000000f 00340039 00300031 ...)</c>
    /// هر کلمه ۴ بایت است و رشته UTF-16 **big-endian** در آن قرار می‌گیرد (00 34 = '4').
    /// بعضی نسخه‌ها little-endian می‌دهند، پس هر دو تفسیر امتحان و فقط عدد ۱۵ رقمی
    /// با Luhn معتبر برگردانده می‌شود؛ وگرنه خروجی خالی است تا عدد اشتباه گزارش نشود.
    /// </summary>
    internal static string DecodeHexDump(string output)
    {
        if (string.IsNullOrWhiteSpace(output)) return string.Empty;
        var words = Regex.Matches(output, @"\b[0-9a-fA-F]{8}\b").Select(m => m.Value).ToArray();

        foreach (var decoder in new Func<string, string>[] { DecodeBigEndian, DecodeLittleEndian })
        {
            var candidate = new string(words.Select(decoder).Aggregate(new System.Text.StringBuilder(),
                (builder, part) => builder.Append(part)).ToString().Where(char.IsAsciiDigit).ToArray());
            if (candidate.Length == 15 && ImeiNumber.MatchesLuhn(candidate)) return candidate;
        }

        // بعضی نسخه‌ها عدد را به‌صورت عدد صحیح ۶۴ بیتی در انتهای خروجی می‌فرستند.
        var digits = Regex.Match(output, @"\b(\d{15})\b");
        return digits.Success && ImeiNumber.MatchesLuhn(digits.Groups[1].Value) ? digits.Groups[1].Value : string.Empty;
    }

    private static string DecodeBigEndian(string word)
    {
        var text = new System.Text.StringBuilder();
        for (var i = 0; i + 3 < word.Length; i += 4)
            AppendIfCharacter(word.AsSpan(i, 4), text, highFirst: true);
        return text.ToString();
    }

    private static string DecodeLittleEndian(string word)
    {
        var text = new System.Text.StringBuilder();
        for (var i = 0; i + 3 < word.Length; i += 4)
            AppendIfCharacter(word.AsSpan(i, 4), text, highFirst: false);
        return text.ToString();
    }

    private static void AppendIfCharacter(ReadOnlySpan<char> half, System.Text.StringBuilder text, bool highFirst)
    {
        if (!int.TryParse(half, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out var value)) return;
        var high = (value >> 8) & 0xFF;
        var low = value & 0xFF;
        var code = highFirst ? ((high == 0 ? low : 0)) : ((low == 0 ? high : 0));
        if (code is >= 0x20 and < 0x7F) text.Append((char)code);
    }
}
