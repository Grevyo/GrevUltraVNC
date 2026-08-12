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

    private async void RefreshScreen_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_vnc is null || !_vnc.HasActiveSession(_machine.Id))
                throw new InvalidOperationException("Open a VNC session to this machine first.");

            _vnc.RequestScreenRefresh(_machine.Id);
            StatusText.Text = "UltraVNC screen refresh requested.";
            await LogActivityAsync("VNC", "Refresh screen", "Requested a full remote screen refresh", true);
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
            await LogActivityAsync("VNC", "Refresh screen", ex.Message, false);
            MessageBox.Show(this, ex.Message, "Refresh screen", MessageBoxButton.OK, MessageBoxImage.Information);
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

        await RunAgentActionAsync(
            () => _agent.EndProcessAsync(_machine, selected.ProcessId),
            "Process",
            "End process",
            $"{selected.Name} · PID {selected.ProcessId}");
    }

    private async void RestartExplorer_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this,
                $"Restart Windows Explorer on {_machine.Name}?\n\nThe taskbar and desktop may disappear briefly while the shell restarts.",
                "Restart Windows Explorer",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        await RunAgentActionAsync(
            () => _agent.RunQuickActionAsync(_machine, "restart-explorer"),
            "Session",
            "Restart Explorer",
            "Active interactive Windows session");
    }

    private async void LockSession_Click(object sender, RoutedEventArgs e) =>
        await RunAgentActionAsync(
            () => _agent.RunQuickActionAsync(_machine, "lock"),
            "Session",
            "Lock workstation",
            "Active interactive Windows session",
            refreshAfterSuccess: false);

    private async void SignOutSession_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this,
                $"Sign out the active user on {_machine.Name}?\n\nAny unsaved work in that Windows session can be lost.",
                "Sign out active user",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        await RunAgentActionAsync(
            () => _agent.RunQuickActionAsync(_machine, "sign-out"),
            "Session",
            "Sign out",
            "Active interactive Windows session",
            refreshAfterSuccess: false);
    }

    private async void SleepSession_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this,
                $"Put {_machine.Name} to sleep?\n\nRemote connections will drop until the machine wakes again.",
                "Sleep machine",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        await RunAgentActionAsync(
            () => _agent.RunQuickActionAsync(_machine, "sleep"),
            "Power",
            "Sleep machine",
            "Suspend requested",
            refreshAfterSuccess: false);
    }

    private async void HibernateSession_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this,
                $"Hibernate {_machine.Name}?\n\nRemote connections will drop until the machine is powered or woken again.",
                "Hibernate machine",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        await RunAgentActionAsync(
            () => _agent.RunQuickActionAsync(_machine, "hibernate"),
            "Power",
            "Hibernate machine",
            "Hibernate requested",
            refreshAfterSuccess: false);
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

        await RunAgentActionAsync(
            () => _agent.ControlServiceAsync(_machine, selected.ServiceName, action),
            "Service",
            $"{actionTitle} service",
            $"{selected.DisplayName} · {selected.ServiceName}");
    }

    private async Task RunAgentActionAsync(
        Func<Task<AgentActionResponse>> action,
        string category,
        string activityAction,
        string detail,
        bool refreshAfterSuccess = true)
    {
        if (_actionRunning) return;
        _actionRunning = true;
        StatusText.Text = "Sending authenticated Agent action…";

        try
        {
            var result = await action();
            if (result.Success)
            {
                if (refreshAfterSuccess)
                    await RefreshAllAsync();

                StatusText.Text = result.Message;
                await LogActivityAsync(category, activityAction, detail, true);
            }
            else
            {
                StatusText.Text = result.Message;
                await LogActivityAsync(category, activityAction, result.Message, false);
                MessageBox.Show(this, result.Message, "Grev Agent action", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
            await LogActivityAsync(category, activityAction, ex.Message, false);
            MessageBox.Show(this, ex.Message, "Grev Agent action", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _actionRunning = false;
        }
    }

    private async void FlushDns_Click(object sender, RoutedEventArgs e) =>
        await RunToolCommandAsync("cmd", "ipconfig /flushdns", "Flush DNS", "Network");

    private async void RestartSpooler_Click(object sender, RoutedEventArgs e) =>
        await RunToolCommandAsync(
            "powershell",
            "Restart-Service -Name Spooler -Force; Get-Service -Name Spooler | Select-Object Name, Status | Format-Table -AutoSize",
            "Restart Print Spooler",
            "Service");

    private async void NetworkConfig_Click(object sender, RoutedEventArgs e) =>
        await RunToolCommandAsync("cmd", "ipconfig /all", "Network configuration", "Network");

    private async void DiskSpace_Click(object sender, RoutedEventArgs e) =>
        await RunToolCommandAsync(
            "powershell",
            "Get-CimInstance Win32_LogicalDisk -Filter \"DriveType=3\" | Select-Object DeviceID,@{N='FreeGB';E={[math]::Round($_.FreeSpace/1GB,1)}},@{N='SizeGB';E={[math]::Round($_.Size/1GB,1)}} | Format-Table -AutoSize",
            "Disk space",
            "System");

    private async void WindowsUpdateScan_Click(object sender, RoutedEventArgs e) =>
        await RunToolCommandAsync("cmd", "UsoClient.exe StartScan", "Windows Update scan", "Windows Update");

    private async void RestartVncTool_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this,
                "Restart UltraVNC on the remote machine? An active VNC session may disconnect.",
                "Restart UltraVNC",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        await RunAgentActionAsync(
            () => _agent.ControlServiceAsync(_machine, "uvnc_service", "restart"),
            "VNC",
            "Restart UltraVNC",
            "uvnc_service");
    }

    private async void TaskManagerTool_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_vnc is null || !_vnc.HasActiveSession(_machine.Id))
                throw new InvalidOperationException("Open a VNC session to this machine first.");

            _vnc.SendCtrlShiftEscape(_machine.Id);
            StatusText.Text = "Task Manager shortcut sent to the VNC session.";
            await LogActivityAsync("VNC", "Open Task Manager", "Ctrl+Shift+Esc sent through UltraVNC", true);
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
            await LogActivityAsync("VNC", "Open Task Manager", ex.Message, false);
            MessageBox.Show(this, ex.Message, "Task Manager", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private async Task RunToolCommandAsync(string shell, string command, string title, string category)
    {
        if (_commandRunning) return;
        _commandRunning = true;
        StatusText.Text = $"{title} on {_machine.Name}…";
        AppendToolOutput($"[{DateTime.Now:HH:mm:ss}] {title}");

        try
        {
            var result = await _agent.RunCommandAsync(_machine, shell, command, 30);
            AppendCommandResult(ToolOutputBox, result);
            StatusText.Text = result.Success
                ? $"{title} completed."
                : result.TimedOut
                    ? $"{title} timed out."
                    : $"{title} finished with exit code {result.ExitCode}.";

            await LogActivityAsync(category, title, $"{ShellLabel(shell)} · exit {result.ExitCode}", result.Success);
        }
        catch (Exception ex)
        {
            AppendToolOutput($"[Grev Agent error] {ex.Message}");
            StatusText.Text = ex.Message;
            await LogActivityAsync(category, title, ex.Message, false);
        }
        finally
        {
            _commandRunning = false;
        }
    }

    private async Task LoadSavedCommandsAsync()
    {
        _savedCommands = await _workspace.LoadSavedCommandsAsync();
        RenderSavedCommands();
    }

    private void RenderSavedCommands()
    {
        SavedCommandsList.ItemsSource = _savedCommands
            .OrderBy(command => command.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private void SavedCommandsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SavedCommandsList.SelectedItem is not SavedCommand selected) return;

        SavedCommandNameBox.Text = selected.Name;
        SavedCommandTextBox.Text = selected.Command;
        SelectShell(SavedCommandShellBox, selected.Shell);
    }

    private void NewSavedCommand_Click(object sender, RoutedEventArgs e)
    {
        SavedCommandsList.SelectedItem = null;
        SavedCommandNameBox.Clear();
        SavedCommandTextBox.Clear();
        SavedCommandShellBox.SelectedIndex = 0;
        SavedCommandNameBox.Focus();
        StatusText.Text = "New saved command.";
    }

    private async void SaveSavedCommand_Click(object sender, RoutedEventArgs e)
    {
        var name = SavedCommandNameBox.Text.Trim();
        var commandText = SavedCommandTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(commandText))
        {
            StatusText.Text = "Give the saved command a name and command text first.";
            return;
        }

        var shell = GetSelectedShell(SavedCommandShellBox);
        if (SavedCommandsList.SelectedItem is SavedCommand existing)
        {
            existing.Name = name;
            existing.Shell = shell;
            existing.Command = commandText;
            existing.UpdatedAtUtc = DateTime.UtcNow;
        }
        else
        {
            var saved = new SavedCommand
            {
                Name = name,
                Shell = shell,
                Command = commandText
            };
            _savedCommands.Add(saved);
            SavedCommandsList.SelectedItem = saved;
        }

        await _workspace.SaveSavedCommandsAsync(_savedCommands);
        RenderSavedCommands();
        StatusText.Text = $"Saved command '{name}'.";
    }

    private async void DeleteSavedCommand_Click(object sender, RoutedEventArgs e)
    {
        if (SavedCommandsList.SelectedItem is not SavedCommand selected)
        {
            StatusText.Text = "Select a saved command first.";
            return;
        }

        if (MessageBox.Show(this,
                $"Delete the saved command '{selected.Name}'?",
                "Delete saved command",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        _savedCommands.RemoveAll(command => command.Id == selected.Id);
        await _workspace.SaveSavedCommandsAsync(_savedCommands);
        RenderSavedCommands();
        NewSavedCommand_Click(sender, e);
        StatusText.Text = $"Deleted saved command '{selected.Name}'.";
    }

    private async void RunSavedCommand_Click(object sender, RoutedEventArgs e)
    {
        var name = SavedCommandNameBox.Text.Trim();
        var commandText = SavedCommandTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(commandText))
        {
            StatusText.Text = "Select or enter a command first.";
            return;
        }

        var shell = GetSelectedShell(SavedCommandShellBox);
        await RunSavedCommandAsync(shell, commandText, string.IsNullOrWhiteSpace(name) ? "Unsaved command" : name);
    }

    private async Task RunSavedCommandAsync(string shell, string command, string name)
    {
        if (_commandRunning) return;
        _commandRunning = true;
        StatusText.Text = $"Running '{name}' on {_machine.Name}…";
        AppendToolOutput($"[{DateTime.Now:HH:mm:ss}] Saved command: {name} ({ShellLabel(shell)})");

        try
        {
            var result = await _agent.RunCommandAsync(_machine, shell, command, 30);
            AppendCommandResult(ToolOutputBox, result);
            StatusText.Text = result.Success
                ? $"'{name}' completed."
                : $"'{name}' finished with exit code {result.ExitCode}.";
            await LogActivityAsync("Saved command", name, $"{ShellLabel(shell)} · exit {result.ExitCode}", result.Success);
        }
        catch (Exception ex)
        {
            AppendToolOutput($"[Grev Agent error] {ex.Message}");
            StatusText.Text = ex.Message;
            await LogActivityAsync("Saved command", name, ex.Message, false);
        }
        finally
        {
            _commandRunning = false;
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

        var shell = GetSelectedShell(TerminalShellBox);
        var shellLabel = ShellLabel(shell);

        _commandRunning = true;
        RunCommandButton.IsEnabled = false;
        TerminalCommandBox.IsEnabled = false;
        StatusText.Text = $"Running {shellLabel} command on {_machine.Name}…";
        TerminalOutputBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {shellLabel}> {command}{Environment.NewLine}");
        TerminalOutputBox.ScrollToEnd();

        try
        {
            var result = await _agent.RunCommandAsync(_machine, shell, command, 30);
            AppendCommandResult(TerminalOutputBox, result);

            StatusText.Text = result.Success
                ? $"Command completed with exit code {result.ExitCode}."
                : result.TimedOut
                    ? "Command timed out and was terminated by Grev Agent."
                    : $"Command finished with exit code {result.ExitCode}.";

            await LogActivityAsync("Terminal", $"Run {shellLabel} command", $"Exit {result.ExitCode}", result.Success);
            TerminalCommandBox.Clear();
        }
        catch (Exception ex)
        {
            TerminalOutputBox.AppendText($"[Grev Agent error] {ex.Message}{Environment.NewLine}{Environment.NewLine}");
            TerminalOutputBox.ScrollToEnd();
            StatusText.Text = ex.Message;
            await LogActivityAsync("Terminal", $"Run {shellLabel} command", ex.Message, false);
        }
        finally
        {
            _commandRunning = false;
            RunCommandButton.IsEnabled = true;
            TerminalCommandBox.IsEnabled = true;
            TerminalCommandBox.Focus();
        }
    }

    private static void AppendCommandResult(TextBox output, AgentCommandResponse result)
    {
        if (!string.IsNullOrEmpty(result.StandardOutput))
        {
            output.AppendText(result.StandardOutput);
            if (!result.StandardOutput.EndsWith(Environment.NewLine, StringComparison.Ordinal))
                output.AppendText(Environment.NewLine);
        }

        if (!string.IsNullOrEmpty(result.StandardError))
        {
            output.AppendText("[stderr]" + Environment.NewLine);
            output.AppendText(result.StandardError);
            if (!result.StandardError.EndsWith(Environment.NewLine, StringComparison.Ordinal))
                output.AppendText(Environment.NewLine);
        }

        var timeoutText = result.TimedOut ? " · TIMED OUT" : string.Empty;
        output.AppendText($"[exit {result.ExitCode} · {result.DurationMilliseconds} ms{timeoutText}]{Environment.NewLine}{Environment.NewLine}");
        output.ScrollToEnd();
    }

    private void AppendToolOutput(string text)
    {
        ToolOutputBox.AppendText(text + Environment.NewLine);
        ToolOutputBox.ScrollToEnd();
    }

    private async Task LoadActivityAsync()
    {
        var entries = await _workspace.LoadActivityAsync(_machine.Id);
        ActivityList.ItemsSource = entries.Select(entry => new ActivityRow(
            entry.TimestampUtc.ToLocalTime().ToString("dd MMM yyyy HH:mm:ss"),
            entry.Category,
            entry.Action,
            entry.Detail,
            entry.Success ? "Success" : "Failed")).ToArray();
        ActivitySummaryText.Text = entries.Count == 0
            ? "No recorded activity for this machine yet."
            : $"{entries.Count} recorded action{(entries.Count == 1 ? string.Empty : "s")} for {_machine.Name}";
    }

    private async Task LogActivityAsync(string category, string action, string detail, bool success)
    {
        try
        {
            await _workspace.AppendActivityAsync(new ActivityEntry
            {
                MachineId = _machine.Id,
                MachineName = _machine.Name,
                TimestampUtc = DateTime.UtcNow,
                Category = category,
                Action = action,
                Detail = detail,
                Success = success
            });

            if (ActivityPanel.Visibility == Visibility.Visible)
                await LoadActivityAsync();
        }
        catch
        {
            // Activity logging must never block a management action.
        }
    }

    private async void RefreshActivity_Click(object sender, RoutedEventArgs e) => await LoadActivityAsync();

    private async void ClearActivity_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this,
                $"Clear all recorded GrevUltraVNC activity for {_machine.Name}?",
                "Clear activity history",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        await _workspace.ClearActivityAsync(_machine.Id);
        await LoadActivityAsync();
        StatusText.Text = "Machine activity history cleared.";
    }

    private void ProcessSearchBox_TextChanged(object sender, TextChangedEventArgs e) => RenderProcesses();
    private void ServiceSearchBox_TextChanged(object sender, TextChangedEventArgs e) => RenderServices();

    private void OverviewTab_Click(object sender, RoutedEventArgs e) => ShowSection(OverviewPanel, OverviewButton);
    private void ProcessesTab_Click(object sender, RoutedEventArgs e) => ShowSection(ProcessesPanel, ProcessesButton);
    private void ServicesTab_Click(object sender, RoutedEventArgs e) => ShowSection(ServicesPanel, ServicesButton);
    private void SessionTab_Click(object sender, RoutedEventArgs e) => ShowSection(SessionPanel, SessionButton);
    private void ToolsTab_Click(object sender, RoutedEventArgs e) => ShowSection(ToolsPanel, ToolsButton);
    private void TerminalTab_Click(object sender, RoutedEventArgs e)
    {
        ShowSection(TerminalPanel, TerminalButton);
        TerminalCommandBox.Focus();
    }

    private async void ActivityTab_Click(object sender, RoutedEventArgs e)
    {
        ShowSection(ActivityPanel, ActivityButton);
        await LoadActivityAsync();
    }

    private void ShowSection(FrameworkElement section, Button activeButton)
    {
        OverviewPanel.Visibility = Visibility.Collapsed;
        ProcessesPanel.Visibility = Visibility.Collapsed;
        ServicesPanel.Visibility = Visibility.Collapsed;
        SessionPanel.Visibility = Visibility.Collapsed;
        ToolsPanel.Visibility = Visibility.Collapsed;
        TerminalPanel.Visibility = Visibility.Collapsed;
        ActivityPanel.Visibility = Visibility.Collapsed;
        section.Visibility = Visibility.Visible;

        OverviewButton.Style = (Style)FindResource("SecondaryButton");
        ProcessesButton.Style = (Style)FindResource("SecondaryButton");
        ServicesButton.Style = (Style)FindResource("SecondaryButton");
        SessionButton.Style = (Style)FindResource("SecondaryButton");
        ToolsButton.Style = (Style)FindResource("SecondaryButton");
        TerminalButton.Style = (Style)FindResource("SecondaryButton");
        ActivityButton.Style = (Style)FindResource("SecondaryButton");
        activeButton.Style = (Style)FindResource("PrimaryButton");
    }

    private static string GetSelectedShell(ComboBox comboBox)
    {
        var item = comboBox.SelectedItem as ComboBoxItem;
        return item?.Tag?.ToString() ?? "powershell";
    }

    private static void SelectShell(ComboBox comboBox, string shell)
    {
        foreach (var candidate in comboBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(candidate.Tag?.ToString(), shell, StringComparison.OrdinalIgnoreCase))
            {
                comboBox.SelectedItem = candidate;
                return;
            }
        }
        comboBox.SelectedIndex = 0;
    }

    private static string ShellLabel(string shell) =>
        string.Equals(shell, "cmd", StringComparison.OrdinalIgnoreCase) ? "CMD" : "PowerShell";

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
