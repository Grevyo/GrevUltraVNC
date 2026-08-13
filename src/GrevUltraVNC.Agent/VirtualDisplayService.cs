using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using GrevUltraVNC.Contracts;

namespace GrevUltraVNC.Agent;

/// <summary>
/// Owns the Grev virtual monitor on the host. The software-device handle deliberately lives
/// inside the long-running Agent process: closing that handle removes the virtual monitor.
/// </summary>
public sealed class VirtualDisplayService : IDisposable
{
    private const string SupportedMonitorMapName = @"Global\{4A77E11C-B0B4-40F9-AA8B-D249116A76FE}";
    private const int SupportedMonitorMapBytes = sizeof(int) * (1 + 200 + 200);
    private const int DisplayDeviceAttachedToDesktop = 0x00000001;
    private const int DisplayDevicePrimaryDevice = 0x00000004;
    private const int DisplayDeviceMirroringDriver = 0x00000008;
    private const int EnumCurrentSettings = -1;
    private const uint CdsUpdateRegistry = 0x00000001;
    private const uint CdsNoReset = 0x10000000;
    private const uint DmPosition = 0x00000020;
    private const uint DmPelsWidth = 0x00080000;
    private const uint DmPelsHeight = 0x00100000;
    private const uint SwDeviceCapabilitiesRemovable = 0x1;
    private const uint SwDeviceCapabilitiesSilentInstall = 0x2;
    private const uint SwDeviceCapabilitiesDriverRequired = 0x8;
    private const uint MaximumAllowed = 0x02000000;
    private static readonly TimeSpan LeaseLifetime = TimeSpan.FromSeconds(30);
    private static readonly SwDeviceCreateCallback DeviceCreateCallback = OnDeviceCreated;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, DateTimeOffset> _leases = new(StringComparer.OrdinalIgnoreCase);
    private readonly Timer _leaseTimer;
    private MemoryMappedFile? _supportedMonitorMap;
    private IntPtr _softwareDeviceHandle;
    private string? _virtualDeviceName;
    private bool _disposed;

