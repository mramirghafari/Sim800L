using System.Text;

namespace SIM800L_Flasher;

internal sealed record QuizAttempt(bool Correct, string Given, string Expected, int Remaining);

/// <summary>
/// «آزمون هویت» در سطح API دستگاه (مثل گوشی‌های سامسونگ که از نسخه ۱۱ اندروید، IMEI و سریال را
/// با یک رشته ۳۵ رقمی به فروشنده ارسال می‌کنند). در ماژول‌های SIMCom معادل آن AT+GSN/AT+CGSN است.
///
/// نکته مهم: این آزمون هویت را برای «سرویس/فروشنده» تأیید می‌کند، نه برای شبکه. همچنان BTS همان
/// IMEI سخت‌افزار را می‌بیند؛ ولی همان چیزی است که با *#06# و استعلام سامانه مقایسه می‌شود.
/// </summary>
internal sealed class DeviceQuiz
{
    private static readonly string[] Alphabet =
        "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz-_".Select(c => c.ToString()).ToArray();

    private readonly Sim800Device _device;

    public DeviceQuiz(Sim800Device device) => _device = device;

    /// <summary>ساخت رشته ۳۵ رقمی چالش: ۱۵ رقم تصادفی + ۲۰ رقم از IMEI.</summary>
    public static string BuildChallenge(string imei, Random? random = null)
    {
        random ??= Random.Shared;
        var digits = new string(imei.Where(char.IsDigit).ToArray());
        if (digits.Length != 15) throw new ArgumentException("IMEI باید ۱۵ رقم باشد.", nameof(imei));

        var challenge = new StringBuilder(35);
        for (var i = 0; i < 15; i++) challenge.Append(random.Next(0, 10));
        for (var i = 0; i < 20; i++) challenge.Append(digits[random.Next(0, 15)]);
        return challenge.ToString();
    }

    /// <summary>پاسخ مورد انتظار: هر رقم چالش با ارقام IMEI ترکیب و به base64-url نگاشت می‌شود.</summary>
    public static string ExpectedAnswer(string challenge, string imei)
    {
        var digits = new string(imei.Where(char.IsDigit).ToArray());
        var builder = new StringBuilder();
        for (var i = 0; i < challenge.Length; i++)
        {
            var c = challenge[i] - '0';
            var m = digits[i % digits.Length] - '0';
            builder.Append(Alphabet[(c + m) % Alphabet.Length]);
        }
        return builder.ToString();
    }

    public async Task<string> ReadImeiAsync(CancellationToken ct = default)
    {
        var value = ImeiWriter.ExtractImei(await _device.SendAsync("AT+GSN", 3000, ct));
        if (value.Length != 15) value = ImeiWriter.ExtractImei(await _device.SendAsync("AT+CGSN", 3000, ct));
        return value;
    }

    /// <summary>آزمون تعاملی؛ حداقل ۳ تلاش لازم است و با هر پاسخ غلط یکی کم می‌شود.</summary>
    public static QuizAttempt Evaluate(string given, string challenge, string imei, int attemptsLeft)
    {
        var expected = ExpectedAnswer(challenge, imei);
        var correct = string.Equals(given.Trim(), expected, StringComparison.Ordinal);
        return new QuizAttempt(correct, given.Trim(), expected, correct ? attemptsLeft : Math.Max(0, attemptsLeft - 1));
    }

    public static string Explain() =>
        "این آزمون همان چیزی است که گوشی‌های سامسونگ (اندروید ۱۱+) برای تأیید اصالت IMEI و سریال انجام می‌دهند: " +
        "یک رشته ۳۵ رقمی می‌گیرند و از دستگاه می‌خواهند با استفاده از IMEI خودش آن را حل کند. " +
        "در ماژول SIMCom، این پاسخ معادل خواندن AT+GSN است. اگر IMEI عوض شده باشد، پاسخ هم عوض می‌شود — " +
        "و همین راهی است که بدون بازکردن گوشی می‌توان فهمید هویت دستگاه همان است که ادعا می‌شود.";
}
