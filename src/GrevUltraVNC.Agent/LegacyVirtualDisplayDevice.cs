using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace GrevUltraVNC.Agent;

/// <summary>
/// Fallback for hosts where the Software Device API cannot enumerate UVncVirtualDisplay.
/// This mirrors DevCon/Device Manager's "Add legacy hardware" path: create a temporary
/// root-enumerated device with UltraVNC's hardware ID, then bind the exact signed INF.
/// </summary>
internal static class LegacyVirtualDisplayDevice
{
    private const string HardwareId = "UVncVirtualDisplay";
    private const uint DicdGenerateId = 0x00000001;
    private const uint SpdrpHardwareId = 0x00000001;
    private const uint DifRegisterDevice = 0x00000019;
    private const uint InstallFlagForce = 0x00000001;
    private const uint InstallFlagNonInteractive = 0x00000004;

    public static string Create(string infPath)
    {
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
                    "GrevUVncVirtualDisplay",
                    ref classGuid,
                    "Grev UltraVNC Virtual Display",
                    IntPtr.Zero,
                    DicdGenerateId,
                    ref deviceInfo))
            {
                ThrowLastWin32("Windows could not create the temporary Screen 2 device node");
            }

            var hardwareIds = Encoding.Unicode.GetBytes(HardwareId + "\0\0");
            if (!SetupDiSetDeviceRegistryProperty(
                    deviceInfoSet,
                    ref deviceInfo,
                    SpdrpHardwareId,
                    hardwareIds,
                    (uint)hardwareIds.Length))
            {
                ThrowLastWin32("Windows could not assign the UltraVNC hardware ID to Screen 2");
            }

            if (!SetupDiCallClassInstaller(DifRegisterDevice, deviceInfoSet, ref deviceInfo))
                ThrowLastWin32("Windows could not register the temporary Screen 2 display device");

            instanceId = GetInstanceId(deviceInfoSet, ref deviceInfo);

            var rebootRequired = false;
            if (!UpdateDriverForPlugAndPlayDevices(
                    IntPtr.Zero,
                    HardwareId,
                    Path.GetFullPath(infPath),
                    InstallFlagForce | InstallFlagNonInteractive,
                    out rebootRequired))
            {
                ThrowLastWin32("Windows created the Screen 2 device node but could not bind the UltraVNC display driver");
            }

            if (rebootRequired)
                throw new InvalidOperationException(
                    "Windows says the UltraVNC virtual-display driver requires a restart before Screen 2 can be used.");

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
            // Removal is best-effort. A subsequent Agent start/session can clean up a stale node.
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

        // Display adapters class. UltraVNC's virtual display INF should declare this itself;
        // the fallback keeps the error path deterministic if an unusual package omits ClassGuid.
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
}
