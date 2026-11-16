using System.Text;

namespace SIM800L_Flasher;

internal sealed record NetworkProbeResult(
    string ModuleImei,
    string CimiResult,
    string SimStoredImei,
    string StoredImeiMatchesModule,
    IReadOnlyList<string> Lines,
    string Verdict);

/// <summary>
/// بررسی می‌کند هویتی که ماژول به شبکه اعلام می‌کند همان IMEI نوشته‌شده است یا نه، و آیا کارت
/// نسخه‌ای از IMEI را نگه می‌دارد یا نه.
///
/// این «تست واقعی» است تا حد امکان: تنها چیزی که اپراتور و دکل BTS می‌بینند هویت خود ماژول/گوشی است.
/// هیچ AT استانداردی اجازه نمی‌دهد IMEI را در شبکه «جا زد»؛ ترفندهای نوع SRSLY/AT+EGMR=1,10 و مشابه
/// مخصوص چیپست‌های MediaTek/Qualcomm در گوشی‌اند و روی SIM800L وجود ندارند.
/// </summary>
internal sealed class NetworkImeiProbe
{
    private readonly Sim800Device _device;
    private readonly SimFileAccess _files;

    public NetworkImeiProbe(Sim800Device device)
    {
        _device = device;
        _files = new SimFileAccess(device);
    }

    public async Task<NetworkProbeResult> RunAsync(string? expectedImei = null, CancellationToken ct = default)
    {
        var lines = new List<string>();

        var imei = ImeiWriter.ExtractImei(await _device.SendAsync("AT+GSN", 3000, ct));
        if (imei.Length != 15) imei = ImeiWriter.ExtractImei(await _device.SendAsync("AT+CGSN", 3000, ct));
        lines.Add($"IMEI اعلام‌شده توسط ماژول : {(imei.Length == 15 ? imei : "خوانده نشد")}");

        // AT+CIMI روی برخی firmwareها به‌اشتباه شناسه ماژول را برمی‌گرداند؛ ثبت می‌کنیم تا مقایسه شود.
        var cimi = (await _device.SendAsync("AT+CIMI", 3000, ct));
        var cimiFlat = Flatten(cimi);
        lines.Add($"پاسخ AT+CIMI            : {cimiFlat}");

        var stored = string.Empty;
        if (await _files.IsCrsmSupportedAsync(ct))
        {
            stored = await _files.ReadStoredImeiAsync(ct);
            lines.Add($"IMEI ذخیره‌شده در کارت    : {(stored.Length == 15 ? stored : "فیلد ندارد یا خالی است")}");
        }
        else
        {
            lines.Add("IMEI ذخیره‌شده در کارت    : AT+CRSM روی این ماژول پشتیبانی نمی‌شود");
        }

        var matches = stored.Length == 15 && imei.Length == 15 && stored == imei;
        lines.Add($"تطابق کارت با ماژول      : {(stored.Length == 15 ? (matches ? "بله" : "خیر") : "قابل بررسی نیست")}");

        if (expectedImei is not null && expectedImei.Length == 15)
        {
            lines.Add($"مقدار مورد انتظار        : {expectedImei}");
            lines.Add($"ماژول با انتظار برابر است: {(imei == expectedImei ? "بله ✅" : "خیر ❌")}");
        }

        var verdict = BuildVerdict(imei, stored, matches, expectedImei);
        lines.Add(string.Empty);
        lines.Add(verdict);

        return new NetworkProbeResult(imei, cimiFlat, stored, matches ? "بله" : "خیر", lines, verdict);
    }

    private static string BuildVerdict(string moduleImei, string stored, bool matches, string? expected)
    {
        var builder = new StringBuilder();
        if (moduleImei.Length != 15)
        {
            builder.AppendLine("نتیجه: IMEI ماژول خوانده نشد؛ پیش از هر نتیجه‌گیری اتصال و تغذیه را بررسی کنید.");
            return builder.ToString().TrimEnd();
        }

        var expectedOk = expected is null || expected.Length != 15 || moduleImei == expected;
        builder.AppendLine(expectedOk
            ? "نتیجه: هویتی که این ماژول روی شبکه اعلام می‌کند همان مقدار هدف است. اپراتور و دکل‌های BTS این را می‌بینند."
            : "نتیجه: ماژول هنوز مقدار هدف را اعلام نمی‌کند؛ نوشتن پایدار نشده است.");

        if (stored.Length == 15 && !matches)
            builder.AppendLine("توجه: نسخه ذخیره‌شده در کارت با ماژول فرق دارد؛ این فیلد در استاندارد GSM فقط «کپی گزارش‌شده توسط گوشی» است و در شناسایی شبکه نقشی ندارد.");
        else if (stored.Length != 15)
            builder.AppendLine("توجه: کارت فیلد IMEI قابل‌خواندنی ندارد؛ طبیعی است، چون هویت واقعی از خود دستگاه می‌آید.");

        builder.AppendLine("یادآوری: هیچ دستگاه یا نرم‌افزاری نمی‌تواند IMEI را «مستقل از سخت‌افزار» در شبکه جا بزند. " +
                           "آنچه خوانده می‌شود، همان IMEI سخت‌افزار ماژول/گوشی است.");
        return builder.ToString().TrimEnd();
    }

    private static string Flatten(string response) =>
        string.Join(" ", response.Replace("\r", string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
