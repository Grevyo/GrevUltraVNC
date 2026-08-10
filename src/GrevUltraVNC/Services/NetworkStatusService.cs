using System.Net.NetworkInformation;
using System.Net.Sockets;
using GrevUltraVNC.Models;

namespace GrevUltraVNC.Services;

public sealed record MachineProbeResult(MachineStatus Status, long? LatencyMs, bool VncAvailable);

public sealed class NetworkStatusService
{
    public async Task<MachineProbeResult> ProbeAsync(Machine machine, CancellationToken cancellationToken = default)
    {
        var pingTask = PingAsync(machine.IpAddress, cancellationToken);
        var vncTask = IsPortOpenAsync(machine.IpAddress, machine.VncPort, cancellationToken);
        await Task.WhenAll(pingTask, vncTask);

        var (pingOk, latency) = await pingTask;
        var vncOk = await vncTask;

        var status = vncOk
            ? MachineStatus.Online
            : pingOk
                ? MachineStatus.VncUnavailable
                : MachineStatus.Offline;

        return new MachineProbeResult(status, latency, vncOk);
    }

    private static async Task<(bool Ok, long? LatencyMs)> PingAsync(string host, CancellationToken cancellationToken)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(host, TimeSpan.FromMilliseconds(900), cancellationToken: cancellationToken);
            return reply.Status == IPStatus.Success ? (true, reply.RoundtripTime) : (false, null);
        }
        catch
        {
            return (false, null);
        }
    }

    private static async Task<bool> IsPortOpenAsync(string host, int port, CancellationToken cancellationToken)
    {
        try
        {
            using var client = new TcpClient();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMilliseconds(900));
            await client.ConnectAsync(host, port, timeout.Token);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
