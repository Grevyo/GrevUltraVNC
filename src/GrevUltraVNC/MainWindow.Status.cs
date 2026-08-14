using System.Threading;
using System.Windows;

namespace GrevUltraVNC;

public partial class MainWindow
{
    private async void StatusTimer_Tick(object? sender, EventArgs e) => await RefreshStatusesAsync();

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshStatusesAsync();

    private async Task RefreshStatusesAsync()
    {
        if (_statusRefreshRunning) return;
        _statusRefreshRunning = true;
        var identityChanged = 0;

        try
        {
            FooterStatus.Text = $"Resolving and checking {Machines.Count} machine{(Machines.Count == 1 ? string.Empty : "s")}…";
            var checkedAt = DateTime.Now;

            var probes = Machines.Select(async machine =>
            {
                machine.Status = Models.MachineStatus.Checking;
                machine.AgentState = Models.GrevAgentState.Unknown;
                machine.AgentStatus = null;
                machine.AgentMessage = null;
                var originalConnectId = machine.ConnectId;

                await _connectResolver.ResolveAsync(machine);

                var networkTask = _network.ProbeAsync(machine);
                var agentTask = _agent.ProbeAsync(machine);
                await Task.WhenAll(networkTask, agentTask);

                var networkResult = await networkTask;
                var agentResult = await agentTask;

                machine.LatencyMs = networkResult.LatencyMs;
                machine.VncAvailable = networkResult.VncAvailable;
                machine.LastCheckedAt = checkedAt;
                machine.Status = networkResult.Status;
                machine.AgentStatus = agentResult.Status;
                machine.AgentMessage = agentResult.Message;
                machine.AgentState = agentResult.State;

                if (!string.Equals(originalConnectId, machine.ConnectId, StringComparison.Ordinal))
                    Interlocked.Exchange(ref identityChanged, 1);
            });

            await Task.WhenAll(probes);
            if (identityChanged != 0)
                await _storage.SaveMachinesAsync(Machines);

            FooterStatus.Text = $"Last checked {DateTime.Now:HH:mm:ss}";
            RefreshMachineView();
        }
        finally
        {
            _statusRefreshRunning = false;
        }
    }
}
