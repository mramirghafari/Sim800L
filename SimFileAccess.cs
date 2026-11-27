using System.Text;

namespace SIM800L_Flasher;

internal enum SimWriteAccess { ReadOk, UpdateOk, AdminRequired, NotFound, Rejected }

internal sealed record SimApduResult(string Command, int Sw1, int Sw2, string Data)
{
    public bool IsSuccess => Sw1 == 0x90 || (Sw1 == 0x91 && Sw2 == 0x00) || Sw1 == 0x92;

    public string StatusName => Sw1 switch
    {
        0x90 => "OK",
        0x91 => Sw2 == 0x00 ? "OK (proactive)" : "OK",
        0x92 => "OK (سرویس)",
        0x93 => "برنامه USIM مشغول است",
        0x94 => "فایل یا آدرس پیدا نشد",
        0x95 => "بررسی بایت‌ها ناموفق",
        0x98 => "کارت رد کرد (دسترسی/شرط امنیتی)",
        0x6A => "پارامتر نامعتبر یا آدرس بیرون از فایل",
        0x6D => "دستور پشتیبانی نمی‌شود",
        0x6E => "کلاس دستور پشتیبانی نمی‌شود",
        0x69 => Sw2 == 0x82 ? "شرط امنیتی برقرار نیست (ADM/PIN لازم است)" : "دسترسی مجاز نیست",
        0x67 => "طول داده اشتباه است",
        0x63 => "هشدار: تأیید PIN ناموفق",
        0x6F => "خطای عمومی",
        _ => $"وضعیت 0x{Sw1:X2}{Sw2:X2}"
    };
}

/// <summary>
/// دسترسی واقعی به سیستم فایل SIM/USIM از طریق AT+CRSM و AT+CSIM.
///
/// نکته استانداردی: در GSM (TS 51.011) محل ذخیره IMEI در کارت، فیلد IMEI در EF با شناسه
/// '6F7E' است و بر اساس استاندارد، ME باید IMEI خودش را در آن بنویسد؛ یعنی این فیلد
/// «کپی گزارش‌شده توسط گوشی» است، نه منبع هویت. در USIM نیز '6F7E' همان EF_LOCI است.
/// از این گذشته، کلاس دسترسی UPDATE برای این فایل‌ها معمولاً ADM است و فقط اپراتور کلید آن را دارد
/// (تأیید شده در مستندات و در بحث‌های مهندسی). به همین دلیل این کلاس اول «خواندن» و
/// «آزمون نوشتن» را انجام می‌دهد و نتیجه واقعی کارت را گزارش می‌کند، نه ادعا.
/// </summary>
internal sealed class SimFileAccess
{
    public const int EfLoci = 0x6F7E;
    public const int EfImsi = 0x6F07;
    public const int EfIccid = 0x2FE2;
    public const int EfAd = 0x6FAD;
    public const int EfSmsParams = 0x6F42;
    public const int EfServiceTable = 0x6F38;

    private readonly Sim800Device _device;

    public SimFileAccess(Sim800Device device) => _device = device;

