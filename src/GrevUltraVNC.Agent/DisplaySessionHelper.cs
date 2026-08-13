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
    private const int EnumRegistrySettings = -2;
    private const uint CdsUpdateRegistry = 0x00000001;
    private const uint CdsNoReset = 0x10000000;
    private const uint DmPosition = 0x00000020;
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

            var result = CreateAndAttach(Math.Clamp(width, 800, 7680), Math.Clamp(height, 600, 4320));
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

    private static DisplaySessionBridgeResult CreateAndAttach(int bootstrapWidth, int bootstrapHeight)
    {
        // bootstrapWidth/bootstrapHeight are intentionally not used as the final Screen 2 mode.
        // Windows must first expose Screen 2 as an active desktop monitor. Only after that happens
        // do we read the REMOTE physical primary monitor and change Screen 2 to the exact same size.
        _ = bootstrapWidth;
        _ = bootstrapHeight;

        DisplayDevice? virtualDevice = null;
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            var devices = GetDisplayDevices();
            var candidates = devices
                .Where(IsVirtualDevice)
                .OrderBy(device => (device.StateFlags & DisplayDeviceAttachedToDesktop) != 0 ? 1 : 0)
                .ToArray();

            if (candidates.Length > 0)
            {
                virtualDevice = candidates[0];
                break;
            }

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

        // Phase 1: attach the display with its existing/default mode. Do not try to match the
        // physical monitor's resolution until Windows reports Screen 2 as attached and active.
        AttachDisplayBestEffort(deviceName);

        AgentDisplayInfo? attachedVirtual = null;
        List<AgentDisplayInfo> activeDisplays = [];
        deadline = DateTime.UtcNow.AddSeconds(12);
        while (DateTime.UtcNow < deadline)
        {
            activeDisplays = GetDisplays();
            attachedVirtual = activeDisplays.FirstOrDefault(display =>
                string.Equals(display.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase));

            if (attachedVirtual is not null && activeDisplays.Count >= 2 &&
                attachedVirtual.Width > 0 && attachedVirtual.Height > 0 &&
                attachedVirtual.VncMonitorIndex >= 1)
            {
                break;
            }

            attachedVirtual = null;
            Thread.Sleep(250);
        }

        if (attachedVirtual is null)
        {
            return new DisplaySessionBridgeResult(
                false,
                "The interactive Windows session found Grev Screen 2 but could not attach it as an active desktop display. " + DescribeDisplays(),
                deviceName,
                -1,
                GetDisplays().ToArray());
        }

        // Phase 2: Screen 2 is now active. Read the REMOTE physical primary monitor and change
        // only Screen 2's resolution to match it. Do not change Screen 1 and do not span monitors.
        var primary = activeDisplays.FirstOrDefault(display => display.IsPrimary && !display.IsVirtual)
            ?? activeDisplays.FirstOrDefault(display => !display.IsVirtual);

        if (primary is null || primary.Width <= 0 || primary.Height <= 0)
        {
            return new DisplaySessionBridgeResult(
                false,
                "Screen 2 became active, but Grev could not read the remote physical primary monitor resolution.",
                deviceName,
                attachedVirtual.VncMonitorIndex,
                activeDisplays.ToArray());
        }

        if (attachedVirtual.Width != primary.Width || attachedVirtual.Height != primary.Height)
        {
            if (!SetDisplayResolution(deviceName, primary.Width, primary.Height))
            {
                return new DisplaySessionBridgeResult(
                    false,
                    $"Screen 2 became active, but Windows would not set it to the primary monitor resolution {primary.Width}x{primary.Height}.",
                    deviceName,
                    attachedVirtual.VncMonitorIndex,
                    GetDisplays().ToArray());
            }

            deadline = DateTime.UtcNow.AddSeconds(12);
            while (DateTime.UtcNow < deadline)
            {
                activeDisplays = GetDisplays();
                attachedVirtual = activeDisplays.FirstOrDefault(display =>
                    string.Equals(display.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase));

                if (attachedVirtual is not null &&
                    attachedVirtual.Width == primary.Width &&
                    attachedVirtual.Height == primary.Height)
                {
                    break;
                }

                Thread.Sleep(250);
            }
        }

        if (attachedVirtual is null ||
            attachedVirtual.Width != primary.Width ||
            attachedVirtual.Height != primary.Height)
        {
            return new DisplaySessionBridgeResult(
                false,
                $"Screen 2 is active, but it did not settle at the primary monitor resolution {primary.Width}x{primary.Height}. " + DescribeDisplays(),
                deviceName,
                attachedVirtual?.VncMonitorIndex ?? -1,
                GetDisplays().ToArray());
        }

        return new DisplaySessionBridgeResult(
            true,
            $"Screen 2 active at {attachedVirtual.Width}x{attachedVirtual.Height} to match the remote primary monitor; VNC monitor {attachedVirtual.VncMonitorIndex}.",
            attachedVirtual.DeviceName,
            attachedVirtual.VncMonitorIndex,
            activeDisplays.ToArray());
    }

    private static void AttachDisplayBestEffort(string deviceName)
    {
        var attachedDisplays = GetDisplays();
        var rightEdge = attachedDisplays
            .Where(display => !string.Equals(display.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase))
            .Select(display => display.X + display.Width)
            .DefaultIfEmpty(0)
            .Max();

        var mode = CreateDevMode();
        if (!EnumDisplaySettingsEx(deviceName, EnumCurrentSettings, ref mode, 0) &&
            !EnumDisplaySettingsEx(deviceName, EnumRegistrySettings, ref mode, 0) &&
            !EnumDisplaySettingsEx(deviceName, 0, ref mode, 0))
        {
            return;
        }

        // A desktop position is required to attach an inactive monitor. Keep the monitor separate
        // from Screen 1, but leave its existing/default resolution untouched during this phase.
        mode.DmPositionX = rightEdge;
        mode.DmPositionY = 0;
        mode.DmFields = DmPosition;

        var result = ChangeDisplaySettingsEx(
            deviceName,
            ref mode,
            IntPtr.Zero,
            CdsUpdateRegistry | CdsNoReset,
            IntPtr.Zero);

        if (result == 0)
            ChangeDisplaySettingsEx(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero);
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
            var registry = CreateDevMode();
            var registryOk = EnumDisplaySettingsEx(device.DeviceName, EnumRegistrySettings, ref registry, 0);
            return $"{device.DeviceName} [{device.DeviceString}] flags=0x{device.StateFlags:X8} " +
                   $"attached={((device.StateFlags & DisplayDeviceAttachedToDesktop) != 0)} " +
                   $"virtual={IsVirtualDevice(device)} current={(currentOk ? $"{current.DmPelsWidth}x{current.DmPelsHeight}" : "none")} " +
                   $"registry={(registryOk ? $"{registry.DmPelsWidth}x{registry.DmPelsHeight}" : "none")}";
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