    public VirtualDisplayService()
    {
        _leaseTimer = new Timer(_ => ExpireLeases(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    public async Task<AgentDisplayResponse> ExecuteAsync(
        AgentDisplayRequest request,
        CancellationToken cancellationToken = default)
    {
        var controllerId = request.ControllerId?.Trim();
        if (string.IsNullOrWhiteSpace(controllerId) || controllerId.Length > 128)
            return Snapshot(false, "A valid Grev controller ID is required.");

        var operation = request.Operation?.Trim().ToLowerInvariant();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            PruneExpiredLeasesUnsafe();

            switch (operation)
            {
                case "create":
                {
                    var width = Math.Clamp(request.Width, 800, 7680);
                    var height = Math.Clamp(request.Height, 600, 4320);
                    _leases[controllerId] = DateTimeOffset.UtcNow;

                    if (_softwareDeviceHandle == IntPtr.Zero || FindVirtualDisplay(GetDisplays()) is null)
                    {
                        try
                        {
                            await CreateVirtualDisplayUnsafeAsync(width, height, cancellationToken);
                        }
                        catch
                        {
                            _leases.Remove(controllerId);
                            ReleaseDeviceUnsafe();
                            throw;
                        }
                    }

                    var displays = GetDisplays();
                    var virtualDisplay = FindVirtualDisplay(displays);
                    if (virtualDisplay is null || displays.Count < 2)
                    {
                        _leases.Remove(controllerId);
                        ReleaseDeviceUnsafe();
                        return Snapshot(false,
                            "Windows did not attach a second UVnc virtual monitor to the desktop.");
                    }

                    _virtualDeviceName = virtualDisplay.DeviceName;
                    return Snapshot(true,
                        $"Screen 2 ready: {virtualDisplay.Width}x{virtualDisplay.Height} on VNC monitor {virtualDisplay.VncMonitorIndex}.",
                        displays);
                }

                case "heartbeat":
                    if (_leases.ContainsKey(controllerId))
                        _leases[controllerId] = DateTimeOffset.UtcNow;
                    return Snapshot(true, "Screen 2 lease refreshed.");

                case "release":
                    _leases.Remove(controllerId);
                    if (_leases.Count == 0)
                        ReleaseDeviceUnsafe();
                    return Snapshot(true, _leases.Count == 0 ? "Screen 2 removed." : "Screen 2 is still in use by another Grev controller.");

                case "status":
                    return Snapshot(true, "Display status captured.");

                default:
                    return Snapshot(false, "Unsupported display operation. Use create, heartbeat, release, or status.");
            }
        }
        catch (Exception ex)
        {
            return Snapshot(false, ex.Message);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task CreateVirtualDisplayUnsafeAsync(int width, int height, CancellationToken cancellationToken)
    {
        ReleaseDeviceUnsafe();
        await EnsureUltraVncVirtualDriverAsync(cancellationToken);
        PrepareSupportedMonitorMap(width, height);

        var beforeNames = GetDisplays()
            .Where(display => display.IsVirtual)
            .Select(display => display.DeviceName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var creation = new DeviceCreationState();
        var stateHandle = GCHandle.Alloc(creation);
        IntPtr hardwareIds = IntPtr.Zero;
        IntPtr compatibleIds = IntPtr.Zero;
        try
        {
            // Multi-SZ values require two trailing NULs. StringToHGlobalUni supplies one,
            // so the embedded trailing NUL supplies the second.
            hardwareIds = Marshal.StringToHGlobalUni("UVncVirtualDisplay\0");
            compatibleIds = Marshal.StringToHGlobalUni("UVncVirtualDisplay\0");
            var instanceName = $"UVncVirtualDisplayGrev{Environment.ProcessId}";
            var info = new SwDeviceCreateInfo
            {
                CbSize = (uint)Marshal.SizeOf<SwDeviceCreateInfo>(),
                PszInstanceId = Marshal.StringToHGlobalUni(instanceName),
                PszzHardwareIds = hardwareIds,
                PszzCompatibleIds = compatibleIds,
                PContainerId = IntPtr.Zero,
                CapabilityFlags = SwDeviceCapabilitiesRemovable |
                                  SwDeviceCapabilitiesSilentInstall |
                                  SwDeviceCapabilitiesDriverRequired,
                PszDeviceDescription = Marshal.StringToHGlobalUni("Grev UltraVNC Virtual Display"),
                PszDeviceLocation = IntPtr.Zero,
                PSecurityDescriptor = IntPtr.Zero
            };

            try
            {
                var hr = SwDeviceCreate(
                    instanceName,
                    @"HTREE\ROOT\0",
                    ref info,
                    0,
                    IntPtr.Zero,
                    DeviceCreateCallback,
                    GCHandle.ToIntPtr(stateHandle),
                    out var deviceHandle);

                if (hr < 0 || deviceHandle == IntPtr.Zero)
                    Marshal.ThrowExceptionForHR(hr);

                _softwareDeviceHandle = deviceHandle;
                if (!creation.Completed.Wait(TimeSpan.FromSeconds(10), cancellationToken))
                    throw new TimeoutException("Windows timed out while creating the UVnc virtual display device.");
                if (creation.HResult < 0)
                    Marshal.ThrowExceptionForHR(creation.HResult);
            }
            finally
            {
                if (info.PszInstanceId != IntPtr.Zero) Marshal.FreeHGlobal(info.PszInstanceId);
                if (info.PszDeviceDescription != IntPtr.Zero) Marshal.FreeHGlobal(info.PszDeviceDescription);
            }
        }
        finally
        {
            if (hardwareIds != IntPtr.Zero) Marshal.FreeHGlobal(hardwareIds);
            if (compatibleIds != IntPtr.Zero) Marshal.FreeHGlobal(compatibleIds);
            if (stateHandle.IsAllocated) stateHandle.Free();
        }

        AgentDisplayInfo? virtualDisplay = null;
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(12);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var displays = GetDisplays();
            virtualDisplay = displays.FirstOrDefault(display =>
                display.IsVirtual && !beforeNames.Contains(display.DeviceName))
                ?? FindVirtualDisplay(displays);
            if (virtualDisplay is not null)
                break;
            await Task.Delay(250, cancellationToken);
        }

        if (virtualDisplay is null)
            throw new InvalidOperationException(
                "The UltraVNC virtual-display driver created a software device, but Windows never attached it as an active desktop monitor.");

        ConfigureVirtualDisplay(virtualDisplay.DeviceName, width, height);

        deadline = DateTime.UtcNow + TimeSpan.FromSeconds(8);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var displays = GetDisplays();
            virtualDisplay = FindVirtualDisplay(displays);
            if (virtualDisplay is not null && displays.Count >= 2 && virtualDisplay.Width > 0 && virtualDisplay.Height > 0)
            {
                _virtualDeviceName = virtualDisplay.DeviceName;
                return;
            }
            await Task.Delay(250, cancellationToken);
        }

        throw new InvalidOperationException("Windows did not expose the new virtual monitor as a usable second desktop display.");
    }

    private static async Task EnsureUltraVncVirtualDriverAsync(CancellationToken cancellationToken)
    {
        var winvnc = FindWinVncExecutable();
        if (winvnc is null)
            throw new FileNotFoundException("UltraVNC Server winvnc.exe could not be found on the host.");

        var root = Path.GetDirectoryName(winvnc)!;
        var inf = Directory.EnumerateFiles(root, "UVncVirtualDisplay.inf", SearchOption.AllDirectories).FirstOrDefault();
        if (inf is null)
            throw new FileNotFoundException(
                "UltraVNC virtual-display driver files are missing. Reinstall UltraVNC Server with the virtual monitor component available.");

        // UltraVNC provides this command specifically to stage/install its virtual monitor driver.
        // It is safe to call again when the driver is already present; actual device creation below
        // is the authoritative success check.
        var startInfo = new ProcessStartInfo
        {
            FileName = winvnc,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-installdriver");

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Windows could not start UltraVNC's virtual-driver installer.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException("UltraVNC's virtual-driver installer did not finish in time.");
        }
    }

    private void PrepareSupportedMonitorMap(int width, int height)
    {
        _supportedMonitorMap ??= MemoryMappedFile.CreateOrOpen(
            SupportedMonitorMapName,
            SupportedMonitorMapBytes,
            MemoryMappedFileAccess.ReadWrite);
        using var accessor = _supportedMonitorMap.CreateViewAccessor(0, SupportedMonitorMapBytes, MemoryMappedFileAccess.ReadWrite);
        accessor.Write(0, 1);                // counter
        accessor.Write(sizeof(int), width);  // w[0]
        accessor.Write(sizeof(int) * 201L, height); // h[0]
        accessor.Flush();
    }

    private static void ConfigureVirtualDisplay(string deviceName, int width, int height)
    {
        var displays = GetDisplays();
        var rightEdge = displays
            .Where(display => !display.IsVirtual)
            .Select(display => display.X + display.Width)
            .DefaultIfEmpty(0)
            .Max();

        var mode = CreateDevMode();
        if (!EnumDisplaySettingsEx(deviceName, EnumCurrentSettings, ref mode, 0))
            return;

        mode.DmPositionX = rightEdge;
        mode.DmPositionY = 0;
        mode.DmPelsWidth = (uint)width;
        mode.DmPelsHeight = (uint)height;
        mode.DmFields |= DmPosition | DmPelsWidth | DmPelsHeight;

        var originalDesktop = GetThreadDesktop(GetCurrentThreadId());
        var inputDesktop = OpenInputDesktop(0, false, MaximumAllowed);
        try
        {
            if (inputDesktop != IntPtr.Zero)
                SetThreadDesktop(inputDesktop);

            var result = ChangeDisplaySettingsEx(deviceName, ref mode, IntPtr.Zero, CdsUpdateRegistry | CdsNoReset, IntPtr.Zero);
            if (result != 0)
                throw new InvalidOperationException($"Windows could not configure the virtual monitor (display result {result}).");
            ChangeDisplaySettingsEx(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero);
        }
        finally
        {
            if (originalDesktop != IntPtr.Zero)
                SetThreadDesktop(originalDesktop);
            if (inputDesktop != IntPtr.Zero)
                CloseDesktop(inputDesktop);
        }
    }

    private AgentDisplayResponse Snapshot(bool success, string message, IReadOnlyList<AgentDisplayInfo>? knownDisplays = null)
    {
        var displays = knownDisplays ?? GetDisplays();
        var virtualDisplay = FindVirtualDisplay(displays);
        return new AgentDisplayResponse(
            success,
            message,
            virtualDisplay is not null && displays.Count >= 2,
            virtualDisplay?.DeviceName ?? _virtualDeviceName,
            virtualDisplay?.VncMonitorIndex ?? -1,
            displays);
    }

    private static List<AgentDisplayInfo> GetDisplays()
    {
        var result = new List<AgentDisplayInfo>();
        var nonPrimaryIndex = 1;

        for (uint deviceIndex = 0; ; deviceIndex++)
        {
            var device = CreateDisplayDevice();
            if (!EnumDisplayDevices(null, deviceIndex, ref device, 0))
                break;

            if ((device.StateFlags & DisplayDeviceAttachedToDesktop) == 0 ||
                (device.StateFlags & DisplayDeviceMirroringDriver) != 0)
                continue;

            var mode = CreateDevMode();
            if (!EnumDisplaySettingsEx(device.DeviceName, EnumCurrentSettings, ref mode, 0))
                continue;

            var isPrimary = (device.StateFlags & DisplayDevicePrimaryDevice) != 0;
            var vncIndex = isPrimary ? 0 : nonPrimaryIndex++;
            var isVirtual = IsVirtualDevice(device);
            result.Add(new AgentDisplayInfo(
                device.DeviceName,
                device.DeviceString,
                mode.DmPositionX,
                mode.DmPositionY,
                (int)mode.DmPelsWidth,
                (int)mode.DmPelsHeight,
                isPrimary,
                isVirtual,
                vncIndex));
        }

        return result;
    }

    private static AgentDisplayInfo? FindVirtualDisplay(IReadOnlyList<AgentDisplayInfo> displays) =>
        displays.FirstOrDefault(display => display.IsVirtual);

    private static bool IsVirtualDevice(DisplayDevice device) =>
        device.DeviceString.Contains("UVncVirtualDisplay", StringComparison.OrdinalIgnoreCase) ||
        device.DeviceId.Contains("UVncVirtualDisplay", StringComparison.OrdinalIgnoreCase);

    private static string? FindWinVncExecutable()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\uvnc_service");
            var imagePath = key?.GetValue("ImagePath") as string;
            var parsed = ParseExecutablePath(imagePath);
            if (!string.IsNullOrWhiteSpace(parsed) && File.Exists(parsed))
                return parsed;
        }
        catch
        {
        }

        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "uvnc bvba", "UltraVNC", "winvnc.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "UltraVNC", "winvnc.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "uvnc bvba", "UltraVNC", "winvnc.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "UltraVNC", "winvnc.exe")
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static string? ParseExecutablePath(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine)) return null;
        var value = commandLine.Trim();
        if (value.StartsWith('"'))
        {
            var end = value.IndexOf('"', 1);
            return end > 1 ? value[1..end] : null;
        }

        var exe = value.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return exe >= 0 ? value[..(exe + 4)] : value.Split(' ', 2)[0];
    }

