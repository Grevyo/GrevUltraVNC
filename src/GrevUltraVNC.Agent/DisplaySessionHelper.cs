using System.Runtime.InteropServices;
using System.Text.Json;
using GrevUltraVNC.Contracts;

namespace GrevUltraVNC.Agent;

public sealed record DisplaySessionBridgeResult(
    bool Success,
    string Message,
    string? VirtualDeviceName,
    int VirtualMonitorIndex,
    AgentDisplayInfo[] Displays);

internal static class DisplaySessionHelper
{
    private const int DisplayDeviceAttachedToDesktop = 0x00000001;
    private const int DisplayDevicePrimaryDevice = 0x00000004;
    private const int DisplayDeviceMirroringDriver = 0x00000008;
    private const int EnumCurrentSettings = -1;
    private const uint CdsUpdateRegistry = 0x00000001;
    private const uint CdsNoReset = 0x10000000;
    private const uint DmPelsWidth = 0x00080000;
    private const uint DmPelsHeight = 0x00100000;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static bool TryRun(string[] args, out int exitCode)
    {
        exitCode = 0;
        if (args.Length == 0 || !string.Equals(args[0], "--display-session-helper", StringComparison.OrdinalIgnoreCase))
            return false;

        exitCode = Run(args);
        return true;
    }

    private static int Run(string[] args)
    {
        var resultPath = args.Length > 1 ? args[1] : string.Empty;
        try
        {
            if (string.IsNullOrWhiteSpace(resultPath) || args.Length < 4 ||
                !int.TryParse(args[2], out var width) || !int.TryParse(args[3], out var height))
            {
                throw new InvalidOperationException("Screen 2 interactive helper arguments were invalid.");
            }

            var result = WaitForActiveAndMatchPrimary(
                Math.Clamp(width, 800, 7680),
                Math.Clamp(height, 600, 4320));
            WriteResult(resultPath, result);
            return result.Success ? 0 : 2;
        }
        catch (Exception ex)
        {
            if (!string.IsNullOrWhiteSpace(resultPath))
            {
                try
                {
                    WriteResult(resultPath, new DisplaySessionBridgeResult(
                        false,
                        "Screen 2 interactive helper failed: " + ex.Message,
                        null,
                        -1,
                        Array.Empty<AgentDisplayInfo>()));
                }
                catch { }
            }

            return 3;
        }
    }

    private static DisplaySessionBridgeResult WaitForActiveAndMatchPrimary(int bootstrapWidth, int bootstrapHeight)
    {
        // The request size is only a bootstrap value retained for protocol compatibility.
        // Do not use the controller PC's resolution to configure the remote virtual monitor.
        _ = bootstrapWidth;
        _ = bootstrapHeight;

        DisplayDevice? virtualDevice = null;
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            virtualDevice = GetDisplayDevices().FirstOrDefault(IsVirtualDevice);
            if (virtualDevice is not null && !string.IsNullOrWhiteSpace(virtualDevice.Value.DeviceName))
                break;

            virtualDevice = null;
            Thread.Sleep(250);
        }

        if (virtualDevice is null || string.IsNullOrWhiteSpace(virtualDevice.Value.DeviceName))
        {
            return new DisplaySessionBridgeResult(
                false,
                "The interactive Windows session could not see the Grev virtual display adapter. " + DescribeDisplays(),
                null,
                -1,
                GetDisplays().ToArray());
        }

        var deviceName = virtualDevice.Value.DeviceName;

        // IMPORTANT: do not attach, reposition or extend the desktop here. The virtual driver
        // already brings Screen 2 online. Wait until Windows reports it as an ACTIVE monitor first.
        AgentDisplayInfo? virtualDisplay = null;
        List<AgentDisplayInfo> displays = [];
        deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            displays = GetDisplays();
            virtualDisplay = displays.FirstOrDefault(display =>
                string.Equals(display.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase));

            if (virtualDisplay is not null &&
                virtualDisplay.Width > 0 &&
                virtualDisplay.Height > 0 &&
                virtualDisplay.VncMonitorIndex >= 1)
            {
                break;
            }

