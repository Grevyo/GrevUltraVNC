using System.IO.Compression;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using GrevUltraVNC.Contracts;

namespace GrevUltraVNC.Agent;

public sealed class VirtualDisplayService : IDisposable
{
    private const string DriverVersion = "25.7.23";
    private const string DriverAssetUrl =
        "https://github.com/VirtualDrivers/Virtual-Display-Driver/releases/download/25.7.23/VirtualDisplayDriver-x86.Driver.Only.zip";
    private const string DriverAssetSha256 =
        "E24210692B442B39AF763536330CE78B423F19342B7A7792C26DE3944E418B3A";
    private const string DriverHardwareId = @"Root\MttVDD";
    private const string DriverInfName = "MttVDD.inf";
    private const string DriverCatalogName = "mttvdd.cat";
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

    private readonly InteractiveProcessLauncher _interactiveProcessLauncher;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, DateTimeOffset> _leases = new(StringComparer.OrdinalIgnoreCase);
    private readonly Timer _leaseTimer;
    private string? _virtualDeviceInstanceId;
    private string? _virtualDeviceName;
    private AgentDisplayInfo? _virtualDisplay;
    private IReadOnlyList<AgentDisplayInfo> _lastDisplays = Array.Empty<AgentDisplayInfo>();
    private bool _disposed;

