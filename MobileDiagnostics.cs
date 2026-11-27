using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace SIM800L_Flasher;

internal enum MobileCapability { Supported, Unsupported, Unknown, Restricted, AuthorizationRequired, DriverMissing, NotTested }

internal sealed record MobileTarget(string Protocol, string Id, string State, string Model, int? TransportId = null,
    MobileUsbNode? UsbNode = null)
{
    public override string ToString() => $"{Protocol}  |  {Model}  |  {State}  |  {MobileDiagnostics.Mask(UsbNode?.InstanceTail ?? Id)}";
}

internal sealed record MobileDiscovery(IReadOnlyList<MobileTarget> Devices, string Status);
internal sealed record MobileReading(Dictionary<string, string> Fields, Dictionary<string, MobileCapability> Capabilities,
    IReadOnlyDictionary<string, string> Identifiers, string Status, long ElapsedMs);
internal sealed record MobileToolResult(int ExitCode, string Output, string Error);
internal delegate Task<MobileToolResult> MobileToolRunner(string path, IReadOnlyList<string> args, CancellationToken token);

internal sealed class MobileDiagnostics(string toolsFolder, MobileToolRunner run)
{
    internal const string Android = "ADB";
    internal const string Apple = "iOS";
    internal const string WindowsUsb = "USB ویندوز";

    private static readonly (string Key, string Label)[] AndroidFields =
    [
        ("ro.product.manufacturer", "سازنده"), ("ro.product.model", "مدل"),
        ("ro.product.device", "شناسه مدل"), ("ro.soc.manufacturer", "سازنده چیپ"),
        ("ro.soc.model", "چیپ"), ("ro.board.platform", "پلتفرم"),
        ("ro.build.version.release", "نسخه اندروید"), ("ro.build.version.security_patch", "وصله امنیتی"),
        ("ro.build.display.id", "نسخه نرم‌افزار"), ("gsm.version.baseband", "بیس‌بند"), ("gsm.sim.state", "وضعیت سیم‌کارت")
    ];

    private static readonly (string Key, string Label)[] AppleFields =
    [
        ("ProductType", "شناسه مدل"), ("HardwareModel", "مدل برد"), ("CPUArchitecture", "معماری پردازنده"),
        ("ProductVersion", "نسخه iOS"), ("BuildVersion", "نسخه نرم‌افزار"), ("BasebandVersion", "بیس‌بند")
    ];

    private static readonly (string Key, string Label)[] AppleIdentifiers =
    [
        ("InternationalMobileEquipmentIdentity", "IMEI 1"),
        ("InternationalMobileEquipmentIdentity2", "IMEI 2"), ("EmbeddedIdentityDocument", "EID"),
        ("SerialNumber", "سریال")
    ];

    public async Task<MobileDiscovery> DiscoverAsync(string protocol, IReadOnlyList<MobileUsbNode> usb, CancellationToken token)
    {
        var executable = FindTool(protocol == Android ? "adb.exe" : "idevice_id.exe");
        if (executable is null || (protocol == Apple && FindTool("ideviceinfo.exe") is null))
            return new([], $"{protocol} — ابزار لازم پیدا نشد.");

        try
        {
            var result = await run(executable, protocol == Android ? ["devices", "-l"] : ["-l"], token);
            if (result.ExitCode != 0)
                return new([], $"{protocol} — خطای شناسایی (کد {result.ExitCode}). اتصال USB و درایور را بررسی کنید.");

            var devices = protocol == Android ? ParseAdbDevices(result.Output, usb) :
                Lines(result.Output).Where(id => Regex.IsMatch(id, "^(?:[a-fA-F0-9]{40}|[a-fA-F0-9]{8}-[a-fA-F0-9]{16})$"))
                .Distinct(StringComparer.OrdinalIgnoreCase).Select(id => new MobileTarget(Apple, id, "Trust بررسی نشده", "Apple")).ToArray();
            return new(devices, $"{protocol} — {devices.Count} دستگاه در فهرست USB ابزار.");
        }
        catch (Exception ex) when (ex is TimeoutException or Win32Exception or IOException)
        {
            return new([], $"{protocol} — {ToolFailure(ex)}");
        }
    }

    internal static IReadOnlyList<MobileTarget> ParseAdbDevices(string output, IReadOnlyList<MobileUsbNode> usb)
    {
        var devices = new List<MobileTarget>();
        foreach (var line in Lines(output))
        {
            var columns = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (columns.Length < 2 || !Regex.IsMatch(columns[0], "^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$") ||
                columns[0].StartsWith("emulator-", StringComparison.OrdinalIgnoreCase) ||
                columns[0].Contains("._adb", StringComparison.OrdinalIgnoreCase)) continue;

            var serial = columns[0];
            var isUsb = columns.Skip(2).Any(field => field.StartsWith("usb:", StringComparison.Ordinal)) ||
                usb.Any(node => node.Vendor == "Samsung" && node.InstanceTail.Equals(serial, StringComparison.OrdinalIgnoreCase));
            if (!isUsb) continue;

            var model = columns.FirstOrDefault(field => field.StartsWith("model:", StringComparison.Ordinal));
            var transport = columns.FirstOrDefault(field => field.StartsWith("transport_id:", StringComparison.Ordinal));
            var transportId = int.TryParse(transport?[13..], out var number) && number > 0 ? number : (int?)null;
            var state = columns[1] is "device" or "offline" or "unauthorized" or "recovery" or "sideload" or "bootloader"
                ? columns[1] : "unknown";
            devices.Add(new MobileTarget(Android, serial, state, model?[6..] ?? "Android", transportId));
        }
        return devices;
    }

