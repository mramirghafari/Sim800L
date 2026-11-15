using System.Globalization;
using System.Text.RegularExpressions;

namespace SIM800L_Flasher;

/// <summary>
/// بررسی ساختاری IMEI (۱۵ رقم، بدون TAC جعلی، با رقم کنترلی درست بر اساس الگوریتم Luhn).
/// این کلاس فقط «اعتبار ساختاری» را می‌سنجد و هیچ ارتباطی با فلش یا نوشتن حافظه ندارد.
/// </summary>
internal sealed record ImeiNumber(string Value, string Tac, string Serial, int CheckDigit)
{
    private static readonly Regex NonDigit = new(@"\D", RegexOptions.Compiled);
    private static readonly Regex DigitOnly = new(@"^\d+$", RegexOptions.Compiled);

    public static bool TryParse(string? input, out ImeiNumber? imei, out string error)
    {
        imei = null;

        if (string.IsNullOrWhiteSpace(input))
        {
            error = "شناسه IMEI خالی است؛ ۱۵ رقم وارد کنید.";
            return false;
        }

        var trimmed = input.Trim();
        if (!DigitOnly.IsMatch(trimmed))
        {
            error = "IMEI فقط می‌تواند شامل رقم باشد (بدون حرف، فاصله یا خط تیره).";
            return false;
        }

        if (trimmed.Length != 15)
        {
            error = $"طول IMEI باید ۱۵ رقم باشد؛ شما {trimmed.Length} رقم وارد کرده‌اید.";
            return false;
        }

        if (!trimmed.Any(c => c != '0'))
        {
            error = "همه ارقام صفر است؛ IMEI معتبر نیست.";
            return false;
        }

        if (!MatchesLuhn(trimmed))
        {
            error = "رقم کنترلی (رقم پانزدهم) با الگوریتم Luhn سازگار نیست؛ IMEI اشتباه تایپ شده است.";
            return false;
        }

        if (trimmed.StartsWith("00000000", StringComparison.Ordinal) ||
            trimmed.StartsWith("99999999", StringComparison.Ordinal))
        {
            error = "پیشوند IMEI متعلق به TAC ثبت‌شده واقعی نیست.";
            return false;
        }

        imei = new ImeiNumber(trimmed, trimmed[..8], trimmed[8..14], trimmed[14] - '0');
        error = string.Empty;
        return true;
    }

    /// <summary>الگوریتم Luhn؛ برای IMEI ارقام با اندیس فرد (از چپ، ۰-پایه) دو برابر می‌شوند.</summary>
    public static bool MatchesLuhn(string digits)
    {
        var sum = 0;
        var length = digits.Length;
        for (var i = 0; i < length; i++)
        {
            var digit = digits[i] - '0';
            if (i % 2 == 1)
            {
                digit *= 2;
                if (digit > 9) digit -= 9;
            }
            sum += digit;
        }
        return sum % 10 == 0;
    }

    /// <summary>ساخت یک IMEI نمایشی/آزمایشی با رقم کنترلی درست، برای تست خودکار واحد.</summary>
    public static string ComputeCheckDigit(string first14)
    {
        var body = NonDigit.Replace(first14, string.Empty);
        if (body.Length < 14) throw new ArgumentException("حداقل ۱۴ رقم لازم است.", nameof(first14));
        body = body[..14];

        for (var check = 0; check <= 9; check++)
        {
            var candidate = body + check.ToString(CultureInfo.InvariantCulture);
            if (MatchesLuhn(candidate)) return candidate;
        }
        throw new InvalidOperationException("محاسبه رقم کنترلی ناموفق بود.");
    }

    public string TacFormatted => $"{Tac[..2]}-{Tac[2..]}";
    public string Masked => Value[..6] + new string('*', 6) + Value[^1];
}