    public VirtualDisplayService(InteractiveProcessLauncher interactiveProcessLauncher)
    {
        _interactiveProcessLauncher = interactiveProcessLauncher;
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

        await _gate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            PruneExpiredLeasesUnsafe();

            switch (request.Operation?.Trim().ToLowerInvariant())
            {
                case "create":
                {
                    var width = Math.Clamp(request.Width, 800, 7680);
                    var height = Math.Clamp(request.Height, 600, 4320);
                    _leases[controllerId] = DateTimeOffset.UtcNow;

                    if (string.IsNullOrWhiteSpace(_virtualDeviceInstanceId) || _virtualDisplay is null)
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

                    if (_virtualDisplay is null || _lastDisplays.Count < 2 || _virtualDisplay.VncMonitorIndex < 1)
                    {
                        _leases.Remove(controllerId);
                        ReleaseDeviceUnsafe();
                        return Snapshot(false, "Windows did not expose Grev Screen 2 as a usable extended desktop display.");
                    }

                    return Snapshot(true,
                        $"Screen 2 ready: {_virtualDisplay.Width}x{_virtualDisplay.Height} on VNC monitor {_virtualDisplay.VncMonitorIndex}.");
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

        var infPath = await EnsureDriverAsync(cancellationToken);
        _virtualDeviceInstanceId = LegacyVirtualDisplayDevice.Create(
            infPath,
            DriverHardwareId,
            "GrevVirtualDisplay",
            "Grev Virtual Display");
        PersistOwnedDevice(_virtualDeviceInstanceId);

        // The Agent service runs in Session 0. Windows display APIs such as EnumDisplayDevices
        // enumerate the current session, so all desktop discovery/attach work must happen inside
        // the logged-in interactive Windows session rather than in this service process.
        var interactiveResult = await _interactiveProcessLauncher.AttachDisplayAsync(
            width,
            height,
            cancellationToken);

        if (!interactiveResult.Success)
        {
            var pnp = LegacyVirtualDisplayDevice.DescribeStatus(_virtualDeviceInstanceId);
            throw new InvalidOperationException(interactiveResult.Message + " " + pnp);
        }

        _virtualDeviceName = interactiveResult.VirtualDeviceName;
        _lastDisplays = interactiveResult.Displays;
        _virtualDisplay = _lastDisplays.FirstOrDefault(display =>
            string.Equals(display.DeviceName, _virtualDeviceName, StringComparison.OrdinalIgnoreCase))
            ?? _lastDisplays.FirstOrDefault(display => display.IsVirtual);

        if (_virtualDisplay is null || _lastDisplays.Count < 2 || _virtualDisplay.VncMonitorIndex < 1)
        {
            throw new InvalidOperationException(
                "The interactive Windows session attached Screen 2 but did not return a usable VNC monitor index. " +
                interactiveResult.Message);
        }
    }

    private static async Task<string> EnsureDriverAsync(CancellationToken cancellationToken)
    {
        var cachedInf = FindCachedDriverFile(DriverInfName);
        var cachedCatalog = FindCachedDriverFile(DriverCatalogName);
        if (cachedInf is not null && cachedCatalog is not null)
        {
            TrustDriverCatalog(cachedCatalog);
            return cachedInf;
        }

        var parent = Directory.GetParent(DriverCacheDirectory)?.FullName
            ?? throw new InvalidOperationException("Grev could not resolve the Screen 2 driver cache directory.");
        Directory.CreateDirectory(parent);

        var staging = DriverCacheDirectory + ".staging";
        if (Directory.Exists(staging))
            Directory.Delete(staging, true);
        Directory.CreateDirectory(staging);

        try
        {
            var zipPath = Path.Combine(staging, "driver.zip");
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

            var actualHash = Convert.ToHexString(
                SHA256.HashData(await File.ReadAllBytesAsync(zipPath, cancellationToken)));
            if (!string.Equals(actualHash, DriverAssetSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"The Screen 2 driver download failed integrity verification. Expected {DriverAssetSha256}, got {actualHash}.");
            }

            var payload = Path.Combine(staging, "payload");
            ZipFile.ExtractToDirectory(zipPath, payload, true);

            var inf = Directory.EnumerateFiles(payload, DriverInfName, SearchOption.AllDirectories).FirstOrDefault();
            var catalog = Directory.EnumerateFiles(payload, DriverCatalogName, SearchOption.AllDirectories).FirstOrDefault();
            if (inf is null || catalog is null)
                throw new InvalidOperationException("The verified Screen 2 package is missing its INF or catalog.");

            TrustDriverCatalog(catalog);
            if (Directory.Exists(DriverCacheDirectory))
                Directory.Delete(DriverCacheDirectory, true);
            Directory.Move(payload, DriverCacheDirectory);

            return FindCachedDriverFile(DriverInfName)
                ?? throw new InvalidOperationException("Grev cached the Screen 2 driver but could not find its INF.");
        }
        finally
        {
            try
            {
                if (Directory.Exists(staging))
                    Directory.Delete(staging, true);
            }
            catch { }
        }
    }

    private static string? FindCachedDriverFile(string fileName)
    {
        if (!Directory.Exists(DriverCacheDirectory))
            return null;
        return Directory.EnumerateFiles(DriverCacheDirectory, fileName, SearchOption.AllDirectories).FirstOrDefault();
    }

    private static void TrustDriverCatalog(string catalogPath)
    {
        var certificates = new X509Certificate2Collection();
#pragma warning disable SYSLIB0057
        certificates.Import(File.ReadAllBytes(catalogPath));
#pragma warning restore SYSLIB0057
        if (certificates.Count == 0)
            throw new InvalidOperationException("The Screen 2 driver catalog did not contain a publisher certificate.");

        using var store = new X509Store(StoreName.TrustedPublisher, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadWrite);
        foreach (var certificate in certificates)
        {
            if (string.IsNullOrWhiteSpace(certificate.Thumbprint))
                continue;
            if (store.Certificates.Find(X509FindType.FindByThumbprint, certificate.Thumbprint, false).Count == 0)
                store.Add(certificate);
        }
    }

    private AgentDisplayResponse Snapshot(bool success, string message)
    {
        var active = !string.IsNullOrWhiteSpace(_virtualDeviceInstanceId) &&
                     _virtualDisplay is not null &&
                     _lastDisplays.Count >= 2;

        return new AgentDisplayResponse(
            success,
            message,
            active,
            _virtualDisplay?.DeviceName ?? _virtualDeviceName,
            _virtualDisplay?.VncMonitorIndex ?? -1,
            _lastDisplays);
    }

    private void CleanupStaleOwnedDevice()
    {
        try
        {
            if (!File.Exists(OwnedDeviceStatePath)) return;
            var instanceId = File.ReadAllText(OwnedDeviceStatePath).Trim();
            if (!string.IsNullOrWhiteSpace(instanceId))
                LegacyVirtualDisplayDevice.TryRemove(instanceId);
            File.Delete(OwnedDeviceStatePath);
        }
        catch { }
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
        catch { }
    }

    private void ExpireLeases()
    {
        if (_disposed || !_gate.Wait(0)) return;
        try
        {
            PruneExpiredLeasesUnsafe();
            if (_leases.Count == 0 && !string.IsNullOrWhiteSpace(_virtualDeviceInstanceId))
                ReleaseDeviceUnsafe();
        }
        catch { }
        finally { _gate.Release(); }
    }

    private void PruneExpiredLeasesUnsafe()
    {
        var cutoff = DateTimeOffset.UtcNow - LeaseLifetime;
        foreach (var id in _leases.Where(item => item.Value < cutoff).Select(item => item.Key).ToArray())
            _leases.Remove(id);
    }

    private void ReleaseDeviceUnsafe()
    {
        if (!string.IsNullOrWhiteSpace(_virtualDeviceInstanceId))
        {
            LegacyVirtualDisplayDevice.TryRemove(_virtualDeviceInstanceId);
            _virtualDeviceInstanceId = null;
        }

        _virtualDeviceName = null;
        _virtualDisplay = null;
        _lastDisplays = Array.Empty<AgentDisplayInfo>();
        ClearOwnedDeviceState();
    }

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
}