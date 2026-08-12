using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GrevUltraVNC.Contracts;
using GrevUltraVNC.Models;
using GrevUltraVNC.Services;

namespace GrevUltraVNC;

public partial class MachineOverviewWindow : Window
{
    private readonly Machine _machine;
    private readonly GrevAgentClient _agent = new();
    private readonly AgentUpdateService _agentUpdater;
    private IReadOnlyList<AgentProcessInfo> _processes = [];
    private IReadOnlyList<AgentServiceInfo> _services = [];
    private bool _refreshing;
    private bool _actionRunning;
    private bool _commandRunning;
    private bool _agentUpdateRunning;

    public MachineOverviewWindow(Machine machine)
    {
        InitializeComponent();
        _machine = machine;
        _agentUpdater = new AgentUpdateService(_agent);

        MachineNameText.Text = machine.Name;
        MachineAddressText.Text = $"{machine.IpAddress}  ·  Agent {machine.AgentPort}  ·  VNC {machine.VncPort}";

        Loaded += MachineOverviewWindow_Loaded;
        Closed += (_, _) => _agent.Dispose();
    }

    private async void MachineOverviewWindow_Loaded(object sender, RoutedEventArgs e) => await RefreshAllAsync();
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAllAsync();

    private async Task RefreshAllAsync()
    {
        if (_refreshing) return;
        _refreshing = true;
        StatusText.Text = "Refreshing live Agent data…";
        AgentStateText.Text = "● CHECKING AGENT";
        AgentStateText.Foreground = (Brush)FindResource("MutedTextBrush");
        UpdateAgentButton.IsEnabled = false;

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

            AgentStateText.Text = string.IsNullOrWhiteSpace(probe.Message)
                ? "● AGENT CONNECTED"
                : "● AGENT UPDATE RECOMMENDED";
            AgentStateText.Foreground = string.IsNullOrWhiteSpace(probe.Message)
                ? new SolidColorBrush(Color.FromRgb(80, 220, 145))
                : (Brush)FindResource("Accent2Brush");
            UpdateAgentButton.Content = string.IsNullOrWhiteSpace(probe.Message)
                ? "⇩ Update Agent"
                : "⇩ Update Agent · recommended";
            UpdateAgentButton.IsEnabled = !_agentUpdateRunning;
            StatusText.Text = string.IsNullOrWhiteSpace(probe.Message)
                ? $"Live data refreshed {DateTime.Now:HH:mm:ss}"
                : probe.Message;
        }
        catch (Exception ex)
        {
            AgentStateText.Text = "● AGENT UNAVAILABLE";
            AgentStateText.Foreground = (Brush)FindResource("DangerBrush");
            UpdateAgentButton.IsEnabled = false;
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

            MessageBox.Show(this,
                $"Grev Agent on {_machine.Name} updated successfully.",
                "Grev Agent updated",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
            MessageBox.Show(this, ex.Message, "Grev Agent update", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _agentUpdateRunning = false;
            if (_machine.AgentState == GrevAgentState.Connected)
                UpdateAgentButton.IsEnabled = true;
        }
    }

    private async void EndProcess_Click(object sender, RoutedEventArgs e)
    {
        if (ProcessesList.SelectedItem is not ProcessRow selected)
        {
            StatusText.Text = "Select a process first.";
            return;
        }

        if (MessageBox.Show(this,
                $"End {selected.Name} (PID {selected.ProcessId}) on {_machine.Name}?\n\nUnsaved work in that process can be lost.",
                "End remote process",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        await RunAgentActionAsync(() => _agent.EndProcessAsync(_machine, selected.ProcessId));
    }

    private async void RestartExplorer_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this,
                $"Restart Windows Explorer on {_machine.Name}?\n\nThe taskbar and desktop may disappear briefly while the shell restarts.",
                "Restart Windows Explorer",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        await RunAgentActionAsync(() => _agent.RunQuickActionAsync(_machine, "restart-explorer"));
    }

    private async void StartService_Click(object sender, RoutedEventArgs e) =>
        await RunSelectedServiceActionAsync("start", requiresConfirmation: false);

    private async void StopService_Click(object sender, RoutedEventArgs e) =>
        await RunSelectedServiceActionAsync("stop", requiresConfirmation: true);

    private async void RestartService_Click(object sender, RoutedEventArgs e) =>
        await RunSelectedServiceActionAsync("restart", requiresConfirmation: true);

    private async Task RunSelectedServiceActionAsync(string action, bool requiresConfirmation)
    {
        if (ServicesList.SelectedItem is not ServiceRow selected)
        {
            StatusText.Text = "Select a Windows service first.";
            return;
        }

        var actionTitle = char.ToUpperInvariant(action[0]) + action[1..];
        if (requiresConfirmation && MessageBox.Show(this,
                $"{actionTitle} {selected.DisplayName} ({selected.ServiceName}) on {_machine.Name}?",
                $"{actionTitle} remote service",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        await RunAgentActionAsync(() => _agent.ControlServiceAsync(_machine, selected.ServiceName, action));
    }

    private async Task RunAgentActionAsync(Func<Task<AgentActionResponse>> action)
    {
        if (_actionRunning) return;
        _actionRunning = true;
        StatusText.Text = "Sending authenticated Agent action…";

        try
        {
            var result = await action();
            if (result.Success)
            {
                await RefreshAllAsync();
                StatusText.Text = result.Message;
            }
            else
            {
                StatusText.Text = result.Message;
                MessageBox.Show(this, result.Message, "Grev Agent action", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
            MessageBox.Show(this, ex.Message, "Grev Agent action", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _actionRunning = false;
        }
    }

    private async void RunCommand_Click(object sender, RoutedEventArgs e) => await RunTerminalCommandAsync();

    private async void TerminalCommandBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await RunTerminalCommandAsync();
    }

    private async Task RunTerminalCommandAsync()
    {
        if (_commandRunning) return;

        var command = TerminalCommandBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(command))
        {
            StatusText.Text = "Enter a command first.";
            return;
        }

        var shellItem = TerminalShellBox.SelectedItem as ComboBoxItem;
        var shell = shellItem?.Tag?.ToString() ?? "powershell";
        var shellLabel = string.Equals(shell, "cmd", StringComparison.OrdinalIgnoreCase) ? "CMD" : "PowerShell";

        _commandRunning = true;
        RunCommandButton.IsEnabled = false;
        TerminalCommandBox.IsEnabled = false;
        StatusText.Text = $"Running {shellLabel} command on {_machine.Name}…";
        TerminalOutputBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {shellLabel}> {command}{Environment.NewLine}");
        TerminalOutputBox.ScrollToEnd();

        try
        {
            var result = await _agent.RunCommandAsync(_machine, shell, command, 30);

            if (!string.IsNullOrEmpty(result.StandardOutput))
            {
                TerminalOutputBox.AppendText(result.StandardOutput);
                if (!result.StandardOutput.EndsWith(Environment.NewLine, StringComparison.Ordinal))
                    TerminalOutputBox.AppendText(Environment.NewLine);
            }

            if (!string.IsNullOrEmpty(result.StandardError))
            {
                TerminalOutputBox.AppendText("[stderr]" + Environment.NewLine);
                TerminalOutputBox.AppendText(result.StandardError);
                if (!result.StandardError.EndsWith(Environment.NewLine, StringComparison.Ordinal))
                    TerminalOutputBox.AppendText(Environment.NewLine);
            }

            var timeoutText = result.TimedOut ? " · TIMED OUT" : string.Empty;
            TerminalOutputBox.AppendText($"[exit {result.ExitCode} · {result.DurationMilliseconds} ms{timeoutText}]{Environment.NewLine}{Environment.NewLine}");
            TerminalOutputBox.ScrollToEnd();

            StatusText.Text = result.Success
                ? $"Command completed with exit code {result.ExitCode}."
                : result.TimedOut
                    ? "Command timed out and was terminated by Grev Agent."
                    : $"Command finished with exit code {result.ExitCode}.";

            TerminalCommandBox.Clear();
        }
        catch (Exception ex)
        {
            TerminalOutputBox.AppendText($"[Grev Agent error] {ex.Message}{Environment.NewLine}{Environment.NewLine}");
            TerminalOutputBox.ScrollToEnd();
            StatusText.Text = ex.Message;
        }
        finally
        {
            _commandRunning = false;
            RunCommandButton.IsEnabled = true;
            TerminalCommandBox.IsEnabled = true;
            TerminalCommandBox.Focus();
        }
    }

    private void ProcessSearchBox_TextChanged(object sender, TextChangedEventArgs e) => RenderProcesses();
    private void ServiceSearchBox_TextChanged(object sender, TextChangedEventArgs e) => RenderServices();

    private void OverviewTab_Click(object sender, RoutedEventArgs e) => ShowSection(OverviewPanel, OverviewButton);
    private void ProcessesTab_Click(object sender, RoutedEventArgs e) => ShowSection(ProcessesPanel, ProcessesButton);
    private void ServicesTab_Click(object sender, RoutedEventArgs e) => ShowSection(ServicesPanel, ServicesButton);
    private void TerminalTab_Click(object sender, RoutedEventArgs e)
    {
        ShowSection(TerminalPanel, TerminalButton);
        TerminalCommandBox.Focus();
    }

    private void ShowSection(FrameworkElement section, Button activeButton)
    {
        OverviewPanel.Visibility = Visibility.Collapsed;
        ProcessesPanel.Visibility = Visibility.Collapsed;
        ServicesPanel.Visibility = Visibility.Collapsed;
        TerminalPanel.Visibility = Visibility.Collapsed;
        section.Visibility = Visibility.Visible;

        OverviewButton.Style = (Style)FindResource("SecondaryButton");
        ProcessesButton.Style = (Style)FindResource("SecondaryButton");
        ServicesButton.Style = (Style)FindResource("SecondaryButton");
        TerminalButton.Style = (Style)FindResource("SecondaryButton");
        activeButton.Style = (Style)FindResource("PrimaryButton");
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
}