    /// <summary>آیا ماژول دستور CRSM را می‌شناسد؟ (SIM800L با firmwareهای مختلف متفاوت است)</summary>
    public async Task<bool> IsCrsmSupportedAsync(CancellationToken ct = default)
    {
        var response = await _device.SendAsync("AT+CRSM=242", 3000, ct);
        return !response.Contains("ERROR", StringComparison.OrdinalIgnoreCase) &&
               response.Contains("+CRSM", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<bool> IsCsimSupportedAsync(CancellationToken ct = default)
    {
        var response = await _device.SendAsync("AT+CSIM=10,\"A0A40000023F00\"", 3000, ct);
        return !response.Contains("ERROR", StringComparison.OrdinalIgnoreCase) &&
               response.Contains("+CSIM", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>خواندن یک فایل مقدماتی (transparent) با AT+CRSM (READ BINARY = 176).</summary>
    public async Task<SimApduResult> ReadBinaryAsync(int fileId, int length, CancellationToken ct = default)
    {
        var command = $"AT+CRSM=176,{fileId},0,0,{length}";
        var response = await _device.SendAsync(command, 4000, ct);
        return ParseCrsm(command, response);
    }

    /// <summary>آزمون نوشتن با UPDATE BINARY (214) — بدون تغییر داده واقعی، همان بایت خوانده‌شده.</summary>
    public async Task<SimApduResult> ProbeUpdateBinaryAsync(int fileId, string hexData, CancellationToken ct = default)
    {
        var command = $"AT+CRSM=214,{fileId},0,0,{hexData.Length / 2},\"{hexData}\"";
        var response = await _device.SendAsync(command, 5000, ct);
        return ParseCrsm(command, response);
    }

    /// <summary>نوشتن واقعی (فقط برای فایل‌هایی که کارت اجازه بدهد؛ در عمل ADM لازم است).</summary>
    public async Task<SimApduResult> UpdateBinaryAsync(int fileId, string hexData, CancellationToken ct = default)
    {
        var command = $"AT+CRSM=214,{fileId},0,0,{hexData.Length / 2},\"{hexData}\"";
        var response = await _device.SendAsync(command, 6000, ct);
        return ParseCrsm(command, response);
    }

    /// <summary>خواندن خام IMEI در قالب BCD استاندارد از فیلد 6F7E (اگر وجود داشته باشد).</summary>
    public async Task<string> ReadStoredImeiAsync(CancellationToken ct = default)
    {
        var result = await ReadBinaryAsync(EfLoci, 11, ct);
        if (!result.IsSuccess) return string.Empty;
        return DecodeImei(result.Data);
    }

    /// <summary>تبدیل رشته BCD (نیم‌بایت‌های معکوس) به رقم‌های IMEI؛ 'F' نادیده گرفته می‌شود.</summary>
    public static string DecodeImei(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return string.Empty;
        var digits = new StringBuilder();
        for (var i = 0; i + 1 < hex.Length; i += 2)
        {
            var low = hex[i];
            var high = hex[i + 1];
            if (char.IsDigit(high)) digits.Append(high);
            if (char.IsDigit(low)) digits.Append(low);
            if (digits.Length >= 15) break;
        }
        return digits.Length >= 15 ? digits.ToString(0, 15) : string.Empty;
    }

    /// <summary>تبدیل IMEI به BCD معکوس با پرکردن 'F' (قالب استاندارد فیلدهای SIM).</summary>
    public static string EncodeBcd(string imei)
    {
        var digits = new string(imei.Where(char.IsDigit).ToArray());
        if (digits.Length % 2 != 0) digits += "F";
        var builder = new StringBuilder();
        for (var i = 0; i < digits.Length; i += 2)
        {
            builder.Append(digits[i + 1]);
            builder.Append(digits[i]);
        }
        return builder.ToString().ToUpperInvariant();
    }

    private static SimApduResult ParseCrsm(string command, string response)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            response, @"\+CRSM:\s*(\d+)\s*,\s*(\d+)\s*(?:,\s*""([0-9A-Fa-f]*)""?)?");

        if (match.Success)
        {
            var sw1 = int.Parse(match.Groups[1].Value);
            var sw2 = int.Parse(match.Groups[2].Value);
            var data = match.Groups[3].Success ? match.Groups[3].Value : string.Empty;
            return new SimApduResult(command, sw1, sw2, data);
        }

        // پاسخ +CSIM: +CSIM: <len>,"<hex incl. sw1sw2>"
        var csim = System.Text.RegularExpressions.Regex.Match(response, @"\+CSIM:\s*\d+\s*,\s*""([0-9A-Fa-f]+)""");
        if (csim.Success)
        {
            var hex = csim.Groups[1].Value;
            if (hex.Length >= 4)
            {
                var sw1 = Convert.ToInt32(hex[^4..^2], 16);
                var sw2 = Convert.ToInt32(hex[^2..], 16);
                return new SimApduResult(command, sw1, sw2, hex[..^4]);
            }
        }

        var generic = System.Text.RegularExpressions.Regex.Match(response, @"\b(9[0-9A-F]|6[0-9A-F]){1}([0-9A-F]{2})?\b");
        var fallbackSw1 = generic.Success ? Convert.ToInt32(generic.Value[..2], 16) : 0x6F;
        var fallbackSw2 = generic.Success && generic.Value.Length >= 4
            ? Convert.ToInt32(generic.Value[2..4], 16)
            : 0x00;
        return new SimApduResult(command, fallbackSw1, fallbackSw2, string.Empty);
    }

    /// <summary>تفسیر وضعیت یک آزمون نوشتن روی فیلد IMEI.</summary>
    public static SimWriteAccess ClassifyWrite(SimApduResult result)
    {
        if (result.IsSuccess) return SimWriteAccess.UpdateOk;
        if (result.Sw1 == 0x69 && result.Sw2 == 0x82) return SimWriteAccess.AdminRequired;
        if (result.Sw1 == 0x98) return SimWriteAccess.AdminRequired;
        if (result.Sw1 == 0x94) return SimWriteAccess.NotFound;
        return SimWriteAccess.Rejected;
    }

    public static string Explain(SimWriteAccess access, SimApduResult? result = null) => access switch
    {
        SimWriteAccess.UpdateOk =>
            "کارت اجازه UPDATE داد. توجه: این فیلد در استاندارد GSM فقط «کپی گزارش‌شده IMEI» است و " +
            "هویت واقعی روی شبکه از خود ماژول/گوشی می‌آید؛ پس نوشتن آن تضمینی برای تغییر IMEI در شبکه نیست.",
        SimWriteAccess.AdminRequired =>
            "کارت اجازه نوشتن نداد (شرط امنیتی ADM). این فیلد توسط اپراتور در مرحله شخصی‌سازی با کلید ADM " +
            "قفل می‌شود و از طریق AT/SIM800L قابل نوشتن نیست. هیچ نرم‌افزاری بدون کلید ADM آن اپراتور نمی‌تواند این کار را انجام دهد.",
        SimWriteAccess.NotFound =>
            "فایل موردنظر روی این کارت وجود ندارد (SIM قدیمی یا eUICC).",
        _ =>
            $"کارت درخواست را رد کرد — {(result is null ? "بدون جزئیات" : result.StatusName)}."
    };
}
