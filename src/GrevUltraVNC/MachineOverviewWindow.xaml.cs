using System.Windows;
using System.Windows.Media;
using GrevUltraVNC.Contracts;
using GrevUltraVNC.Models;
using GrevUltraVNC.Services;

namespace GrevUltraVNC;

public partial class MachineOverviewWindow : Window
{
    private readonly Machine _machine;
    private readonly UltraVncSessionService? _vnc;
    private readonly GrevAgentClient _agent = new();
    private readonly AgentUpdateService _agentUpdater;
    private readonly AdminWorkspaceStorage _workspace = new();
    private readonly CancellationTokenSource _windowCancellation = new();
    private IReadOnlyList<AgentProcessInfo> _processes = [];
    private IReadOnlyList<AgentServiceInfo> _services = [];
    private List<SavedCommand> _savedCommands = [];
    private bool _processesLoaded;
    private bool _servicesLoaded;
    private bool _refreshing;
    private bool _actionRunning;
    private bool _commandRunning;
    private bool _agentUpdateRunning;

    public MachineOverviewWindow(Machine machine, UltraVncSessionService? vnc = null)
    {
        InitializeComponent();
        _machine = machine;
        _vnc = vnc;
        _agentUpdater = new AgentUpdateService(_agent);

        MachineNameText.Text = machine.Name;
        MachineAddressText.Text = $"{machine.ConnectDisplayText}  ·  Agent {machine.AgentPort}  ·  VNC {machine.VncPort}";
        RefreshScreenButton.IsEnabled = _vnc?.HasActiveSession(machine.Id) == true;

        Loaded += MachineOverviewWindow_Loaded;
        Closed += MachineOverviewWindow_Closed;
    }

