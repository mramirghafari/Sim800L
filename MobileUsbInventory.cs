using System.Runtime.InteropServices;

namespace SIM800L_Flasher;

internal sealed record MobileUsbNode(string InstanceId, string Vendor, string HardwareId, uint? DriverProblem)
{
    public string InstanceTail => InstanceId[(InstanceId.LastIndexOf('\\') + 1)..];
    public bool IsPhysicalDevice => HardwareId.Length == "VID_0000&PID_0000".Length;
}

internal static class MobileUsbInventory
{
    public static IReadOnlyList<MobileUsbNode> Scan()
    {
        const uint presentUsb = 0x101; // PRESENT | FILTER_ENUMERATOR
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var result = CM_Get_Device_ID_List_SizeW(out var length, "USB", presentUsb);
            if (result != 0) throw new InvalidOperationException($"Configuration Manager (0x{result:X})");

            var buffer = new char[length];
            result = CM_Get_Device_ID_ListW("USB", buffer, length, presentUsb);
            if (result == 0x1A) continue; // USB topology changed between the calls.
            if (result != 0) throw new InvalidOperationException($"Configuration Manager (0x{result:X})");

            return new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries)
                .Select(ReadNode).OfType<MobileUsbNode>().OrderBy(node => node.InstanceId, StringComparer.Ordinal).ToArray();
        }

        throw new InvalidOperationException("فهرست USB هنگام اسکن تغییر کرد.");
    }

    private static MobileUsbNode? ReadNode(string instanceId)
    {
        var parts = instanceId.Split('\\');
        if (parts.Length != 3) return null;

        var vendor = parts[1].ToUpperInvariant() switch
        {
            var hardware when hardware.StartsWith("VID_04E8&PID_", StringComparison.Ordinal) => "Samsung",
            var hardware when hardware.StartsWith("VID_05AC&PID_", StringComparison.Ordinal) => "Apple",
            _ => null
        };
        if (vendor is null) return null;

        uint? problem = null;
        if (CM_Locate_DevNodeW(out var node, instanceId, 0) == 0 &&
            CM_Get_DevNode_Status(out var status, out var code, node, 0) == 0)
            problem = (status & 0x400) != 0 ? code : 0;

        return new MobileUsbNode(instanceId, vendor, parts[1], problem);
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
