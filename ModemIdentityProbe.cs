using System.IO.Ports;
using System.Text;

namespace SIM800L_Flasher;

/// <summary>
/// خواندن IMEI از پورت مودم گوشی (نه ADB). روی سامسونگ این پورت با
/// <c>*#0808#</c> → <c>DM + MODEM + ADB</c> ظاهر می‌شود و به فرمان‌های AT استاندارد
/// بیس‌باند پاسخ می‌دهد؛ همان مسیری که ابزارهای تعمیرگاهی برای Read Info استفاده می‌کنند.
///
/// روی گوشی‌های Qualcomm معمولاً <c>AT+CGSN</c>، روی Shannon/Exynos گاهی <c>AT+CGSN=1</c>
/// و برای سیم دوم/ESIM به‌ترتیب <c>AT+CGSN=2</c> و <c>AT+CGSN=3</c> پاسخ می‌دهند.
/// این کلاس هیچ فرمان نوشتنی روی گوشی نمی‌فرستد؛ فقط خواندن.
/// </summary>
internal sealed record ModemProbeResult(
    string PortName,
    IReadOnlyList<(string Command, string Response)> Exchange,
    string Imei1,
    string Imei2,
    string Eid,
    string Serial,
    string Model,
    string Verdict)
{
    public bool FoundAny => Imei1.Length == 15 || Imei2.Length == 15 || Eid.Length > 0;

    public string ToText()
    {
        var text = new StringBuilder();
        text.AppendLine($"پورت مودم: {PortName}");
        text.AppendLine($"مدل مودم: {(Model.Length > 0 ? Model : "—")}   سریال: {(Serial.Length > 0 ? Serial : "—")}");
        text.AppendLine($"IMEI 1: {(Imei1.Length == 15 ? Imei1 : "خوانده نشد")}");
        text.AppendLine($"IMEI 2: {(Imei2.Length == 15 ? Imei2 : "—")}");
        text.AppendLine($"EID:    {(Eid.Length > 0 ? Eid : "—")}");
        text.AppendLine();
        text.AppendLine("رد فرمان‌ها:");
        foreach (var (command, response) in Exchange)
            text.AppendLine($"  {command} → {Flatten(response)}");
        text.AppendLine();
        text.AppendLine(Verdict);
        return text.ToString();
    }

    private static string Flatten(string value)
    {
        var flat = string.Join(" ", value.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return flat.Length <= 110 ? flat : flat[..110] + "…";
    }
}

internal static class ModemIdentityProbe
{
    public static readonly int[] CommonBaudRates = [115200, 9600, 460800, 230400, 57600, 38400];

    /// <summary>همه پورت‌های سریال فعلی ویندوز، برای انتخاب خودکار پورت مودم.</summary>
    public static IReadOnlyList<string> Ports() => SerialPort.GetPortNames().OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();

    /// <summary>
    /// روی چند پورت (یا یک پورت مشخص) AT می‌زند و اولین پورتی که IMEI بدهد را برمی‌گرداند.
    /// اگر هیچ پورتی پاسخ نداد، دقیق‌ترین پیامی که می‌شود گفت را برمی‌گرداند — نه موفقیت الکی.
    /// </summary>
    public static async Task<ModemProbeResult> ScanAsync(IEnumerable<string> ports, CancellationToken token = default)
    {
        var attempts = new List<string>();
        foreach (var port in ports)
        {
            token.ThrowIfCancellationRequested();
            foreach (var baud in CommonBaudRates)
            {
                var result = await ProbeAsync(port, baud, token);
                if (result.FoundAny) return result;
                attempts.Add($"{port}@{baud}: {result.Verdict}");
                if (result.Verdict.Contains("مودم پاسخ AT نداد", StringComparison.Ordinal) &&
                    result.Verdict.Contains("AT+CGSN", StringComparison.Ordinal))
                    break; // پورت پاسخ AT داد ولی IMEI نداد؛ نرخ‌های دیگر را امتحان نکن.
            }
        }

        var verdict = attempts.Count == 0
            ? "هیچ پورت سریالی پیدا نشد. در گوشی سامسونگ *#0808# را بزن و DM+MODEM+ADB را انتخاب کن."
            : "هیچ پورتی IMEI برنگرداند. " + string.Join(" | ", attempts.TakeLast(4));
        return new ModemProbeResult("-", [], "", "", "", "", "", verdict);
    }

    public static async Task<ModemProbeResult> ProbeAsync(string portName, int baudRate, CancellationToken token = default)
    {
        var exchange = new List<(string, string)>();
        using var port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
        {
            ReadTimeout = 2000,
            WriteTimeout = 2000,
            NewLine = "\r\n",
            DtrEnable = true,
            RtsEnable = true
        };

        try
        {
            port.Open();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return new ModemProbeResult(portName, exchange, "", "", "", "", "", $"پورت باز نشد ({ex.Message})");
        }

        try
        {
            var alive = await SendAsync(port, "AT", token);
            exchange.Add(("AT", alive));
            if (!alive.Contains("OK", StringComparison.OrdinalIgnoreCase))
                return new ModemProbeResult(portName, exchange, "", "", "", "", "",
                    "مودم پاسخ AT نداد؛ این پورت مودم نیست یا گوشی در حالت DM نیست.");

            var model = FirstLine(await SendAsync(port, "AT+CGMM", token));
            exchange.Add(("AT+CGMM", model));
            var serial = Clean(FirstLine(await SendAsync(port, "AT+CGSN=4", token)));
            exchange.Add(("AT+CGSN=4 (سریال)", serial));

            var imei1 = await ReadImeiAsync(port, "", exchange, token);
            var imei2 = await ReadImeiAsync(port, "=2", exchange, token);
            var eid = Clean(FirstLine(await SendAsync(port, "AT+CGSN=3", token)));
            exchange.Add(("AT+CGSN=3 (EID)", eid));

            var verdict = imei1.Length == 15
                ? "IMEI از پورت مودم خوانده شد."
                : "پورت مودم پاسخ داد ولی AT+CGSN عدد ۱۵ رقمی برنگرداند. " +
                  "اگر گوشی سامسونگ است، *#06# را بگیر: ممکن است IMEI در خود گوشی هم خالی باشد (اشکال مودم/CP).";
            return new ModemProbeResult(portName, exchange, imei1,
                imei2.Length == 15 ? imei2 : string.Empty,
                LooksLikeEid(eid) ? eid : string.Empty,
                LooksLikeImei(serial) || serial.Length == 0 ? string.Empty : serial,
                model,
                verdict);
        }
        catch (TimeoutException)
        {
            return new ModemProbeResult(portName, exchange, "", "", "", "", "",
                "مودم به فرمان در زمان مقرر پاسخ نداد؛ کابل و حالت DM+MODEM را بررسی کن.");
        }
        finally
        {
            try { port.Close(); } catch (Exception ex) when (ex is IOException or InvalidOperationException) { }
        }
    }

    private static async Task<string> ReadImeiAsync(SerialPort port, string suffix, List<(string, string)> exchange, CancellationToken token)
    {
        var command = $"AT+CGSN{suffix}";
        var response = await SendAsync(port, command, token);
        exchange.Add((command, response));
        return ExtractImei(response);
    }

    /// <summary>استخراج عدد ۱۵ رقمی از پاسخ؛ هم IMEI خالص و هم +CGSN: IMEI را می‌پذیرد.</summary>
    internal static string ExtractImei(string? response)
    {
        if (string.IsNullOrWhiteSpace(response)) return string.Empty;
        foreach (var token in response.Split(['\r', '\n', ' ', '\t', ':'], StringSplitOptions.RemoveEmptyEntries))
        {
            var digits = new string(token.Where(char.IsAsciiDigit).ToArray());
            if (digits.Length == 15 && digits.Length == token.Length && !digits.All(c => c == '0'))
                return digits;
        }
        return string.Empty;
    }

    private static bool LooksLikeImei(string value) => value.Length == 15 && value.All(char.IsAsciiDigit);

    private static bool LooksLikeEid(string value) => value.Length == 32 && value.All(char.IsAsciiDigit);

    private static string Clean(string value) => value.Trim().Trim('"');

    private static string FirstLine(string value) => value
        .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .FirstOrDefault(line => !line.Equals("OK", StringComparison.OrdinalIgnoreCase) &&
                                !line.Equals("ERROR", StringComparison.OrdinalIgnoreCase)) ?? string.Empty;

    private static async Task<string> SendAsync(SerialPort port, string command, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        try { port.DiscardInBuffer(); } catch (Exception ex) when (ex is IOException or InvalidOperationException) { }
        port.Write(command + "\r\n");
        var deadline = DateTime.UtcNow.AddMilliseconds(2500);
        var text = new StringBuilder();
        while (DateTime.UtcNow < deadline)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var chunk = port.ReadExisting();
                if (chunk.Length > 0)
                {
                    text.Append(chunk);
                    if (text.ToString().Contains("OK", StringComparison.OrdinalIgnoreCase) ||
                        text.ToString().Contains("ERROR", StringComparison.OrdinalIgnoreCase)) break;
                }
            }
            catch (TimeoutException) { }
            await Task.Delay(60, token);
        }
        return text.ToString();
    }
}
