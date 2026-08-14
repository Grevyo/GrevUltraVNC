using System.Windows;
using System.Windows.Controls;
using GrevUltraVNC.Models;

namespace GrevUltraVNC;

public partial class MachineOverviewWindow
{
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

    private void ProcessSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_processesLoaded)
            RenderProcesses();
    }

    private void ServiceSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_servicesLoaded)
            RenderServices();
    }

    private void OverviewTab_Click(object sender, RoutedEventArgs e) => ShowSection(OverviewPanel, OverviewButton);

    private async void ProcessesTab_Click(object sender, RoutedEventArgs e)
    {
        ShowSection(ProcessesPanel, ProcessesButton);
        if (!_processesLoaded)
            await RefreshAllAsync();
    }

    private async void ServicesTab_Click(object sender, RoutedEventArgs e)
    {
        ShowSection(ServicesPanel, ServicesButton);
        if (!_servicesLoaded)
            await RefreshAllAsync();
    }

    private void SessionTab_Click(object sender, RoutedEventArgs e) => ShowSection(SessionPanel, SessionButton);
    private void ToolsTab_Click(object sender, RoutedEventArgs e) => ShowSection(ToolsPanel, ToolsButton);

    private void TerminalTab_Click(object sender, RoutedEventArgs e) => ShowTerminal();

    public void ShowTerminal()
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

        var normalStyle = (Style)FindResource("ManageNavButton");
        OverviewButton.Style = normalStyle;
        ProcessesButton.Style = normalStyle;
        ServicesButton.Style = normalStyle;
        SessionButton.Style = normalStyle;
        ToolsButton.Style = normalStyle;
        TerminalButton.Style = normalStyle;
        ActivityButton.Style = normalStyle;
        activeButton.Style = (Style)FindResource("ManageNavActiveButton");
    }
}
