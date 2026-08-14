using System.Windows;
using GrevUltraVNC.Contracts;

namespace GrevUltraVNC;

public partial class MachineOverviewWindow
{
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
}