    internal static MobileTarget[] CombineWithWindowsUsb(IEnumerable<MobileTarget> discovered, IReadOnlyList<MobileUsbNode> usb)
    {
        var targets = discovered.ToArray();
        var fromWindows = usb.Where(node => node.IsPhysicalDevice && !targets.Any(target => MatchesWindowsUsb(target, node)))
            .Select(node => new MobileTarget(WindowsUsb, node.InstanceId, "اتصال USB شناسایی شد", node.Vendor, UsbNode: node));
        return [.. targets, .. fromWindows];
    }

    internal static bool MatchesWindowsUsb(MobileTarget target, MobileUsbNode node) => (node.Vendor, target.Protocol) switch
    {
        ("Samsung", Android) => node.InstanceTail.Equals(target.Id, StringComparison.OrdinalIgnoreCase),
        ("Apple", Apple) => node.InstanceTail.Replace("-", "").Equals(target.Id.Replace("-", ""), StringComparison.OrdinalIgnoreCase),
        _ => false
    };

    internal static string WindowsUsbHint(MobileUsbNode node) => node.Vendor == "Samsung"
        ? "ویندوز گوشی را از USB می‌بیند. برای خواندن جزئیات، USB debugging را در Developer options فعال و درخواست مجوز رایانه را روی گوشی تأیید کنید، سپس دوباره اسکن کنید. اگر ADB هنوز خالی بود، رابط و درایور ADB را در Device Manager بررسی کنید."
        : "ویندوز دستگاه اپل را از USB می‌بیند. برای خواندن جزئیات، ابزار iOS را آماده و پس از باز کردن قفل گوشی، Trust را تأیید کنید. سپس دوباره اسکن کنید.";

    public async Task<MobileReading> ReadAsync(MobileTarget target, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (target.UsbNode is { } node)
        {
            var fields = new Dictionary<string, string> { ["سازنده"] = node.Vendor, ["شناسه سخت‌افزار USB"] = node.HardwareId };
            var capabilities = new Dictionary<string, MobileCapability>
            {
                ["شناسایی USB در ویندوز"] = MobileCapability.Supported,
                ["خواندن مستقیم اطلاعات گوشی"] = node.DriverProblem == 28 ? MobileCapability.DriverMissing : MobileCapability.NotTested
            };
            return new(fields, capabilities, new Dictionary<string, string>(), WindowsUsbHint(node), 0);
        }

        var elapsed = Stopwatch.StartNew();
        var isAndroid = target.Protocol == Android;
        if (isAndroid && target.State != "device")
            return BuildReading(target, [], target.State == "unauthorized" ? MobileCapability.AuthorizationRequired : MobileCapability.Unknown,
                target.State == "unauthorized" ? "درخواست USB debugging را روی گوشی تأیید کنید و دوباره اسکن کنید."
                : "اندروید در حالت آماده ADB نیست.", elapsed.ElapsedMilliseconds);

        var executable = FindTool(isAndroid ? "adb.exe" : "ideviceinfo.exe");
        if (executable is null)
            return BuildReading(target, [], MobileCapability.NotTested, "ابزار ارتباط در دسترس نیست.", elapsed.ElapsedMilliseconds);

        try
        {
            IReadOnlyList<string> args = isAndroid
                ? target.TransportId is int transportId
                    ? ["-t", transportId.ToString(System.Globalization.CultureInfo.InvariantCulture), "shell", "getprop"]
                    : ["-s", target.Id, "shell", "getprop"]
                : ["-u", target.Id, "-x"];
            var result = await run(executable, args, token);
            if (result.ExitCode != 0)
            {
                var authorization = Regex.IsMatch(result.Error + result.Output,
                    "unauthorized|pairing|pair record|host id|passcode|trust|password", RegexOptions.IgnoreCase);
                return BuildReading(target, [], authorization ? MobileCapability.AuthorizationRequired : MobileCapability.Unknown,
                    authorization ? "قفل صفحه را باز کنید و مجوز اتصال به رایانه را روی گوشی بررسی کنید."
                        : $"خواندن اطلاعات انجام نشد (کد {result.ExitCode}). اتصال را بررسی کنید.", elapsed.ElapsedMilliseconds);
            }

            var properties = isAndroid ? ParseAdbProps(result.Output) : ParseApplePlist(result.Output);
            return BuildReading(target, properties, MobileCapability.Unknown, "", elapsed.ElapsedMilliseconds);
        }
        catch (Exception ex) when (ex is TimeoutException or Win32Exception or IOException or XmlException)
        {
            return BuildReading(target, [], MobileCapability.Unknown, ToolFailure(ex), elapsed.ElapsedMilliseconds);
        }
    }

