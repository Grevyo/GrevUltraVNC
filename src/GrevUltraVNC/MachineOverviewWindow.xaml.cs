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
    private IReadOnlyList<AgentProcessInfo> _processes = [];
    private IReadOnlyList<AgentServiceInfo> _services = [];
    private List<SavedCommand> _savedCommands = [];
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
        MachineAddressText.Text = $"{machine.IpAddress}  ·  Agent {machine.AgentPort}  ·  VNC {machine.VncPort}";
        RefreshScreenButton.IsEnabled = _vnc?.HasActiveSession(machine.Id) == true;

        Loaded += MachineOverviewWindow_Loaded;
        Closed += (_, _) => _agent.Dispose();
    }

    private async void MachineOverviewWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadSavedCommandsAsync();
        await RefreshAllAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAllAsync();

    private async Task RefreshAllAsync()
    {
        if (_refreshing) return;
        _refreshing = true;
        StatusText.Text = "Refreshing live Agent data…";
        AgentStateText.Text = "● CHECKING AGENT";
        AgentStateText.Foreground = (Brush)FindResource("MutedTextBrush");
        UpdateAgentButton.IsEnabled = false;
        SessionActionsPanel.IsEnabled = false;
        SessionFeatureStatusText.Text = "Checking Agent capability…";
        RefreshScreenButton.IsEnabled = _vnc?.HasActiveSession(_machine.Id) == true;

        try
        {
            var probeTask = _agent.ProbeAsync(_machine);
            var processesTask = _agent.GetProcessesAsync(_machine);
            var servicesTask = _agent.GetServicesAsync(_machine);

            await Task.WhenAll(probeTask, processesTask, servicesTask);

            var probe = await probeTask;
            if (probe.State != GrevAgentState.Connected || probe.Status is null)
                throw new InvalidOperationException(probe.Message ?? "Grev Agent is not connected.");

            var status = probe.Status;
            _processes = await processesTask;
            _services = await servicesTask;

            _machine.AgentState = probe.State;
            _machine.AgentStatus = probe.Status;
            _machine.AgentMessage = probe.Message;

            RenderOverview(status);
            RenderProcesses();
            RenderServices();

            var needsUpdate = !string.IsNullOrWhiteSpace(probe.Message);
            AgentStateText.Text = needsUpdate
                ? "● AGENT UPDATE RECOMMENDED"
                : "● AGENT CONNECTED";
            AgentStateText.Foreground = needsUpdate
                ? (Brush)FindResource("Accent2Brush")
                : new SolidColorBrush(Color.FromRgb(80, 220, 145));
            UpdateAgentButton.Content = needsUpdate
                ? "⇩ Update Agent · recommended"
                : "⇩ Update Agent";
            UpdateAgentButton.IsEnabled = !_agentUpdateRunning;

            SessionActionsPanel.IsEnabled = !needsUpdate;
            SessionFeatureStatusText.Text = needsUpdate
                ? "Update Grev Agent to the current protocol before using the new Windows session and power controls."
                : "Agent session controls ready · actions are authenticated with this machine's pairing key.";

            StatusText.Text = needsUpdate
                ? probe.Message!
                : $"Live data refreshed {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            AgentStateText.Text = "● AGENT UNAVAILABLE";
            AgentStateText.Foreground = (Brush)FindResource("DangerBrush");
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
        CpuUsageText.Text = $"{status.CpuUsagePercent:0.#}%";
        CpuNameText.Text = status.CpuName;

        var usedMemory = Math.Max(0, status.TotalMemoryBytes - status.AvailableMemoryBytes);
        var memoryPercent = status.TotalMemoryBytes > 0
            ? usedMemory * 100.0 / status.TotalMemoryBytes
            : 0;
        MemoryUsageText.Text = $"{memoryPercent:0.#}%";
        MemoryDetailText.Text = $"{FormatBytes(usedMemory)} / {FormatBytes(status.TotalMemoryBytes)}";

        UptimeText.Text = FormatUptime(status.UptimeSeconds);
        UserText.Text = string.IsNullOrWhiteSpace(status.InteractiveUser)
            ? "No interactive user"
            : status.InteractiveUser;

        VncServiceText.Text = status.UltraVncServiceStatus;
        VncPortText.Text = status.UltraVncPortListening
            ? $"TCP {status.UltraVncPort} listening"
            : $"TCP {status.UltraVncPort} not listening";

        OsText.Text = status.OsDescription;
        HostText.Text = $"Host: {status.MachineName}";
        AgentVersionText.Text = $"Grev Agent {status.AgentVersion}";
        ProcessSummaryText.Text = $"Processes: {_processes.Count}";
        ServiceSummaryText.Text = $"Services: {_services.Count}";

        DiskItems.ItemsSource = status.Disks.Select(disk => new DiskRow(
            disk.Name,
            string.IsNullOrWhiteSpace(disk.Label) ? "Local disk" : disk.Label,
            $"{FormatBytes(disk.FreeBytes)} free / {FormatBytes(disk.TotalBytes)}")).ToArray();
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
                FormatBytes(process.WorkingSetBytes),
                FormatCpuTime(process.CpuTimeMilliseconds),
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
            await RefreshAllAsync();
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

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "0 B";
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.#} {units[unit]}";
    }

    private static string FormatCpuTime(long milliseconds)
    {
        var time = TimeSpan.FromMilliseconds(Math.Max(0, milliseconds));
        return time.TotalHours >= 1
            ? $"{(int)time.TotalHours}h {time.Minutes}m"
            : time.TotalMinutes >= 1
                ? $"{time.Minutes}m {time.Seconds}s"
                : $"{time.Seconds}s";
    }

    private static string FormatUptime(long seconds)
    {
        var time = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return time.TotalDays >= 1
            ? $"{(int)time.TotalDays}d {time.Hours}h"
            : time.TotalHours >= 1
                ? $"{(int)time.TotalHours}h {time.Minutes}m"
                : $"{time.Minutes}m";
    }

    private sealed record DiskRow(string Name, string Label, string Space);
    private sealed record ProcessRow(int ProcessId, string Name, string Pid, string Memory, string CpuTime, string Session, string Started);
    private sealed record ServiceRow(string DisplayName, string ServiceName, string Status, string StartMode, string Control);
    private sealed record ActivityRow(string Time, string Category, string Action, string Detail, string Result);
}
