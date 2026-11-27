using System.Runtime.InteropServices;

namespace SIM800L_Flasher;

/// <summary>
/// گره USB یک گوشی، همراه با تفکیک رابط‌ها. یک گوشی سامسونگ یا آیفون وقتی وصل می‌شود
/// معمولاً چند گره می‌سازد: MTP، مودم، ADB، DIAG و چند رابط اینترفیس. IMEI را فقط
/// مودم/DIAG به شکل AT برمی‌گرداند، پس باید پورت درست را از همین‌جا پیدا کرد.
/// </summary>
internal sealed record MobileUsbNode(
    string InstanceId,
    string Vendor,
    string HardwareId,
    uint? DriverProblem,
    string? ComPort = null,
    int? VendorId = null,
    int? ProductId = null)
{
    public string InstanceTail => InstanceId[(InstanceId.LastIndexOf('\\') + 1)..];

    /// <summary>شناسه سخت‌افزاری که ویندوز به‌جای والد برای هر اینترفیس می‌سازد.</summary>
    public bool IsPhysicalDevice => HardwareId.Length == "VID_0000&PID_0000".Length;

    public string Kind => (ComPort is not null, Vendor, HardwareId) switch
    {
        (true, "Samsung", var id) when IsModemProductId(id) => "مودم/DIAG سامسونگ",
        (true, _, _) => "پورت سریال",
        (false, "Samsung", var id) when id.Contains("MS_COMP_MTP", StringComparison.OrdinalIgnoreCase) => "MTP سامسونگ",
        (false, "Samsung", var id) when id.Contains("ADB", StringComparison.OrdinalIgnoreCase) => "ADB سامسونگ",
        (false, "Samsung", _) => "سامسونگ (اینترفیس)",
        (false, "Apple", _) => "اپل (اینترفیس)",
        (false, "Android", _) => "اندروید (اینترفیس)",
        (false, "Other", _) => "سایر",
        _ => "ناشناس"
    };

    public bool IsModem => Vendor == "Samsung" && ComPort is not null || Kind.StartsWith("مودم", StringComparison.Ordinal);

    private static bool IsModemProductId(string hardwareId) =>
        hardwareId.Contains("&6860", StringComparison.OrdinalIgnoreCase) ||
        hardwareId.Contains("&6863", StringComparison.OrdinalIgnoreCase) ||
        hardwareId.Contains("&6865", StringComparison.OrdinalIgnoreCase) ||
        hardwareId.Contains("&685D", StringComparison.OrdinalIgnoreCase) ||
        hardwareId.Contains("&6610", StringComparison.OrdinalIgnoreCase) ||
        hardwareId.Contains("Modem", StringComparison.OrdinalIgnoreCase);

    public override string ToString() => $"{Vendor} | {Kind} | {HardwareId}";
}

internal static class MobileUsbInventory
{
    private const uint Present = 0x2;
    private const uint FilterEnumerator = 0x100;

    /// <summary>اسکن همه گره‌های USB و نگاشت آن‌ها به نام پورت COM و VID/PID.</summary>
    public static IReadOnlyList<MobileUsbNode> Scan()
    {
        var comPorts = SerialPortMap();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var result = CM_Get_Device_ID_List_SizeW(out var length, "USB", Present | FilterEnumerator);
            if (result != 0) throw new InvalidOperationException($"Configuration Manager (0x{result:X})");

            var buffer = new char[length];
            result = CM_Get_Device_ID_ListW("USB", buffer, length, Present | FilterEnumerator);
            if (result == 0x1A) continue; // USB topology changed between the calls.
            if (result != 0) throw new InvalidOperationException($"Configuration Manager (0x{result:X})");

            return new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries)
                .Select(id => ReadNode(id, comPorts))
                .OfType<MobileUsbNode>()
                .OrderBy(node => node.InstanceId, StringComparer.Ordinal)
                .ToArray();
        }

        throw new InvalidOperationException("فهرست USB هنگام اسکن تغییر کرد.");
    }

    private static MobileUsbNode? ReadNode(string instanceId, IReadOnlyDictionary<string, string> comPorts)
    {
        var parts = instanceId.Split('\\');
        if (parts.Length != 3) return null;

        var hardware = parts[1].ToUpperInvariant();
        var vendor = hardware switch
        {
            var id when id.StartsWith("VID_04E8&PID_", StringComparison.Ordinal) => "Samsung",
            var id when id.StartsWith("VID_05AC&PID_", StringComparison.Ordinal) => "Apple",
            var id when id.StartsWith("VID_18D1&PID_", StringComparison.Ordinal) ||
                        id.StartsWith("VID_2717&PID_", StringComparison.Ordinal) ||
                        id.StartsWith("VID_12D1&PID_", StringComparison.Ordinal) ||
                        id.StartsWith("VID_2207&PID_", StringComparison.Ordinal) ||
                        id.StartsWith("VID_0E8D&PID_", StringComparison.Ordinal) ||
                        id.StartsWith("VID_2C7C&PID_", StringComparison.Ordinal) => "Android",
            // سرعت‌سنج ثابت‌های پرکاربرد مودم که مخصوص سامسونگ نیست (Unisoc/HiSilicon/MediaTek OEM).
            var id when id.StartsWith("VID_1782&PID_", StringComparison.Ordinal) => "Android",
            _ => null
        };
        if (vendor is null) return null;

        uint? problem = null;
        if (CM_Locate_DevNodeW(out var node, instanceId, 0) == 0 &&
            CM_Get_DevNode_Status(out var status, out var code, node, 0) == 0)
            problem = (status & 0x400) != 0 ? code : 0;

        comPorts.TryGetValue(instanceId, out var comPort);
        return new MobileUsbNode(instanceId, vendor, parts[1], problem, comPort, ParseHex(hardware, "VID_"), ParseHex(hardware, "PID_"));
    }

    private static int? ParseHex(string hardware, string marker)
    {
        var index = hardware.IndexOf(marker, StringComparison.Ordinal);
        if (index < 0 || hardware.Length < index + marker.Length + 4) return null;
        return int.TryParse(hardware.AsSpan(index + marker.Length, 4), System.Globalization.NumberStyles.HexNumber,
            System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    /// <summary>InstanceId هر گره USB → نام پورت COM آن، از رجیستری. بدون این نگاشت، باز کردن پورت حدسی می‌شود.</summary>
    private static Dictionary<string, string> SerialPortMap()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var ports = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DEVICEMAP\SERIALCOMM");
            if (ports is null) return map;
            foreach (var name in ports.GetValueNames())
            {
                if (ports.GetValue(name) is string port)
                    map[name] = port;
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            // بدون دسترسی به رجیستری هم اسکن USB کار می‌کند، فقط نام پورت خالی می‌ماند.
        }
        return map;
    }

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint CM_Get_Device_ID_List_SizeW(out uint length, string filter, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint CM_Get_Device_ID_ListW(string filter,
        [Out, MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.U2, SizeParamIndex = 2)] char[] buffer,
        uint length, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint CM_Locate_DevNodeW(out uint device, string instanceId, uint flags);

    [DllImport("cfgmgr32.dll", ExactSpelling = true)]
    private static extern uint CM_Get_DevNode_Status(out uint status, out uint problem, uint device, uint flags);
}