    private async void MachineOverviewWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadSavedCommandsAsync();
        await RefreshVisibleDataAsync();
    }

    private void MachineOverviewWindow_Closed(object? sender, EventArgs e)
    {
        _windowCancellation.Cancel();
        _agent.Dispose();
        _windowCancellation.Dispose();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshVisibleDataAsync();

    private async Task RefreshVisibleDataAsync()
    {
        if (_refreshing) return;
        _refreshing = true;
        StatusText.Text = "Refreshing live Agent data…";
        AgentStateText.Text = "● CHECKING AGENT";
        AgentStateText.Foreground = ThemeService.ThemeBrush("MutedTextBrush");
        UpdateAgentButton.IsEnabled = false;
        SessionActionsPanel.IsEnabled = false;
        SessionFeatureStatusText.Text = "Checking Agent capability…";
        RefreshScreenButton.IsEnabled = _vnc?.HasActiveSession(_machine.Id) == true;

        var token = _windowCancellation.Token;
        var loadProcesses = ProcessesPanel.Visibility == Visibility.Visible;
        var loadServices = ServicesPanel.Visibility == Visibility.Visible;

        try
        {
            var probeTask = _agent.ProbeAsync(_machine, token);
            var processesTask = loadProcesses
                ? _agent.GetProcessesAsync(_machine, token)
                : Task.FromResult(_processes);
            var servicesTask = loadServices
                ? _agent.GetServicesAsync(_machine, token)
                : Task.FromResult(_services);

            await Task.WhenAll(probeTask, processesTask, servicesTask);

            var probe = await probeTask;
            if (probe.State != GrevAgentState.Connected || probe.Status is null)
                throw new InvalidOperationException(probe.Message ?? "Grev Agent is not connected.");

            var status = probe.Status;
            if (loadProcesses)
            {
                _processes = await processesTask;
                _processesLoaded = true;
            }

            if (loadServices)
            {
                _services = await servicesTask;
                _servicesLoaded = true;
            }

            _machine.AgentState = probe.State;
            _machine.AgentStatus = probe.Status;
            _machine.AgentMessage = probe.Message;

            RenderOverview(status);
            if (_processesLoaded)
                RenderProcesses();
            if (_servicesLoaded)
                RenderServices();

            var needsUpdate = !string.IsNullOrWhiteSpace(probe.Message);
            AgentStateText.Text = needsUpdate
                ? "● AGENT UPDATE RECOMMENDED"
                : "● AGENT CONNECTED";
            AgentStateText.Foreground = ThemeService.ThemeBrush(needsUpdate ? "WarnBrush" : "OkBrush");
            UpdateAgentButton.Content = needsUpdate
                ? "⇩ Update Agent · recommended"
                : "⇩ Update Agent";
            UpdateAgentButton.IsEnabled = !_agentUpdateRunning;

            SessionActionsPanel.IsEnabled = !needsUpdate;
            SessionFeatureStatusText.Text = needsUpdate
                ? "Update Grev Agent to the current protocol before using the Windows session and power controls."
                : "Agent session controls ready · actions are authenticated with this machine's pairing key.";

            StatusText.Text = needsUpdate
                ? probe.Message!
                : $"Live data refreshed {DateTime.Now:HH:mm:ss}";
        }
        catch (OperationCanceledException) when (_windowCancellation.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            AgentStateText.Text = "● AGENT UNAVAILABLE";
            AgentStateText.Foreground = ThemeService.ThemeBrush("DangerBrush");
            UpdateAgentButton.IsEnabled = false;
            SessionActionsPanel.IsEnabled = false;
            SessionFeatureStatusText.Text = "Grev Agent must be connected before session controls can be used.";
            StatusText.Text = ex.Message;
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void RenderOverview(AgentStatusResponse status)
    {
        var cpuPercent = Math.Clamp(status.CpuUsagePercent, 0, 100);
        CpuUsageText.Text = GrevFormat.Percent(cpuPercent);
        CpuMeter.Value = cpuPercent;
        CpuMeter.Foreground = UsageBrush(cpuPercent);
        CpuNameText.Text = status.CpuName;

        var usedMemory = Math.Max(0, status.TotalMemoryBytes - status.AvailableMemoryBytes);
        var memoryPercent = GrevFormat.UsedPercent(usedMemory, status.TotalMemoryBytes);
        MemoryUsageText.Text = GrevFormat.Percent(memoryPercent);
        MemoryMeter.Value = memoryPercent;
        MemoryMeter.Foreground = UsageBrush(memoryPercent);
        MemoryDetailText.Text = $"{GrevFormat.Bytes(usedMemory)} used · {GrevFormat.Bytes(status.AvailableMemoryBytes)} free of {GrevFormat.Bytes(status.TotalMemoryBytes)}";

        UptimeText.Text = GrevFormat.Uptime(status.UptimeSeconds);
        UserText.Text = string.IsNullOrWhiteSpace(status.InteractiveUser)
            ? "Nobody signed in"
            : status.InteractiveUser;

        VncServiceText.Text = status.UltraVncServiceStatus;
        VncStateDot.Fill = ThemeService.ThemeBrush(status.UltraVncPortListening
            ? "OkBrush"
            : status.UltraVncServiceStatus.Contains("Running", StringComparison.OrdinalIgnoreCase)
                ? "WarnBrush"
                : "DangerBrush");
        VncPortText.Text = status.UltraVncPortListening
            ? $"Accepting viewers on TCP {status.UltraVncPort}"
            : $"Not listening on TCP {status.UltraVncPort} — viewers cannot connect";

        OsText.Text = status.OsDescription;
        HostText.Text = $"Host: {status.MachineName}";
        AgentVersionText.Text = $"Grev Agent {status.AgentVersion}";
        ProcessSummaryText.Text = _processesLoaded
            ? $"Processes: {_processes.Count}"
            : "Processes: open tab to load";
        ServiceSummaryText.Text = _servicesLoaded
            ? $"Services: {_services.Count}"
            : "Services: open tab to load";

        DiskItems.ItemsSource = status.Disks
            .Select(disk =>
            {
                var used = Math.Max(0, disk.TotalBytes - disk.FreeBytes);
                var percent = GrevFormat.UsedPercent(used, disk.TotalBytes);
                return new DiskRow(
                    disk.Name,
                    string.IsNullOrWhiteSpace(disk.Label) ? "Local disk" : disk.Label,
                    $"{GrevFormat.Bytes(disk.FreeBytes)} free of {GrevFormat.Bytes(disk.TotalBytes)}",
                    percent,
                    GrevFormat.Percent(percent));
            })
            // Show the disk closest to full first: that is the one that needs a decision.
            .OrderByDescending(disk => disk.Percent)
            .ToArray();
    }

    private void RenderProcesses()
    {
        var search = ProcessSearchBox.Text.Trim();
        var rows = _processes
            .Where(process => string.IsNullOrWhiteSpace(search)
                || process.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                || process.Id.ToString().Contains(search, StringComparison.OrdinalIgnoreCase))
            .Select(process => new ProcessRow(
                process.Id,
                process.Name,
                process.Id.ToString(),
                GrevFormat.Bytes(process.WorkingSetBytes),
                GrevFormat.CpuTime(process.CpuTimeMilliseconds),
                process.SessionId < 0 ? "—" : process.SessionId.ToString(),
                process.StartedAtUtc?.ToLocalTime().ToString("dd MMM HH:mm:ss") ?? "—"))
            .ToArray();

        ProcessesList.ItemsSource = rows;
        ProcessCountText.Text = rows.Length == _processes.Count
            ? $"{_processes.Count} processes"
            : $"Showing {rows.Length} of {_processes.Count}";
        ProcessSummaryText.Text = $"Processes: {_processes.Count}";
    }

    private void RenderServices()
    {
        var search = ServiceSearchBox.Text.Trim();
        var rows = _services
            .Where(service => string.IsNullOrWhiteSpace(search)
                || service.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase)
                || service.ServiceName.Contains(search, StringComparison.OrdinalIgnoreCase)
                || service.Status.Contains(search, StringComparison.OrdinalIgnoreCase)
                || service.StartMode.Contains(search, StringComparison.OrdinalIgnoreCase))
            .Select(service => new ServiceRow(
                service.DisplayName,
                service.ServiceName,
                service.Status,
                service.StartMode,
                service.CanStop ? "Controllable" : "Protected"))
            .ToArray();

        ServicesList.ItemsSource = rows;
        ServiceCountText.Text = rows.Length == _services.Count
            ? $"{_services.Count} services"
            : $"Showing {rows.Length} of {_services.Count}";
        ServiceSummaryText.Text = $"Services: {_services.Count}";
    }

    private async void UpdateAgent_Click(object sender, RoutedEventArgs e)
    {
        if (_agentUpdateRunning) return;

        if (MessageBox.Show(this,
                $"Update Grev Agent on {_machine.Name} from the latest GitHub release?\n\nThe Agent service will restart and the existing pairing key will be preserved.",
                "Update Grev Agent",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        _agentUpdateRunning = true;
        UpdateAgentButton.IsEnabled = false;
        SessionActionsPanel.IsEnabled = false;

        try
        {
            var progress = new Progress<string>(message => StatusText.Text = message);
            var result = await _agentUpdater.UpdateFromGitHubAsync(_machine, progress);
            _machine.AgentState = result.State;
            _machine.AgentStatus = result.Status;
            _machine.AgentMessage = result.Message;

            _agentUpdateRunning = false;
            await RefreshVisibleDataAsync();
            StatusText.Text = "Grev Agent updated successfully and is responding again.";
            await LogActivityAsync("Agent", "Update Agent", "Updated from agent-latest", true);

            MessageBox.Show(this,
                $"Grev Agent on {_machine.Name} updated successfully.",
                "Grev Agent updated",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
            await LogActivityAsync("Agent", "Update Agent", ex.Message, false);
            MessageBox.Show(this, ex.Message, "Grev Agent update", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _agentUpdateRunning = false;
            if (_machine.AgentState == GrevAgentState.Connected)
                UpdateAgentButton.IsEnabled = true;
        }
    }

    /// <summary>Themed pressure colour shared by every meter in this window.</summary>
    private static Brush UsageBrush(double percent) => ThemeService.ThemeBrush(percent switch
    {
        >= 90 => "DangerBrush",
        >= 75 => "WarnBrush",
        _ => "AccentBrush"
    });

    private sealed record DiskRow(string Name, string Label, string Space, double Percent, string PercentText);
    private sealed record ProcessRow(int ProcessId, string Name, string Pid, string Memory, string CpuTime, string Session, string Started);
    private sealed record ServiceRow(string DisplayName, string ServiceName, string Status, string StartMode, string Control);
    private sealed record ActivityRow(string Time, string Category, string Action, string Detail, string Result);
}
