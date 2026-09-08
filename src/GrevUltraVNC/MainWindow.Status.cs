using System.Threading;
using System.Windows;
using GrevUltraVNC.Models;
using GrevUltraVNC.Services;

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
            SetFooterState(
                $"Resolving routes and checking {Machines.Count} machine{(Machines.Count == 1 ? string.Empty : "s")}…",
                "AccentBrush");
            var checkedAt = DateTime.Now;

            var probes = Machines.Select(async machine =>
            {
                machine.Status = MachineStatus.Checking;
                machine.AgentState = GrevAgentState.Unknown;
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

            RefreshMachineView();
            UpdateViewerStatusText();

            // The footer light mirrors the worst thing on the dashboard, so a problem is
            // visible even when the offending card has scrolled out of sight.
            var unreachable = Machines.Count(machine => machine.Status == MachineStatus.Offline);
            if (Machines.Count == 0)
                SetFooterState("No machines configured yet", "IdleBrush");
            else if (unreachable == 0)
                SetFooterState($"All {Machines.Count} reachable · checked {DateTime.Now:HH:mm:ss}", "OkBrush");
            else
                SetFooterState(
                    $"{unreachable} of {Machines.Count} unreachable · checked {DateTime.Now:HH:mm:ss}",
                    "WarnBrush");
        }
        finally
        {
            _statusRefreshRunning = false;
        }
    }

    private void SetFooterState(string message, string brushKey)
    {
        FooterStatus.Text = message;
        FooterPulse.Fill = ThemeService.ThemeBrush(brushKey);
    }
}