    private void ExpireLeases()
    {
        if (_disposed) return;
        if (!_gate.Wait(0)) return;
        try
        {
            PruneExpiredLeasesUnsafe();
            if (_leases.Count == 0 && _softwareDeviceHandle != IntPtr.Zero)
                ReleaseDeviceUnsafe();
        }
        catch
        {
        }
        finally
        {
            _gate.Release();
        }
    }

    private void PruneExpiredLeasesUnsafe()
    {
        var cutoff = DateTimeOffset.UtcNow - LeaseLifetime;
        foreach (var controllerId in _leases
                     .Where(item => item.Value < cutoff)
                     .Select(item => item.Key)
                     .ToArray())
            _leases.Remove(controllerId);
    }

    private void ReleaseDeviceUnsafe()
    {
        if (_softwareDeviceHandle != IntPtr.Zero)
        {
            try { SwDeviceClose(_softwareDeviceHandle); } catch { }
            _softwareDeviceHandle = IntPtr.Zero;
        }

        _virtualDeviceName = null;
        try
        {
            if (_supportedMonitorMap is not null)
            {
                using var accessor = _supportedMonitorMap.CreateViewAccessor(0, sizeof(int), MemoryMappedFileAccess.Write);
                accessor.Write(0, 0);
                accessor.Flush();
            }
        }
        catch
        {
        }
    }

