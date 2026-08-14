using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GrevUltraVNC.Contracts;
using GrevUltraVNC.Models;

namespace GrevUltraVNC;

public partial class MachineOverviewWindow
{
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
}
