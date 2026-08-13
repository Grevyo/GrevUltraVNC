using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace GrevUltraVNC.Agent;

/// <summary>
/// Creates a temporary root-enumerated display device and binds an explicitly supplied signed
/// INF package to it. This mirrors Device Manager/DevCon's "Add legacy hardware" path and is
/// used by Grev Screen 2 for Indirect Display Driver providers.
/// </summary>
internal static class LegacyVirtualDisplayDevice
{
    private const uint DicdGenerateId = 0x00000001;
    private const uint SpdrpHardwareId = 0x00000001;
    private const uint DifRegisterDevice = 0x00000019;
    private const uint InstallFlagForce = 0x00000001;
    private const uint InstallFlagNonInteractive = 0x00000004;

    public static string Create(
        string infPath,
        string hardwareId,
        string deviceNodeName,
        string deviceDescription)
    {
        if (string.IsNullOrWhiteSpace(infPath) || !File.Exists(infPath))
            throw new FileNotFoundException("The Screen 2 display-driver INF could not be found.", infPath);
        if (string.IsNullOrWhiteSpace(hardwareId))
            throw new ArgumentException("A Screen 2 hardware ID is required.", nameof(hardwareId));

        var classGuid = ReadClassGuid(infPath);
        var deviceInfoSet = SetupDiCreateDeviceInfoList(ref classGuid, IntPtr.Zero);
        if (deviceInfoSet == new IntPtr(-1))
            ThrowLastWin32("Windows could not create a device-information set for Screen 2");

        string? instanceId = null;
        try
        {
            var deviceInfo = new SpDevInfoData
            {
                CbSize = (uint)Marshal.SizeOf<SpDevInfoData>()
            };

            if (!SetupDiCreateDeviceInfo(
                    deviceInfoSet,
                    deviceNodeName,
                    ref classGuid,
                    deviceDescription,
                    IntPtr.Zero,
                    DicdGenerateId,
                    ref deviceInfo))
            {
                ThrowLastWin32("Windows could not create the temporary Screen 2 display device");
            }

            var hardwareIds = Encoding.Unicode.GetBytes(hardwareId + "\0\0");
            if (!SetupDiSetDeviceRegistryProperty(
                    deviceInfoSet,
                    ref deviceInfo,
                    SpdrpHardwareId,
                    hardwareIds,
                    (uint)hardwareIds.Length))
            {
                ThrowLastWin32("Windows could not assign the Screen 2 hardware ID");
            }

            if (!SetupDiCallClassInstaller(DifRegisterDevice, deviceInfoSet, ref deviceInfo))
                ThrowLastWin32("Windows could not register the temporary Screen 2 display device");

            instanceId = GetInstanceId(deviceInfoSet, ref deviceInfo);

            var rebootRequired = false;
            if (!UpdateDriverForPlugAndPlayDevices(
                    IntPtr.Zero,
                    hardwareId,
                    Path.GetFullPath(infPath),
                    InstallFlagForce | InstallFlagNonInteractive,
                    out rebootRequired))
            {
                ThrowLastWin32("Windows created Screen 2 but could not bind its display driver");
            }

            if (rebootRequired)
                throw new InvalidOperationException(
                    "Windows says the Screen 2 display driver requires a restart before it can be used.");

            return instanceId;
        }
        catch
        {
            if (!string.IsNullOrWhiteSpace(instanceId))
                TryRemove(instanceId);
            throw;
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(deviceInfoSet);
        }
    }

    public static string DescribeStatus(string instanceId)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
            return "No device instance ID is available.";

        var locate = CM_Locate_DevNode(out var devInst, instanceId, 0);
        if (locate != 0)
            return $"Windows PnP could not locate the new display device (CONFIGRET 0x{locate:X8}).";

        var statusResult = CM_Get_DevNode_Status(out var status, out var problemNumber, devInst, 0);
        if (statusResult != 0)
            return $"Windows PnP could not read the display-device status (CONFIGRET 0x{statusResult:X8}).";