    private static void OnDeviceCreated(IntPtr hSwDevice, int hResult, IntPtr context, IntPtr deviceInstanceId)
    {
        if (context == IntPtr.Zero) return;
        try
        {
            var handle = GCHandle.FromIntPtr(context);
            if (handle.Target is DeviceCreationState state)
            {
                state.HResult = hResult;
                state.Completed.Set();
            }
        }
        catch
        {
        }
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

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(VirtualDisplayService));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _leaseTimer.Dispose();
        _gate.Wait();
        try
        {
            _leases.Clear();
            ReleaseDeviceUnsafe();
            _supportedMonitorMap?.Dispose();
            _supportedMonitorMap = null;
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }

    private sealed class DeviceCreationState
    {
        public ManualResetEventSlim Completed { get; } = new(false);
        public int HResult { get; set; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SwDeviceCreateInfo
    {
        public uint CbSize;
        public IntPtr PszInstanceId;
        public IntPtr PszzHardwareIds;
        public IntPtr PszzCompatibleIds;
        public IntPtr PContainerId;
        public uint CapabilityFlags;
        public IntPtr PszDeviceDescription;
        public IntPtr PszDeviceLocation;
        public IntPtr PSecurityDescriptor;
    }

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

    private delegate void SwDeviceCreateCallback(IntPtr hSwDevice, int hResult, IntPtr context, IntPtr deviceInstanceId);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int SwDeviceCreate(
        string enumeratorName,
        string parentDeviceInstance,
        ref SwDeviceCreateInfo createInfo,
        uint propertyCount,
        IntPtr properties,
        SwDeviceCreateCallback callback,
        IntPtr context,
        out IntPtr softwareDevice);

    [DllImport("cfgmgr32.dll")]
    private static extern void SwDeviceClose(IntPtr softwareDevice);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayDevices(string? device, uint deviceIndex, ref DisplayDevice displayDevice, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplaySettingsEx(string deviceName, int modeNum, ref DevMode devMode, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ChangeDisplaySettingsEx(string deviceName, ref DevMode devMode, IntPtr hwnd, uint flags, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "ChangeDisplaySettingsExW")]
    private static extern int ChangeDisplaySettingsEx(IntPtr deviceName, IntPtr devMode, IntPtr hwnd, uint flags, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr OpenInputDesktop(uint flags, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint desiredAccess);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetThreadDesktop(IntPtr desktop);

    [DllImport("user32.dll")]
    private static extern IntPtr GetThreadDesktop(uint threadId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseDesktop(IntPtr desktop);
}
