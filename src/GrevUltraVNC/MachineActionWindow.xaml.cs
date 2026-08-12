using System.Diagnostics;
using System.Windows;
using GrevUltraVNC.Models;
using GrevUltraVNC.Services;

namespace GrevUltraVNC;

public partial class MachineActionWindow : Window
{
    private readonly Machine _machine;
    private readonly AppSettings _settings;
    private readonly UltraVncSessionService _vnc;
    private readonly WakeOnLanService _wake = new();
    private readonly PowerService _power = new();
    private readonly NetworkStatusService _network = new();
    private readonly RemoteUltraVncService _remoteVnc = new();
    private readonly VncCredentialService _credentials = new();
    private readonly AgentCredentialService _agentCredentials = new();
    private readonly GrevAgentClient _agent = new();
    private readonly GrevConnectResolver _connectResolver = new();
    private readonly AgentUpdateService _agentUpdater;
    private bool _agentUpdateRunning;

    public bool MachineChanged { get; private set; }
    public bool MachineDeleted { get; private set; }

    public MachineActionWindow(Machine machine, AppSettings settings, UltraVncSessionService vnc)
    {
        InitializeComponent();
        _machine = machine;
        _settings = settings;
        _vnc = vnc;
        _agentUpdater = new AgentUpdateService(_agent);
        Closed += (_, _) =>
        {
            _agent.Dispose();
            _connectResolver.Dispose();
        };
        RefreshHeader();
    }

    private void RefreshHeader()
    {
        MachineNameText.Text = _machine.Name;
        MachineAddressText.Text = $"{_machine.ConnectDisplayText}  ·  VNC {_machine.VncPort}";
    }

    private async Task EnsureRouteAsync()
    {
        await _connectResolver.ResolveAsync(_machine);
        RefreshHeader();
        if (string.IsNullOrWhiteSpace(_machine.ActiveAddress))
            throw new InvalidOperationException($"{_machine.ConnectId} could not be found on the current LAN or Grev Connect networks.");
    }

    private async void Vnc_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await EnsureRouteAsync();
            _vnc.Launch(_machine, _settings);
            var controlPanel = new GrevControlPanelWindow(_machine, _vnc);
            controlPanel.Show();
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not open VNC", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Cad_Click(object sender, RoutedEventArgs e) => SendRemoteKey(() => _vnc.SendCtrlAltDelete(_machine.Id));
    private void WindowsKey_Click(object sender, RoutedEventArgs e) => SendRemoteKey(() => _vnc.SendWindowsKey(_machine.Id));
    private void TaskManager_Click(object sender, RoutedEventArgs e) => SendRemoteKey(() => _vnc.SendCtrlShiftEscape(_machine.Id));

