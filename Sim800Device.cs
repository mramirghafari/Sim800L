using System.Diagnostics;
using System.IO.Ports;
using System.Text;
using System.Text.RegularExpressions;

namespace SIM800L_Flasher;

internal sealed record DeviceSnapshot(
    string Model,
    string Revision,
    string Imei,
    string Imsi,
    string Iccid,
    string OperatorName,
    string Signal,
    string Registration,
    string SimStatus,
    string PhoneNumber);

internal sealed class Sim800Device : IDisposable
{
    private const string PhoneNumberUnavailable = "روی سیم‌کارت ثبت نشده";
    private static readonly IReadOnlyDictionary<string, string> IranianOperators =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["43201"] = "کیش‌سل پارس",
            ["43202"] = "آپتل (نگین ارتباطات آوا)",
            ["43203"] = "پارسیان همراه لوتوس",
            ["43204"] = "فناپ موبایل",
            ["43205"] = "همراه هوشمند آینده",
            ["43206"] = "آرین‌تل",
            ["43207"] = "هوشمند امین موبایل",
            ["43208"] = "شاتل موبایل",
            ["43209"] = "های‌وب",
            ["43211"] = "همراه اول",
            ["43212"] = "های‌وب / آواسل",
            ["43213"] = "های‌وب",
            ["43214"] = "منطقه آزاد کیش",
            ["43220"] = "رایتل",
            ["43232"] = "مخابرات ایران",
            ["43235"] = "ایرانسل",
            ["43244"] = "مبین‌نت",
            ["43245"] = "فرابرد داده‌های ایرانیان",
            ["43246"] = "های‌وب",
            ["43249"] = "گسترش ارتباطات مبنا",
            ["43250"] = "شاتل",
            ["43251"] = "پیشگامان",
            ["43252"] = "آسیاتک",
            ["43270"] = "مخابرات ایران",
            ["43271"] = "ارتباطات کوه نور",
            ["43293"] = "ارتباطات فرزانگان پارس"
        };

    private readonly SemaphoreSlim _serialLock = new(1, 1);
    private SerialPort? _port;

    public bool IsOpen => _port?.IsOpen == true;
    public string PortName => _port?.PortName ?? string.Empty;

    public void Open(string portName, int baudRate)
    {
        Close();
        _port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
        {
            Handshake = Handshake.None,
            Encoding = Encoding.ASCII,
            NewLine = "\r\n",
            ReadTimeout = 250,
            WriteTimeout = 1500,
            DtrEnable = false,
            RtsEnable = false
        };
        _port.Open();
        _port.DiscardInBuffer();
        _port.DiscardOutBuffer();
    }

    public async Task<string> SendAsync(string command, int timeoutMs = 3000, CancellationToken cancellationToken = default)
    {
        if (_port?.IsOpen != true) throw new InvalidOperationException("ابتدا به ماژول متصل شوید.");
        if (!AtGuard.IsAllowed(command, out var reason)) throw new InvalidOperationException(reason);

        await _serialLock.WaitAsync(cancellationToken);
        try
        {
            _port.DiscardInBuffer();
            _port.Write(command.Trim() + "\r");

            var response = new StringBuilder();
            var timer = Stopwatch.StartNew();
            while (timer.ElapsedMilliseconds < timeoutMs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var chunk = _port.ReadExisting();
                if (!string.IsNullOrEmpty(chunk))
                {
                    response.Append(chunk);
                    var value = response.ToString();
                    if (value.Contains("\r\nOK\r\n", StringComparison.OrdinalIgnoreCase) ||
                        value.Contains("\r\nERROR\r\n", StringComparison.OrdinalIgnoreCase) ||
                        value.Contains("+CME ERROR", StringComparison.OrdinalIgnoreCase) ||
                        value.Contains("+CMS ERROR", StringComparison.OrdinalIgnoreCase))
                        break;
                }
                await Task.Delay(30, cancellationToken);
            }

            return response.Length == 0 ? "(بدون پاسخ؛ مهلت دریافت تمام شد)" : response.ToString().Trim();
        }
        finally
        {
            _serialLock.Release();
        }
    }

    public async Task<DeviceSnapshot> ReadSnapshotAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        async Task<string> QueryRaw(string command)
        {
            progress?.Report(command);
            return await SendAsync(command, 3500, cancellationToken);
        }

        async Task<string> Query(string command) => Clean(command, await QueryRaw(command));

        var ati = await Query("ATI");
        var revision = await Query("AT+GMR");
        var imei = await Query("AT+CGSN");
        var imsi = await Query("AT+CIMI");
        var iccid = await Query("AT+CCID");

        await QueryRaw("AT+COPS=3,2");
        var numericOperator = await QueryRaw("AT+COPS?");
        await QueryRaw("AT+COPS=3,0");
        var alphaOperator = await QueryRaw("AT+COPS?");
        var serviceProvider = await QueryRaw("AT+CSPN?");
        var operatorName = ResolveOperatorName(alphaOperator, numericOperator, serviceProvider, imsi);

        var signal = await Query("AT+CSQ");
        var registration = await Query("AT+CREG?");
        var simStatus = await Query("AT+CPIN?");

        var phoneNumber = await ReadPhoneNumberAsync(progress, cancellationToken);

        return new DeviceSnapshot(ati, revision, imei, imsi, iccid, operatorName, signal, registration, simStatus, phoneNumber);
    }

    public async Task<string> ReadPhoneNumberAsync(
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        async Task<string> QueryRaw(string command)
        {
            progress?.Report(command);
            return await SendAsync(command, 3500, cancellationToken);
        }

        var cnum = await QueryRaw("AT+CNUM");
        var phoneNumber = ParsePhoneNumber(cnum);
        if (phoneNumber != PhoneNumberUnavailable) return phoneNumber;

        await QueryRaw("AT+CPBS=\"ON\"");
        var ownNumbers = await QueryRaw("AT+CPBR=1,5");
        return ParsePhoneNumber(cnum, ownNumbers);
    }

    internal static string ResolveOperatorName(
        string alphaResponse,
        string numericResponse,
        string serviceProviderResponse,
        string imsi)
    {
        var alpha = ExtractCopsValue(alphaResponse);
        var numeric = ExtractCopsValue(numericResponse);
        var provider = ExtractQuotedValue(serviceProviderResponse, "\\+CSPN:\\s*\"(?<value>[^\"]*)\"");

        // IMSI صادرکننده‌ی خود سیم‌کارت را مشخص می‌کند؛ COPS ممکن است برای MVNO
        // فقط شبکه‌ی میزبان (مثلاً همراه اول یا ایرانسل) را برگرداند.
        var subscriberPlmn = FirstPlmn(imsi);
        var networkPlmn = FirstPlmn(numeric) ?? FirstPlmn(alpha);
        var plmn = subscriberPlmn ?? networkPlmn;
        var mapped = plmn is not null && IranianOperators.TryGetValue(plmn, out var known) ? known : null;
        var networkName = mapped ?? FirstUsefulName(alpha, provider);

        if (string.IsNullOrWhiteSpace(networkName))
            return plmn is null ? "نام اپراتور دریافت نشد" : $"اپراتور با کد {plmn}";

        return plmn is null || networkName.Contains(plmn, StringComparison.Ordinal)
            ? networkName
            : $"{networkName} ({plmn})";
    }

    internal static string ParsePhoneNumber(params string[] responses)
    {
        foreach (var response in responses.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            var cnum = Regex.Match(response,
                "\\+CNUM:\\s*\"[^\"]*\"\\s*,\\s*\"(?<number>\\+?[0-9]{7,16})\"",
                RegexOptions.IgnoreCase);
            if (cnum.Success)
                return NormalizePhoneNumber(cnum.Groups["number"].Value);

            var phoneBook = Regex.Match(response,
                "\\+CPBR:\\s*\\d+\\s*,\\s*\"(?<number>\\+?[0-9]{7,16})\"",
                RegexOptions.IgnoreCase);
            if (phoneBook.Success)
                return NormalizePhoneNumber(phoneBook.Groups["number"].Value);
        }

        return PhoneNumberUnavailable;
    }

    private static string? ExtractCopsValue(string response) =>
        ExtractQuotedValue(response, "\\+COPS:\\s*\\d+\\s*,\\s*\\d+\\s*,\\s*\"(?<value>[^\"]+)\"");

    private static string? ExtractQuotedValue(string response, string pattern)
    {
        if (string.IsNullOrWhiteSpace(response)) return null;
        var match = Regex.Match(response, pattern, RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["value"].Value.Trim() : null;
    }

    private static string? FirstPlmn(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var match = Regex.Match(value, @"(?<!\d)(?<plmn>\d{5,6})(?!\d)");
        if (match.Success) return match.Groups["plmn"].Value;

        var digits = new string(value.Where(char.IsDigit).ToArray());
        return digits.Length >= 5 ? digits[..5] : null;
    }

    private static string? FirstUsefulName(params string?[] values) => values
        .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value) && value.Any(char.IsLetter));

    private static string NormalizePhoneNumber(string value)
    {
        var number = value.Trim();
        if (number.StartsWith("0098", StringComparison.Ordinal))
            number = "+98" + number[4..];
        else if (number.StartsWith("98", StringComparison.Ordinal) && number.Length == 12)
            number = "+" + number;
        else if (number.StartsWith("09", StringComparison.Ordinal) && number.Length == 11)
            number = "+98" + number[1..];
        else if (number.StartsWith('9') && number.Length == 10)
            number = "+98" + number;

        return number;
    }

    private static string Clean(string command, string response)
    {
        var lines = response.Replace("\r", string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !line.Equals(command, StringComparison.OrdinalIgnoreCase))
            .Where(line => !line.Equals("OK", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return lines.Length == 0 ? "—" : string.Join(" | ", lines);
    }

    public void Close()
    {
        if (_port is null) return;
        try { if (_port.IsOpen) _port.Close(); }
        finally { _port.Dispose(); _port = null; }
    }

    public void Dispose()
    {
        Close();
        _serialLock.Dispose();
    }
}