        return problemNumber == 0
            ? $"PnP status 0x{status:X8}; Windows reports no device problem code."
            : $"PnP status 0x{status:X8}; device problem code {problemNumber}.";
    }

    public static void TryRemove(string instanceId)
    {
        if (string.IsNullOrWhiteSpace(instanceId)) return;

        try
        {
            var pnputil = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "System32",
                "pnputil.exe");
            if (!File.Exists(pnputil)) return;

            var startInfo = new ProcessStartInfo
            {
                FileName = pnputil,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("/remove-device");
            startInfo.ArgumentList.Add(instanceId);

            using var process = Process.Start(startInfo);
            if (process is null) return;
            process.WaitForExit(15000);
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
            }
        }
        catch
        {
            // Removal is best-effort. A subsequent Agent start/session cleans up the exact
            // Grev-owned instance ID persisted by VirtualDisplayService.
        }
    }

    private static Guid ReadClassGuid(string infPath)
    {
        foreach (var rawLine in File.ReadLines(infPath))
        {
            var line = rawLine.Trim();
            if (!line.StartsWith("ClassGuid", StringComparison.OrdinalIgnoreCase))
                continue;

            var separator = line.IndexOf('=');
            if (separator < 0) continue;
            var value = line[(separator + 1)..].Trim().Trim('{', '}');
            if (Guid.TryParse(value, out var guid))
                return guid;
        }

        return new Guid("4d36e968-e325-11ce-bfc1-08002be10318");
    }

    private static string GetInstanceId(IntPtr deviceInfoSet, ref SpDevInfoData deviceInfo)
    {
        var buffer = new StringBuilder(512);
        if (!SetupDiGetDeviceInstanceId(
                deviceInfoSet,
                ref deviceInfo,
                buffer,
                buffer.Capacity,
                out _))
        {
            ThrowLastWin32("Windows created Screen 2 but Grev could not read its device instance ID");
        }

        return buffer.ToString();
    }

    private static void ThrowLastWin32(string message)
    {
        var error = Marshal.GetLastWin32Error();
        throw new InvalidOperationException(
            $"{message} (Win32 {error} / 0x{error:X8}: {new Win32Exception(error).Message}).");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDevInfoData
    {
        public uint CbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public UIntPtr Reserved;
    }

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern IntPtr SetupDiCreateDeviceInfoList(ref Guid classGuid, IntPtr hwndParent);

    [DllImport("setupapi.dll", EntryPoint = "SetupDiCreateDeviceInfoW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiCreateDeviceInfo(
        IntPtr deviceInfoSet,
        string deviceName,
        ref Guid classGuid,
        string? deviceDescription,
        IntPtr hwndParent,
        uint creationFlags,
        ref SpDevInfoData deviceInfoData);

    [DllImport("setupapi.dll", EntryPoint = "SetupDiSetDeviceRegistryPropertyW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiSetDeviceRegistryProperty(
        IntPtr deviceInfoSet,
        ref SpDevInfoData deviceInfoData,
        uint property,
        byte[] propertyBuffer,
        uint propertyBufferSize);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiCallClassInstaller(
        uint installFunction,
        IntPtr deviceInfoSet,
        ref SpDevInfoData deviceInfoData);

    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceInstanceIdW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceInstanceId(
        IntPtr deviceInfoSet,
        ref SpDevInfoData deviceInfoData,
        StringBuilder deviceInstanceId,
        int deviceInstanceIdSize,
        out int requiredSize);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("newdev.dll", EntryPoint = "UpdateDriverForPlugAndPlayDevicesW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateDriverForPlugAndPlayDevices(
        IntPtr hwndParent,
        string hardwareId,
        string fullInfPath,
        uint installFlags,
        [MarshalAs(UnmanagedType.Bool)] out bool rebootRequired);

    [DllImport("cfgmgr32.dll", EntryPoint = "CM_Locate_DevNodeW", CharSet = CharSet.Unicode)]
    private static extern uint CM_Locate_DevNode(out uint deviceInstance, string deviceId, uint flags);

    [DllImport("cfgmgr32.dll")]
    private static extern uint CM_Get_DevNode_Status(
        out uint status,
        out uint problemNumber,
        uint deviceInstance,
        uint flags);
}