    private void SendRemoteKey(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Remote key", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private async void Wake_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _wake.SendAsync(_machine.MacAddress);
            MessageBox.Show(this, "Wake-on-LAN packet sent.", "GrevUltraVNC", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Wake-on-LAN", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void Restart_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, $"Restart {_machine.Name} now?", "Confirm restart", MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        try
        {
            await EnsureRouteAsync();
            var result = await _power.RestartAsync(_machine.ActiveAddress);
            MessageBox.Show(this, result.Message, "Restart", MessageBoxButton.OK,
                result.Success ? MessageBoxImage.Information : MessageBoxImage.Error);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Restart", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Shutdown_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, $"Shut down {_machine.Name} now?", "Confirm shutdown", MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        try
        {
            await EnsureRouteAsync();
            var result = await _power.ShutdownAsync(_machine.ActiveAddress);
            MessageBox.Show(this, result.Message, "Shut down", MessageBoxButton.OK,
                result.Success ? MessageBoxImage.Information : MessageBoxImage.Error);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Shut down", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void StartVncService_Click(object sender, RoutedEventArgs e) =>
        await RunVncServiceActionAsync(host => _remoteVnc.StartAsync(host), "Start UltraVNC");

    private async void RestartVncService_Click(object sender, RoutedEventArgs e)
    {
        if (_vnc.HasActiveSession(_machine.Id) && MessageBox.Show(this,
                "Restarting UltraVNC will normally disconnect the active VNC session. Continue?",
                "Restart UltraVNC", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        await RunVncServiceActionAsync(host => _remoteVnc.RestartAsync(host), "Restart UltraVNC");
    }

    private async void StopVncService_Click(object sender, RoutedEventArgs e)
    {
        if (_vnc.HasActiveSession(_machine.Id) && MessageBox.Show(this,
                "Stopping UltraVNC will disconnect the active VNC session. Continue?",
                "Stop UltraVNC", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        await RunVncServiceActionAsync(host => _remoteVnc.StopAsync(host), "Stop UltraVNC");
    }

    private async Task RunVncServiceActionAsync(Func<string, Task<RemoteServiceResult>> action, string title)
    {
        try
        {
            await EnsureRouteAsync();
            var result = await action(_machine.ActiveAddress);
            MessageBox.Show(this, result.Message, title, MessageBoxButton.OK,
                result.Success ? MessageBoxImage.Information : MessageBoxImage.Error);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void EnableVncAutoStart_Click(object sender, RoutedEventArgs e) =>
        await RunVncServiceActionAsync(host => _remoteVnc.EnableAutoStartAndStartAsync(host), "UltraVNC at boot");

    private void Overview_Click(object sender, RoutedEventArgs e)
    {
        var overview = new MachineOverviewWindow(_machine, _vnc) { Owner = this };
        overview.ShowDialog();
    }

    private async void UpdateAgent_Click(object sender, RoutedEventArgs e)
    {
        if (_agentUpdateRunning) return;

        if (MessageBox.Show(this,
                $"Update Grev Agent on {_machine.Name} from the latest GitHub release?\n\nThe Agent service will restart. The existing pairing key will be preserved.",
                "Update Grev Agent",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        _agentUpdateRunning = true;
        UpdateAgentButton.IsEnabled = false;
        AgentUpdateStatusText.Text = "Preparing Agent update…";

        try
        {
            await EnsureRouteAsync();
            var progress = new Progress<string>(message => AgentUpdateStatusText.Text = message);
            var result = await _agentUpdater.UpdateFromGitHubAsync(_machine, progress);
            _machine.AgentState = result.State;
            _machine.AgentStatus = result.Status;
            _machine.AgentMessage = result.Message;
            AgentUpdateStatusText.Text = "Grev Agent is up to date and responding.";

            MessageBox.Show(this,
                $"Grev Agent on {_machine.Name} updated successfully and is responding again.",
                "Grev Agent updated",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            AgentUpdateStatusText.Text = ex.Message;
            MessageBox.Show(this, ex.Message, "Grev Agent update", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _agentUpdateRunning = false;
            UpdateAgentButton.IsEnabled = true;
        }
    }

    private async void Shares_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await EnsureRouteAsync();
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\\\\{_machine.ActiveAddress}\\",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Network shares", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Diagnostics_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _connectResolver.ResolveAsync(_machine);
            var networkResult = await _network.ProbeAsync(_machine);
            var serviceResult = string.IsNullOrWhiteSpace(_machine.ActiveAddress)
                ? new RemoteServiceResult(false, "No current route")
                : await _remoteVnc.QueryAsync(_machine.ActiveAddress);
            var agentResult = string.IsNullOrWhiteSpace(_machine.ActiveAddress)
                ? new GrevAgentProbeResult(GrevAgentState.NotDetected, Message: "No current route")
                : await _agent.ProbeAsync(_machine);
            var latency = networkResult.LatencyMs is null ? "No ping response" : $"{networkResult.LatencyMs} ms";
            var vnc = networkResult.VncAvailable ? $"Reachable on TCP {_machine.VncPort}" : $"Not reachable on TCP {_machine.VncPort}";
            var service = serviceResult.Success ? serviceResult.Message : $"Could not query: {serviceResult.Message}";
            var agent = agentResult.Status is null
                ? $"{agentResult.State}: {agentResult.Message}"
                : $"Connected · Agent {agentResult.Status.AgentVersion} · CPU {agentResult.Status.CpuUsagePercent:0.#}%";

            MessageBox.Show(this,
                $"Machine: {_machine.Name}\nGrev Connect ID: {(_machine.ConnectId.Length == 0 ? "Not assigned" : _machine.ConnectId)}\nRoute: {(_machine.ActiveAddress.Length == 0 ? "Unavailable" : $"{_machine.ResolvedRoute} · {_machine.ActiveAddress}")}\nLAN IP: {_machine.IpAddress}\nPing: {latency}\nVNC port: {vnc}\nService: {service}\nGrev Agent: {agent}\nProbe result: {networkResult.Status}",
                "Connection diagnostics", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Connection diagnostics", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new MachineDialog(_machine) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        MachineChanged = true;
        RefreshHeader();
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, $"Remove {_machine.Name} from GrevUltraVNC?", "Remove machine",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        var cleanupErrors = new List<string>();
        try { _credentials.Delete(_machine.Id); }
        catch (Exception ex) { cleanupErrors.Add($"VNC credential: {ex.Message}"); }

        try { _agentCredentials.Delete(_machine.Id); }
        catch (Exception ex) { cleanupErrors.Add($"Agent credential: {ex.Message}"); }

        if (cleanupErrors.Count > 0)
        {
            MessageBox.Show(this,
                "The machine will be removed, but some saved credentials could not be deleted:\n\n" + string.Join("\n", cleanupErrors),
                "Credential cleanup", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        MachineDeleted = true;
        Close();
    }
}
