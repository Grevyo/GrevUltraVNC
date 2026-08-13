using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using GrevUltraVNC.Contracts;

namespace GrevUltraVNC.Agent;

/// <summary>
/// Owns Grev Screen 2 on the host. UltraVNC is only the viewer transport here: Grev first
/// creates a real Windows Indirect Display Driver monitor, then the controller opens a second
/// UltraVNC viewer against the monitor index returned by this service.
/// </summary>
public sealed class VirtualDisplayService : IDisposable
{
    // VirtualDrivers/Virtual-Display-Driver 25.7.23. This is the same signed driver-only asset
    // used by the upstream silent-install script. The exact release and SHA-256 are pinned so
    // Screen 2 never silently downloads a different driver build.
    private const string DriverVersion = "25.7.23";
    private const string DriverAssetUrl =
        "https://github.com/VirtualDrivers/Virtual-Display-Driver/releases/download/25.7.23/VirtualDisplayDriver-x86.Driver.Only.zip";
    private const string DriverAssetSha256 =
        "E24210692B442B39AF763536330CE78B423F19342B7A7792C26DE3944E418B3A";
    private const string DriverHardwareId = @"Root\MttVDD";
    private const string DriverInfName = "MttVDD.inf";
    private const string DriverCatalogName = "mttvdd.cat";

    private const int DisplayDeviceAttachedToDesktop = 0x00000001;
    private const int DisplayDevicePrimaryDevice = 0x00000004;
    private const int DisplayDeviceMirroringDriver = 0x00000008;
    private const int EnumCurrentSettings = -1;
    private const uint CdsUpdateRegistry = 0x00000001;
    private const uint CdsNoReset = 0x10000000;
    private const uint DmPosition = 0x00000020;
    private const uint DmPelsWidth = 0x00080000;
    private const uint DmPelsHeight = 0x00100000;
    private const uint MaximumAllowed = 0x02000000;
    private static readonly TimeSpan LeaseLifetime = TimeSpan.FromSeconds(30);

