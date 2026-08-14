using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using GrevUltraVNC.Contracts;
using GrevUltraVNC.Models;

namespace GrevUltraVNC.Services;

public sealed record GrevConnectResolution(
    string Address,
    string Route,
    string? ConnectId,
    string? MachineName,
    AgentPingResponse? Ping = null);

public sealed class GrevConnectResolver : IDisposable
{
    private static readonly TimeSpan DiscoveryCacheLifetime = TimeSpan.FromSeconds(20);

    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _discoveryGate = new(1, 1);
    private readonly Dictionary<int, DiscoverySnapshot> _discoveryCache = [];

    public GrevConnectResolver()
    {
        var handler = new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromMilliseconds(450),
            UseProxy = false
        };
        _httpClient = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
    }

    public async Task<GrevConnectResolution?> ResolveAsync(Machine machine, CancellationToken cancellationToken = default)
    {
        var expectedId = machine.ConnectId?.Trim() ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(machine.ResolvedAddress))
        {
            var cached = await ProbeAsync(machine.ResolvedAddress, machine.AgentPort, cancellationToken, 550);
            if (MatchesExpected(cached, expectedId))
                return Apply(machine, machine.ResolvedAddress, machine.ResolvedRoute, cached!);
        }

        if (!string.IsNullOrWhiteSpace(machine.IpAddress))
        {
            var saved = await ProbeAsync(machine.IpAddress, machine.AgentPort, cancellationToken, 700);
            if (MatchesExpected(saved, expectedId))
                return Apply(machine, machine.IpAddress, "LAN", saved!);
        }

        if (string.IsNullOrWhiteSpace(expectedId))
        {
            machine.ResolvedAddress = null;
            machine.ResolvedRoute = string.Empty;
            return null;
        }

        var snapshot = await GetDiscoverySnapshotAsync(machine.AgentPort, cancellationToken);
        var discovered = snapshot.Results.FirstOrDefault(result => GrevConnectId.Equals(result.ConnectId, expectedId));
        if (discovered is null)
        {
            machine.ResolvedAddress = null;
            machine.ResolvedRoute = string.Empty;
            return null;
        }

        machine.ResolvedAddress = discovered.Address;
        machine.ResolvedRoute = discovered.Route;
        return discovered;
    }

    public async Task<IReadOnlyList<GrevConnectResolution>> DiscoverAllAsync(
        int agentPort = AgentProtocol.DefaultPort,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await GetDiscoverySnapshotAsync(agentPort, cancellationToken);
        return snapshot.Results;
    }

    private async Task<DiscoverySnapshot> GetDiscoverySnapshotAsync(int port, CancellationToken cancellationToken)
    {
        if (_discoveryCache.TryGetValue(port, out var cached) && cached.ExpiresAtUtc > DateTimeOffset.UtcNow)
            return cached;

        await _discoveryGate.WaitAsync(cancellationToken);
        try
        {
            if (_discoveryCache.TryGetValue(port, out cached) && cached.ExpiresAtUtc > DateTimeOffset.UtcNow)
                return cached;

            var results = new Dictionary<string, GrevConnectResolution>(StringComparer.OrdinalIgnoreCase);
            foreach (var subnet in GetCandidateSubnets())
            {
                foreach (var result in await ScanSubnetAsync(subnet, port, cancellationToken))
                {
                    var key = string.IsNullOrWhiteSpace(result.ConnectId) ? result.Address : result.ConnectId!;
                    results.TryAdd(key, result);
                }
            }

            var snapshot = new DiscoverySnapshot(
                DateTimeOffset.UtcNow.Add(DiscoveryCacheLifetime),
                results.Values
                    .OrderBy(item => item.ConnectId ?? item.Address, StringComparer.OrdinalIgnoreCase)
                    .ToArray());
            _discoveryCache[port] = snapshot;
            return snapshot;
        }
        finally
        {
            _discoveryGate.Release();
        }
    }

    private async Task<IReadOnlyList<GrevConnectResolution>> ScanSubnetAsync(
        CandidateSubnet subnet,
        int port,
        CancellationToken cancellationToken)
    {
        var found = new List<GrevConnectResolution>();
        using var gate = new SemaphoreSlim(64, 64);
        var tasks = new List<Task>(253);

        for (var host = 1; host <= 254; host++)
        {
            var address = $"{subnet.A}.{subnet.B}.{subnet.C}.{host}";
            if (string.Equals(address, subnet.LocalAddress, StringComparison.Ordinal))
                continue;

            tasks.Add(Task.Run(async () =>
            {
                await gate.WaitAsync(cancellationToken);
                try
                {
                    var ping = await ProbeAsync(address, port, cancellationToken, 350);
                    if (ping is null || string.IsNullOrWhiteSpace(ping.ConnectId))
                        return;

                    lock (found)
                    {
                        found.Add(new GrevConnectResolution(
                            address,
                            subnet.Route,
                            ping.ConnectId,
                            ping.MachineName,
                            ping));
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                }
                finally
                {
                    gate.Release();
                }
            }, CancellationToken.None));
        }

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }

        return found.ToArray();
    }

    private async Task<AgentPingResponse?> ProbeAsync(
        string address,
        int port,
        CancellationToken cancellationToken,
        int timeoutMilliseconds)
    {
        if (!IPAddress.TryParse(address, out var parsed) || parsed.AddressFamily != AddressFamily.InterNetwork)
            return null;

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMilliseconds(timeoutMilliseconds));
            var ping = await _httpClient.GetFromJsonAsync<AgentPingResponse>(
                $"http://{address}:{port}{AgentProtocol.PingPath}",
                cancellationToken: timeout.Token);
            return ping is not null && string.Equals(ping.Product, "GrevUltraVNC Agent", StringComparison.OrdinalIgnoreCase)
                ? ping
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool MatchesExpected(AgentPingResponse? ping, string expectedId)
    {
        if (ping is null) return false;
        if (string.IsNullOrWhiteSpace(expectedId)) return true;
        return GrevConnectId.Equals(ping.ConnectId, expectedId);
    }

    private static GrevConnectResolution Apply(Machine machine, string address, string route, AgentPingResponse ping)
    {
        machine.ResolvedAddress = address;
        machine.ResolvedRoute = string.IsNullOrWhiteSpace(route) ? "Grev Connect" : route;
        if (string.IsNullOrWhiteSpace(machine.ConnectId) && !string.IsNullOrWhiteSpace(ping.ConnectId))
            machine.ConnectId = ping.ConnectId;
        return new GrevConnectResolution(address, machine.ResolvedRoute, ping.ConnectId, ping.MachineName, ping);
    }

    private static IReadOnlyList<CandidateSubnet> GetCandidateSubnets()
    {
        var results = new Dictionary<string, CandidateSubnet>(StringComparer.OrdinalIgnoreCase);

        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (adapter.OperationalStatus != OperationalStatus.Up || adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;

            IPInterfaceProperties properties;
            try { properties = adapter.GetIPProperties(); }
            catch { continue; }

            foreach (var unicast in properties.UnicastAddresses)
            {
                var address = unicast.Address;
                if (address.AddressFamily != AddressFamily.InterNetwork || !IsPrivateOrOverlay(address))
                    continue;

                var bytes = address.GetAddressBytes();
                var key = $"{bytes[0]}.{bytes[1]}.{bytes[2]}";
                if (results.ContainsKey(key)) continue;

                var adapterText = $"{adapter.Name} {adapter.Description}";
                var route = adapterText.Contains("zima", StringComparison.OrdinalIgnoreCase) ||
                            adapterText.Contains("zerotier", StringComparison.OrdinalIgnoreCase)
                    ? "Zima / Grev Connect"
                    : "Grev Connect";

                results[key] = new CandidateSubnet(bytes[0], bytes[1], bytes[2], address.ToString(), route);
                if (results.Count >= 8)
                    return results.Values.ToArray();
            }
        }

        return results.Values.ToArray();
    }

    private static bool IsPrivateOrOverlay(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes[0] == 10 ||
               (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) ||
               (bytes[0] == 192 && bytes[1] == 168) ||
               (bytes[0] == 100 && bytes[1] is >= 64 and <= 127);
    }

    public void Dispose()
    {
        _discoveryGate.Dispose();
        _httpClient.Dispose();
    }

    private sealed record CandidateSubnet(byte A, byte B, byte C, string LocalAddress, string Route);
    private sealed record DiscoverySnapshot(DateTimeOffset ExpiresAtUtc, IReadOnlyList<GrevConnectResolution> Results);
}