    private static MobileReading BuildReading(MobileTarget target, Dictionary<string, string> properties,
        MobileCapability unavailable, string status, long elapsedMs)
    {
        var isAndroid = target.Protocol == Android;
        var fields = new Dictionary<string, string>();
        var identifiers = new Dictionary<string, string>();
        var capabilities = new Dictionary<string, MobileCapability>();

        foreach (var (key, label) in isAndroid ? AndroidFields : AppleFields)
        {
            if (properties.TryGetValue(key, out var value) && value.Length is > 0 and <= 160 &&
                !value.Any(char.IsControl) && value.ToLowerInvariant() is not ("unknown" or "n/a" or "null"))
                fields[label] = SessionLogger.Redact(value);
            capabilities[label] = fields.ContainsKey(label) ? MobileCapability.Supported : unavailable;
        }

        foreach (var (key, label) in AppleIdentifiers)
        {
            if (!isAndroid && properties.TryGetValue(key, out var value) && value.Length is > 0 and <= 64 &&
                value.All(char.IsAsciiLetterOrDigit)) identifiers[label] = value;
            capabilities[label] = identifiers.ContainsKey(label) ? MobileCapability.Supported
                : isAndroid && unavailable == MobileCapability.Unknown && fields.Count > 0 ? MobileCapability.Restricted : unavailable;
        }

        if (isAndroid && fields.Count > 0)
        {
            identifiers["سریال"] = target.Id;
            capabilities["سریال"] = MobileCapability.Supported;
        }

        capabilities["گزارش تشخیصی"] = MobileCapability.Supported;
        capabilities["بازیابی شناسه از داخل XAnten"] = MobileCapability.Unsupported;
        if (string.IsNullOrEmpty(status)) status = fields.Count > 0
            ? "اطلاعات قابل دسترس از دستگاه خوانده شد."
            : "ابزار اجرا شد ولی فیلد تشخیصی قابل استفاده برنگرداند.";
        return new(fields, capabilities, identifiers, status, elapsedMs);
    }

    private static Dictionary<string, string> ParseAdbProps(string output)
    {
        var properties = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in Lines(output))
        {
            var delimiter = line.IndexOf("]: [", StringComparison.Ordinal);
            if (delimiter > 0 && line.StartsWith('[') && line.EndsWith(']'))
                properties[line[1..delimiter]] = line[(delimiter + 4)..^1].Trim();
        }
        return properties;
    }

    private static Dictionary<string, string> ParseApplePlist(string output)
    {
        using var input = new StringReader(output);
        using var xml = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null,
            MaxCharactersInDocument = 1_000_000 });
        var values = XDocument.Load(xml).Root?.Element("dict")?.Elements().ToArray() ?? [];
        var properties = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i + 1 < values.Length; i += 2)
        {
            if (values[i].Name == "key" && values[i + 1].Name is { LocalName: "string" or "integer" })
                properties[values[i].Value] = values[i + 1].Value.Trim();
        }
        return properties;
    }

    internal static string Mask(string value) => value.Length <= 4 ? new string('•', value.Length) : "••••" + value[^4..];

    private static IEnumerable<string> Lines(string output) => output.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private static string ToolFailure(Exception error) => error switch
    {
        TimeoutException => "زمان پاسخ ابزار تمام شد. اتصال را بررسی و دوباره تلاش کنید.",
        XmlException => "پاسخ ابزار iOS به‌صورت معتبر دریافت نشد.",
        _ => "اجرای ابزار ممکن نبود. فایل اجرایی و وابستگی‌های آن را بررسی کنید."
    };

    /// <summary>مسیر ابزار برای استفاده سایر بخش‌ها (بدون اجرای آن).</summary>
    public string? FindToolPath(string name) => FindTool(name);

    private string? FindTool(string name)
    {
        var directories = new List<string> { toolsFolder, Path.Combine(AppContext.BaseDirectory, "platform-tools"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Android", "Sdk", "platform-tools") };
        directories.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries));
        foreach (var entry in directories)
        {
            var directory = entry.Trim().Trim('"');
            if (!Path.IsPathFullyQualified(directory)) continue;
            var path = Path.Combine(directory, name);
            if (File.Exists(path)) return path;
        }
        return null;
    }

    internal static async Task<MobileToolResult> RunToolAsync(string executable, IReadOnlyList<string> args, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var info = new ProcessStartInfo(executable)
        {
            WorkingDirectory = Path.GetDirectoryName(executable)!, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var arg in args) info.ArgumentList.Add(arg);

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(12));
        using var process = Process.Start(info) ?? throw new IOException("Process did not start.");
        var stdout = process.StandardOutput.ReadToEndAsync(deadline.Token);
        var stderr = process.StandardError.ReadToEndAsync(deadline.Token);
        try
        {
            await Task.WhenAll(process.WaitForExitAsync(deadline.Token), stdout, stderr);
            return new(process.ExitCode, await stdout, await stderr);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(); }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException) { }
            token.ThrowIfCancellationRequested();
            throw new TimeoutException();
        }
    }
}