            virtualDisplay = null;
            Thread.Sleep(250);
        }

        if (virtualDisplay is null)
        {
            return new DisplaySessionBridgeResult(
                false,
                "Windows detected Grev Screen 2, but it did not become an active desktop monitor. " + DescribeDisplays(),
                deviceName,
                -1,
                GetDisplays().ToArray());
        }

        // Screen 2 is ACTIVE now. Only at this point read the remote physical primary monitor.
        var primary = displays.FirstOrDefault(display => display.IsPrimary && !display.IsVirtual)
            ?? displays.FirstOrDefault(display => !display.IsVirtual);

        if (primary is null || primary.Width <= 0 || primary.Height <= 0)
        {
            return new DisplaySessionBridgeResult(
                false,
                "Screen 2 is active, but Grev could not read the remote physical primary monitor resolution.",
                deviceName,
                virtualDisplay.VncMonitorIndex,
                displays.ToArray());
        }

        // Change ONLY the virtual monitor's width/height. Its desktop position is left entirely
        // alone so Grev does not turn the remote desktop into a combined/spanned framebuffer.
        if (virtualDisplay.Width != primary.Width || virtualDisplay.Height != primary.Height)
        {
            if (!SetDisplayResolution(deviceName, primary.Width, primary.Height))
            {
                return new DisplaySessionBridgeResult(
                    false,
                    $"Screen 2 is active, but Windows would not set it to the physical primary resolution {primary.Width}x{primary.Height}.",
                    deviceName,
                    virtualDisplay.VncMonitorIndex,
                    GetDisplays().ToArray());
            }

            deadline = DateTime.UtcNow.AddSeconds(12);
            while (DateTime.UtcNow < deadline)
            {
                displays = GetDisplays();
                virtualDisplay = displays.FirstOrDefault(display =>
                    string.Equals(display.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase));

                if (virtualDisplay is not null &&
                    virtualDisplay.Width == primary.Width &&
                    virtualDisplay.Height == primary.Height)
                {
                    break;
                }

                Thread.Sleep(250);
            }
        }

        if (virtualDisplay is null ||
            virtualDisplay.Width != primary.Width ||
            virtualDisplay.Height != primary.Height)
        {
            return new DisplaySessionBridgeResult(
                false,
                $"Screen 2 stayed active but did not settle at {primary.Width}x{primary.Height}. " + DescribeDisplays(),
                deviceName,
                virtualDisplay?.VncMonitorIndex ?? -1,
                GetDisplays().ToArray());
        }

        return new DisplaySessionBridgeResult(
            true,
            $"Screen 2 active at {virtualDisplay.Width}x{virtualDisplay.Height}, matching the remote physical primary monitor.",
            virtualDisplay.DeviceName,
            virtualDisplay.VncMonitorIndex,
            displays.ToArray());
    }

    private static bool SetDisplayResolution(string deviceName, int width, int height)
    {
        if (!SupportsMode(deviceName, width, height))
            return false;

        var mode = CreateDevMode();
        if (!EnumDisplaySettingsEx(deviceName, EnumCurrentSettings, ref mode, 0))
            return false;

        mode.DmPelsWidth = (uint)width;
        mode.DmPelsHeight = (uint)height;
        mode.DmFields = DmPelsWidth | DmPelsHeight;

        var result = ChangeDisplaySettingsEx(
            deviceName,
            ref mode,
            IntPtr.Zero,
            CdsUpdateRegistry | CdsNoReset,
            IntPtr.Zero);

        if (result != 0)
            return false;

        ChangeDisplaySettingsEx(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero);
        return true;
    }

    private static bool SupportsMode(string deviceName, int width, int height)
    {
        for (var index = 0; index < 512; index++)
        {
            var mode = CreateDevMode();
            if (!EnumDisplaySettingsEx(deviceName, index, ref mode, 0))
                break;
            if (mode.DmPelsWidth == (uint)width && mode.DmPelsHeight == (uint)height)
                return true;
        }

        return false;
    }

    private static List<AgentDisplayInfo> GetDisplays()
    {
        var result = new List<AgentDisplayInfo>();
        var nonPrimaryIndex = 1;

        foreach (var device in GetDisplayDevices())
        {
            if ((device.StateFlags & DisplayDeviceAttachedToDesktop) == 0 ||
                (device.StateFlags & DisplayDeviceMirroringDriver) != 0)
                continue;

            var mode = CreateDevMode();
            if (!EnumDisplaySettingsEx(device.DeviceName, EnumCurrentSettings, ref mode, 0))
                continue;

            var isPrimary = (device.StateFlags & DisplayDevicePrimaryDevice) != 0;
            result.Add(new AgentDisplayInfo(
                device.DeviceName,
                device.DeviceString,
                mode.DmPositionX,
                mode.DmPositionY,
                (int)mode.DmPelsWidth,
                (int)mode.DmPelsHeight,
                isPrimary,
                IsVirtualDevice(device),
                isPrimary ? 0 : nonPrimaryIndex++));
        }

        return result;
    }

    private static List<DisplayDevice> GetDisplayDevices()
    {
        var result = new List<DisplayDevice>();
        for (uint index = 0; ; index++)
        {
            var device = CreateDisplayDevice();
            if (!EnumDisplayDevices(null, index, ref device, 0))
                break;
            result.Add(device);
        }

        return result;
    }

    private static string DescribeDisplays()
    {
        var devices = GetDisplayDevices();
        if (devices.Count == 0)
            return "Interactive GDI returned no display adapters.";

        return "Interactive GDI: " + string.Join(" | ", devices.Select(device =>
        {
            var current = CreateDevMode();
            var currentOk = EnumDisplaySettingsEx(device.DeviceName, EnumCurrentSettings, ref current, 0);
            return $"{device.DeviceName} [{device.DeviceString}] flags=0x{device.StateFlags:X8} " +
                   $"attached={((device.StateFlags & DisplayDeviceAttachedToDesktop) != 0)} " +
                   $"virtual={IsVirtualDevice(device)} current={(currentOk ? $"{current.DmPelsWidth}x{current.DmPelsHeight}" : "none")}";
        }));
    }

    private static bool IsVirtualDevice(DisplayDevice device) =>
        device.DeviceString.Contains("Virtual Display Driver", StringComparison.OrdinalIgnoreCase) ||
        device.DeviceString.Contains("MttVDD", StringComparison.OrdinalIgnoreCase) ||
        device.DeviceString.Contains("Grev Virtual Display", StringComparison.OrdinalIgnoreCase) ||
        device.DeviceId.Contains("MttVDD", StringComparison.OrdinalIgnoreCase);

    private static void WriteResult(string path, DisplaySessionBridgeResult result)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(result, JsonOptions));
    }

    private static DisplayDevice CreateDisplayDevice() => new()
    {
        Cb = Marshal.SizeOf<DisplayDevice>(),
        DeviceName = string.Empty,
        DeviceString = string.Empty,
        DeviceId = string.Empty,
        DeviceKey = string.Empty
    };

    private static DevMode CreateDevMode() => new()
    {
        DmDeviceName = string.Empty,
        DmFormName = string.Empty,
        DmSize = (ushort)Marshal.SizeOf<DevMode>()
    };

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDevice
    {
        public int Cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DevMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DmDeviceName;
        public ushort DmSpecVersion;
        public ushort DmDriverVersion;
        public ushort DmSize;
        public ushort DmDriverExtra;
        public uint DmFields;
        public int DmPositionX;
        public int DmPositionY;
        public uint DmDisplayOrientation;
        public uint DmDisplayFixedOutput;
        public short DmColor;
        public short DmDuplex;
        public short DmYResolution;
        public short DmTTOption;
        public short DmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DmFormName;
        public ushort DmLogPixels;
        public uint DmBitsPerPel;
        public uint DmPelsWidth;
        public uint DmPelsHeight;
        public uint DmDisplayFlags;
        public uint DmDisplayFrequency;
        public uint DmICMMethod;
        public uint DmICMIntent;
        public uint DmMediaType;
        public uint DmDitherType;
        public uint DmReserved1;
        public uint DmReserved2;
        public uint DmPanningWidth;
        public uint DmPanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayDevices(
        string? device,
        uint deviceIndex,
        ref DisplayDevice displayDevice,
        uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplaySettingsEx(
        string deviceName,
        int modeNum,
        ref DevMode devMode,
        uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ChangeDisplaySettingsEx(
        string deviceName,
        ref DevMode devMode,
        IntPtr hwnd,
        uint flags,
        IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "ChangeDisplaySettingsExW")]
    private static extern int ChangeDisplaySettingsEx(
        IntPtr deviceName,
        IntPtr devMode,
        IntPtr hwnd,
        uint flags,
        IntPtr lParam);
}