    private static readonly string AgentDataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "GrevUltraVNC",
        "Agent");

    private static readonly string DriverCacheDirectory = Path.Combine(
        AgentDataDirectory,
        "Drivers",
        "GrevVirtualDisplay",
        DriverVersion);

    private static readonly string OwnedDeviceStatePath = Path.Combine(
        AgentDataDirectory,
        "screen2-device.txt");

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, DateTimeOffset> _leases = new(StringComparer.OrdinalIgnoreCase);
    private readonly Timer _leaseTimer;
    private string? _virtualDeviceInstanceId;
    private string? _virtualDeviceName;
    private bool _disposed;

    public VirtualDisplayService()
    {
        Directory.CreateDirectory(AgentDataDirectory);
        CleanupStaleOwnedDevice();
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

                    if (string.IsNullOrWhiteSpace(_virtualDeviceInstanceId) ||
                        FindOwnedVirtualDisplay(GetDisplays()) is null)
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
                    var virtualDisplay = FindOwnedVirtualDisplay(displays);
                    if (virtualDisplay is null || displays.Count < 2)
                    {
                        _leases.Remove(controllerId);
                        ReleaseDeviceUnsafe();
                        return Snapshot(false,
                            "Windows did not attach Grev Screen 2 to the desktop.");
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
                    return Snapshot(true,
                        _leases.Count == 0
                            ? "Screen 2 removed."
                            : "Screen 2 is still in use by another Grev controller.");

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

    private async Task CreateVirtualDisplayUnsafeAsync(
        int width,
        int height,
        CancellationToken cancellationToken)
    {
        ReleaseDeviceUnsafe();

        var beforeDisplays = GetDisplays();
        var beforeNames = beforeDisplays
            .Select(display => display.DeviceName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var infPath = await EnsureGrevVirtualDisplayDriverAsync(cancellationToken);
        _virtualDeviceInstanceId = LegacyVirtualDisplayDevice.Create(
            infPath,
            DriverHardwareId,
            "GrevVirtualDisplay",
            "Grev Virtual Display");
        PersistOwnedDevice(_virtualDeviceInstanceId);

        AgentDisplayInfo? virtualDisplay = null;
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var displays = GetDisplays();
            virtualDisplay = displays.FirstOrDefault(display =>
                display.IsVirtual && !beforeNames.Contains(display.DeviceName));
            if (virtualDisplay is not null)
                break;
            await Task.Delay(250, cancellationToken);
        }

        if (virtualDisplay is null)
        {
            var pnpStatus = LegacyVirtualDisplayDevice.DescribeStatus(_virtualDeviceInstanceId);
            throw new InvalidOperationException(
                "Grev installed the signed Screen 2 display driver, but Windows did not expose a new desktop monitor. " +
                pnpStatus);
        }

        _virtualDeviceName = virtualDisplay.DeviceName;
        ConfigureVirtualDisplayBestEffort(virtualDisplay.DeviceName, width, height);

        deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var displays = GetDisplays();
            virtualDisplay = FindOwnedVirtualDisplay(displays);
            if (virtualDisplay is not null &&
                displays.Count >= 2 &&
                virtualDisplay.Width > 0 &&
                virtualDisplay.Height > 0)
            {
                return;
            }
            await Task.Delay(250, cancellationToken);
        }

        throw new InvalidOperationException(
            "Windows created Grev Screen 2 but did not expose it as a usable extended desktop display.");
    }

    private static async Task<string> EnsureGrevVirtualDisplayDriverAsync(
        CancellationToken cancellationToken)
    {
        var existingInf = FindCachedDriverFile(DriverInfName);
        var existingCatalog = FindCachedDriverFile(DriverCatalogName);
        if (existingInf is not null && existingCatalog is not null)
        {
            TrustDriverCatalog(existingCatalog);
            return existingInf;
        }

        var parent = Directory.GetParent(DriverCacheDirectory)?.FullName
            ?? throw new InvalidOperationException("Grev could not resolve the Screen 2 driver cache directory.");
        Directory.CreateDirectory(parent);

        var stagingDirectory = DriverCacheDirectory + ".staging";
        if (Directory.Exists(stagingDirectory))
            Directory.Delete(stagingDirectory, recursive: true);
        Directory.CreateDirectory(stagingDirectory);

        try
        {
            var zipPath = Path.Combine(stagingDirectory, "driver.zip");
            using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) })
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("GrevUltraVNC-Agent/Screen2");
                using var response = await client.GetAsync(
                    DriverAssetUrl,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
                response.EnsureSuccessStatusCode();

                await using var output = new FileStream(
                    zipPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    81920,
                    useAsync: true);
                await response.Content.CopyToAsync(output, cancellationToken);
            }

            var actualHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(zipPath, cancellationToken)));
            if (!string.Equals(actualHash, DriverAssetSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"The Screen 2 driver download failed integrity verification. Expected {DriverAssetSha256}, got {actualHash}.");
            }

            var payloadDirectory = Path.Combine(stagingDirectory, "payload");
            ZipFile.ExtractToDirectory(zipPath, payloadDirectory, overwriteFiles: true);

            var inf = Directory.EnumerateFiles(
                    payloadDirectory,
                    DriverInfName,
                    SearchOption.AllDirectories)
                .FirstOrDefault();
            var catalog = Directory.EnumerateFiles(
                    payloadDirectory,
                    DriverCatalogName,
                    SearchOption.AllDirectories)
                .FirstOrDefault();

            if (inf is null || catalog is null)
                throw new InvalidOperationException(
                    "The verified Screen 2 driver package did not contain MttVDD.inf and mttvdd.cat.");

            TrustDriverCatalog(catalog);

            if (Directory.Exists(DriverCacheDirectory))
                Directory.Delete(DriverCacheDirectory, recursive: true);
            Directory.Move(payloadDirectory, DriverCacheDirectory);

            var installedInf = FindCachedDriverFile(DriverInfName);
            if (installedInf is null)
                throw new InvalidOperationException("Grev cached the Screen 2 driver but could not find its INF afterwards.");

            return installedInf;
        }
        finally
        {
            try
            {
                if (Directory.Exists(stagingDirectory))
                    Directory.Delete(stagingDirectory, recursive: true);
            }
            catch
            {
            }
        }
    }

    private static string? FindCachedDriverFile(string fileName)
    {
        if (!Directory.Exists(DriverCacheDirectory))
            return null;

        return Directory.EnumerateFiles(
                DriverCacheDirectory,
                fileName,
                SearchOption.AllDirectories)
            .FirstOrDefault();
    }

    private static void TrustDriverCatalog(string catalogPath)
    {
        var catalogBytes = File.ReadAllBytes(catalogPath);
        var certificates = new X509Certificate2Collection();
#pragma warning disable SYSLIB0057
        certificates.Import(catalogBytes);
#pragma warning restore SYSLIB0057

        if (certificates.Count == 0)
            throw new InvalidOperationException(
                "The Screen 2 driver catalog did not contain a publisher certificate.");

        using var store = new X509Store(StoreName.TrustedPublisher, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadWrite);
        foreach (var certificate in certificates)
        {
            if (string.IsNullOrWhiteSpace(certificate.Thumbprint))
                continue;

            var existing = store.Certificates.Find(
                X509FindType.FindByThumbprint,
                certificate.Thumbprint,
                validOnly: false);
            if (existing.Count == 0)
                store.Add(certificate);
        }
    }

    private static void ConfigureVirtualDisplayBestEffort(
        string deviceName,
        int requestedWidth,
        int requestedHeight)
    {
        var displays = GetDisplays();
        var rightEdge = displays
            .Where(display => !string.Equals(display.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase))
            .Select(display => display.X + display.Width)
            .DefaultIfEmpty(0)
            .Max();

        var mode = CreateDevMode();
        if (!EnumDisplaySettingsEx(deviceName, EnumCurrentSettings, ref mode, 0))
            return;

        var useRequestedSize = SupportsMode(deviceName, requestedWidth, requestedHeight);
        mode.DmPositionX = rightEdge;
        mode.DmPositionY = 0;
        mode.DmFields |= DmPosition;

        if (useRequestedSize)
        {
            mode.DmPelsWidth = (uint)requestedWidth;
            mode.DmPelsHeight = (uint)requestedHeight;
            mode.DmFields |= DmPelsWidth | DmPelsHeight;
        }

        var originalDesktop = GetThreadDesktop(GetCurrentThreadId());
        var inputDesktop = OpenInputDesktop(0, false, MaximumAllowed);
        try
        {
            if (inputDesktop != IntPtr.Zero)
                SetThreadDesktop(inputDesktop);

            var result = ChangeDisplaySettingsEx(
                deviceName,
                ref mode,
                IntPtr.Zero,
                CdsUpdateRegistry | CdsNoReset,
                IntPtr.Zero);

            if (result != 0 && useRequestedSize)
            {
                // The monitor itself is more important than forcing a particular resolution.
                // Retry as position-only if Windows/driver rejects the controller's requested size.
                mode = CreateDevMode();
                if (EnumDisplaySettingsEx(deviceName, EnumCurrentSettings, ref mode, 0))
                {
                    mode.DmPositionX = rightEdge;
                    mode.DmPositionY = 0;
                    mode.DmFields |= DmPosition;
                    result = ChangeDisplaySettingsEx(
                        deviceName,
                        ref mode,
                        IntPtr.Zero,
                        CdsUpdateRegistry | CdsNoReset,
                        IntPtr.Zero);
                }
            }

            if (result == 0)
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

    private static bool SupportsMode(string deviceName, int width, int height)
    {
        for (var modeIndex = 0; modeIndex < 512; modeIndex++)
        {
            var mode = CreateDevMode();
            if (!EnumDisplaySettingsEx(deviceName, modeIndex, ref mode, 0))
                break;
            if (mode.DmPelsWidth == (uint)width && mode.DmPelsHeight == (uint)height)
                return true;
        }

        return false;
    }

    private AgentDisplayResponse Snapshot(
        bool success,
        string message,
        IReadOnlyList<AgentDisplayInfo>? knownDisplays = null)
    {
        var displays = knownDisplays ?? GetDisplays();
        var virtualDisplay = FindOwnedVirtualDisplay(displays);
        return new AgentDisplayResponse(
            success,
            message,
            virtualDisplay is not null && displays.Count >= 2,
            virtualDisplay?.DeviceName ?? _virtualDeviceName,
            virtualDisplay?.VncMonitorIndex ?? -1,
            displays);
    }

    private AgentDisplayInfo? FindOwnedVirtualDisplay(IReadOnlyList<AgentDisplayInfo> displays)
    {
        if (string.IsNullOrWhiteSpace(_virtualDeviceInstanceId))
            return null;

        if (!string.IsNullOrWhiteSpace(_virtualDeviceName))
        {
            var exact = displays.FirstOrDefault(display =>
                string.Equals(display.DeviceName, _virtualDeviceName, StringComparison.OrdinalIgnoreCase));
            if (exact is not null)
                return exact;
        }

        return displays.FirstOrDefault(display => display.IsVirtual);
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
            result.Add(new AgentDisplayInfo(
                device.DeviceName,
                device.DeviceString,
                mode.DmPositionX,
                mode.DmPositionY,
                (int)mode.DmPelsWidth,
                (int)mode.DmPelsHeight,
                isPrimary,
                IsVirtualDevice(device),
                vncIndex));
        }

        return result;
    }

    private static bool IsVirtualDevice(DisplayDevice device) =>
        device.DeviceString.Contains("Virtual Display Driver", StringComparison.OrdinalIgnoreCase) ||
        device.DeviceString.Contains("MttVDD", StringComparison.OrdinalIgnoreCase) ||
        device.DeviceString.Contains("Grev Virtual Display", StringComparison.OrdinalIgnoreCase) ||
        device.DeviceId.Contains("MttVDD", StringComparison.OrdinalIgnoreCase);

    private void CleanupStaleOwnedDevice()
    {
        try
        {
            if (!File.Exists(OwnedDeviceStatePath))
                return;

            var instanceId = File.ReadAllText(OwnedDeviceStatePath).Trim();
            if (!string.IsNullOrWhiteSpace(instanceId))
                LegacyVirtualDisplayDevice.TryRemove(instanceId);
            File.Delete(OwnedDeviceStatePath);
        }
        catch
        {
            // Do not stop the Agent from starting because a stale display could not be removed.
        }
    }

    private static void PersistOwnedDevice(string instanceId)
    {
        Directory.CreateDirectory(AgentDataDirectory);
        File.WriteAllText(OwnedDeviceStatePath, instanceId);
    }

    private static void ClearOwnedDeviceState()
    {
        try
        {
            if (File.Exists(OwnedDeviceStatePath))
                File.Delete(OwnedDeviceStatePath);
        }
        catch
        {
        }
    }

    private void ExpireLeases()
    {
        if (_disposed) return;
        if (!_gate.Wait(0)) return;
        try
        {
            PruneExpiredLeasesUnsafe();
            if (_leases.Count == 0 && !string.IsNullOrWhiteSpace(_virtualDeviceInstanceId))
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
        {
            _leases.Remove(controllerId);
        }
    }

    private void ReleaseDeviceUnsafe()
    {
        if (!string.IsNullOrWhiteSpace(_virtualDeviceInstanceId))
        {
            LegacyVirtualDisplayDevice.TryRemove(_virtualDeviceInstanceId);
            _virtualDeviceInstanceId = null;
        }

        _virtualDeviceName = null;
        ClearOwnedDeviceState();
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
        if (_disposed)
            throw new ObjectDisposedException(nameof(VirtualDisplayService));
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
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
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

    [DllImport("user32.dll")]
    private static extern IntPtr OpenInputDesktop(
        uint flags,
        [MarshalAs(UnmanagedType.Bool)] bool inherit,
        uint desiredAccess);

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
