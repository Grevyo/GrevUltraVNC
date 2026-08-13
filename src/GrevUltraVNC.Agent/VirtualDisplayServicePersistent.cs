using GrevUltraVNC.Contracts;

namespace GrevUltraVNC.Agent;

public sealed class VirtualDisplayService : IDisposable
{
    private const string DriverVersion = "25.7.23";
    private const string DriverHardwareId = @"Root\MttVDD";
    private const string DriverInfName = "MttVDD.inf";
    private const int SecondaryMonitorIndex = 1;

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
    private int _width = 1920;
    private int _height = 1080;
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

        await _gate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            PruneExpiredLeasesUnsafe();

            switch (request.Operation?.Trim().ToLowerInvariant())
            {
                case "create":
                    _width = Math.Clamp(request.Width, 800, 7680);
                    _height = Math.Clamp(request.Height, 600, 4320);
                    _leases[controllerId] = DateTimeOffset.UtcNow;

                    if (string.IsNullOrWhiteSpace(_virtualDeviceInstanceId))
                    {
                        try
                        {
                            await CreateDeviceAsync(cancellationToken);
                        }
                        catch
                        {
                            _leases.Remove(controllerId);
                            ReleaseDeviceUnsafe();
                            throw;
                        }
                    }

                    return Snapshot(true,
                        "Screen 2 was created and will remain active while the Grev viewer lease is alive.");

                case "heartbeat":
                    if (_leases.ContainsKey(controllerId))
                        _leases[controllerId] = DateTimeOffset.UtcNow;
                    return Snapshot(true, "Screen 2 lease refreshed.");

                case "release":
                    _leases.Remove(controllerId);
                    if (_leases.Count == 0)
                        ReleaseDeviceUnsafe();
                    return Snapshot(true,
                        _leases.Count == 0 ? "Screen 2 removed." : "Screen 2 is still in use.");

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

    private async Task CreateDeviceAsync(CancellationToken cancellationToken)
    {
        ReleaseDeviceUnsafe();

        var infPath = FindCachedDriverInf();
        if (infPath is null)
        {
            throw new InvalidOperationException(
                $"The signed Screen 2 driver cache was not found under {DriverCacheDirectory}. " +
                "Run Screen 2 once with the previous Agent or reinstall the Grev Agent package so the driver can be cached.");
        }

        _virtualDeviceInstanceId = LegacyVirtualDisplayDevice.Create(
            infPath,
            DriverHardwareId,
            "GrevVirtualDisplay",
            "Grev Virtual Display");

        Directory.CreateDirectory(AgentDataDirectory);
        File.WriteAllText(OwnedDeviceStatePath, _virtualDeviceInstanceId);

        // Windows is already showing the new virtual monitor on the interactive desktop at this
        // point. Do not use Session-0 GDI enumeration as a success condition; that was causing
        // Grev to remove a healthy monitor after it had visibly appeared.
        await Task.Delay(1500, cancellationToken);
    }

    private static string? FindCachedDriverInf()
    {
        if (!Directory.Exists(DriverCacheDirectory))
            return null;

        return Directory.EnumerateFiles(
                DriverCacheDirectory,
                DriverInfName,
                SearchOption.AllDirectories)
            .FirstOrDefault();
    }

    private AgentDisplayResponse Snapshot(bool success, string message)
    {
        var active = !string.IsNullOrWhiteSpace(_virtualDeviceInstanceId);
        IReadOnlyList<AgentDisplayInfo> displays = active
            ? new[]
            {
                new AgentDisplayInfo(
                    "Grev Screen 2",
                    "Grev Virtual Display",
                    0,
                    0,
                    _width,
                    _height,
                    false,
                    true,
                    SecondaryMonitorIndex)
            }
            : Array.Empty<AgentDisplayInfo>();

        return new AgentDisplayResponse(
            success,
            message,
            active,
            active ? "Grev Screen 2" : null,
            active ? SecondaryMonitorIndex : -1,
            displays);
    }

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
        }
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
        if (_disposed || !_gate.Wait(0))
            return;

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
        foreach (var id in _leases
                     .Where(item => item.Value < cutoff)
                     .Select(item => item.Key)
                     .ToArray())
        {
            _leases.Remove(id);
        }
    }

    private void ReleaseDeviceUnsafe()
    {
        if (!string.IsNullOrWhiteSpace(_virtualDeviceInstanceId))
        {
            LegacyVirtualDisplayDevice.TryRemove(_virtualDeviceInstanceId);
            _virtualDeviceInstanceId = null;
        }

        ClearOwnedDeviceState();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(VirtualDisplayService));
    }

    public void Dispose()
    {
        if (_disposed)
            return;

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
