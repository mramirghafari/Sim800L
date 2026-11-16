using System.Text;

namespace SIM800L_Flasher;

internal sealed record IdentityProofRow(string Scenario, string ImeiReported, string Source, string Meaning);

/// <summary>
/// اثبات عملی اینکه IMEI از سیم‌کارت نمی‌آید: IMEI بدون سیم‌کارت هم خوانده می‌شود و
/// با تعویض سیم‌کارت تغییر نمی‌کند. فقط یک گوشی/ماژول با سیم‌کارت «کار» می‌کند چون
/// اشتراک (IMSI) از کارت می‌آید، نه چون IMEI از کارت می‌آید.
/// </summary>
internal sealed class IdentityProof
{
    private readonly Sim800Device _device;

    public IdentityProof(Sim800Device device) => _device = device;

    public async Task<IReadOnlyList<IdentityProofRow>> RunAsync(CancellationToken ct = default)
    {
        var rows = new List<IdentityProofRow>();

        // ۱) IMEI بدون توجه به وضعیت سیم‌کارت — از حافظه خود ماژول خوانده می‌شود.
        var imei = ImeiWriter.ExtractImei(await _device.SendAsync("AT+GSN", 3000, ct));
        if (imei.Length != 15)
            imei = ImeiWriter.ExtractImei(await _device.SendAsync("AT+CGSN", 3000, ct));

        var cpin = await _device.SendAsync("AT+CPIN?", 3000, ct);
        var simState = cpin.Contains("NOT INSERTED", StringComparison.OrdinalIgnoreCase) ? "سیم‌کارت نیست"
            : cpin.Contains("READY", StringComparison.OrdinalIgnoreCase) ? "سیم‌کارت آماده"
            : cpin.Contains("SIM PIN", StringComparison.OrdinalIgnoreCase) ? "سیم‌کارت با PIN"
            : "وضعیت نامشخص";

        rows.Add(new IdentityProofRow(
            "خواندن IMEI با AT+GSN",
            imei.Length == 15 ? imei : "خوانده نشد",
            "حافظه خود ماژول (نه سیم‌کارت)",
            $"وضعیت کارت: {simState} — IMEI مستقل از کارت خوانده می‌شود"));

        // ۲) IMSI فقط از سیم‌کارت می‌آید — تفاوت بنیادی دو شناسه.
        var imsi = await _device.SendAsync("AT+CIMI", 3000, ct);
        var imsiFlat = string.Join("", imsi.Where(char.IsDigit));
        rows.Add(new IdentityProofRow(
            "خواندن IMSI با AT+CIMI",
            imsiFlat.Length >= 6 ? imsiFlat[..Math.Min(15, imsiFlat.Length)] : "خوانده نشد",
            "سیم‌کارت",
            "IMSI هویت مشترک است و فقط از کارت می‌آید؛ همین است که با تعویض کارت عوض می‌شود"));

        // ۳) ICCID — هم فقط روی کارت است.
        var iccid = await _device.SendAsync("AT+CCID", 3000, ct);
        var iccidFlat = string.Join("", iccid.Where(char.IsDigit));
        rows.Add(new IdentityProofRow(
            "خواندن ICCID با AT+CCID",
            iccidFlat.Length > 6 ? SessionLogger.Redact(iccidFlat) : "خوانده نشد",
            "سیم‌کارت",
            "سریال کارت؛ بخشی از سیم‌کارت است، نه دستگاه"));

        return rows;
    }

    /// <summary>هشدار درست برای تعویض سیم‌کارت بین دو گوشی.</summary>
    public static string DuplicateImeiWarning(string imei) =>
        $"اگر همین IMEI ({imei}) هم‌زمان در دو دستگاه به شبکه وصل شود، اپراتور آن را «IMEI تکراری» می‌بیند و " +
        "هر دو دستگاه را می‌تواند بلاک کند. برای همین IMEI تخصیص‌یافته به کد ملی، فقط باید روی یک دستگاه فعال باشد.";
}
