using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GrevUltraVNC.Models;

namespace GrevUltraVNC;

public partial class MainWindow
{
    private bool FilterMachine(object item)
    {
        if (item is not Machine machine) return false;

        var matchesFilter = _machineFilter switch
        {
            "online" => machine.Status is MachineStatus.Online or MachineStatus.VncUnavailable,
            "offline" => machine.Status == MachineStatus.Offline,
            "favorites" => machine.IsFavorite,
            _ => true
        };
        if (!matchesFilter) return false;

        if (string.IsNullOrWhiteSpace(_searchText)) return true;

        return machine.Name.Contains(_searchText, StringComparison.OrdinalIgnoreCase)
               || machine.IpAddress.Contains(_searchText, StringComparison.OrdinalIgnoreCase)
               || machine.ConnectId.Contains(_searchText, StringComparison.OrdinalIgnoreCase)
               || machine.Group.Contains(_searchText, StringComparison.OrdinalIgnoreCase)
               || machine.Notes.Contains(_searchText, StringComparison.OrdinalIgnoreCase);
    }

    private void RefreshMachineView()
    {
        if (!_uiReady) return;

        MachinesView.Refresh();
        var shown = MachinesView.Cast<object>().Count();
        FilterSummaryText.Text = shown == Machines.Count
            ? $"{Machines.Count} machine{(Machines.Count == 1 ? string.Empty : "s")}"
            : $"Showing {shown} of {Machines.Count}";

        EmptyStatePanel.Visibility = shown == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (shown == 0)
        {
            EmptyStateTitle.Text = Machines.Count == 0 ? "No machines yet" : "No matching machines";
            EmptyStateDetail.Text = Machines.Count == 0
                ? "Add your first PC to start a LAN or Grev Connect session."
                : "Try another search or switch back to All machines.";
        }
    }

    private async void AddMachine_Click(object sender, RoutedEventArgs e)
    {
        var machine = new Machine();
        var dialog = new MachineDialog(machine) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        Machines.Add(machine);
        await _storage.SaveMachinesAsync(Machines);
        RefreshMachineView();
        await RefreshStatusesAsync();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchText = (sender as TextBox)?.Text.Trim() ?? string.Empty;
        if (!_uiReady) return;

        _searchDebounceTimer.Stop();
        _searchDebounceTimer.Start();
    }

    private void SearchDebounceTimer_Tick(object? sender, EventArgs e)
    {
        _searchDebounceTimer.Stop();
        RefreshMachineView();
    }

    private void MachineFilter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string filter)
            return;

        _machineFilter = filter;
        UpdateMachineFilterStyles();
        RefreshMachineView();
    }

    private void UpdateMachineFilterStyles()
    {
        if (!_uiReady) return;

        var normal = (Style)FindResource("MainFilterButton");
        var active = (Style)FindResource("MainFilterActiveButton");
        AllFilterButton.Style = normal;
        OnlineFilterButton.Style = normal;
        OfflineFilterButton.Style = normal;
        FavoritesFilterButton.Style = normal;

        var selected = _machineFilter switch
        {
            "online" => OnlineFilterButton,
            "offline" => OfflineFilterButton,
            "favorites" => FavoritesFilterButton,
            _ => AllFilterButton
        };
        selected.Style = active;
    }

    private void MachineCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2 || (sender as FrameworkElement)?.DataContext is not Machine machine) return;
        if (FindAncestor<Button>(e.OriginalSource as DependencyObject) is not null) return;
        ConnectMachine(machine);
    }

    private async void MachineCard_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not Machine machine) return;
        e.Handled = true;
        await OpenMachineActionsAsync(machine);
    }

    private void ConnectMachine_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if ((sender as FrameworkElement)?.DataContext is Machine machine)
            ConnectMachine(machine);
    }

    private void ManageMachine_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if ((sender as FrameworkElement)?.DataContext is not Machine machine) return;

        var overview = new MachineOverviewWindow(machine, _vnc) { Owner = this };
        overview.ShowDialog();
    }

    private async void FavoriteMachine_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if ((sender as FrameworkElement)?.DataContext is not Machine machine) return;

        machine.IsFavorite = !machine.IsFavorite;
        await _storage.SaveMachinesAsync(Machines);
        RefreshMachineView();
    }

    private async void MoreMachine_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if ((sender as FrameworkElement)?.DataContext is not Machine machine) return;
        await OpenMachineActionsAsync(machine);
    }

    private async Task OpenMachineActionsAsync(Machine machine)
    {
        var dialog = new MachineActionWindow(machine, _settings, _vnc) { Owner = this };
        dialog.ShowDialog();

        if (dialog.MachineDeleted)
        {
            Machines.Remove(machine);
            try { _credentials.Delete(machine.Id); } catch { }
            try { _agentCredentials.Delete(machine.Id); } catch { }
            await _storage.SaveMachinesAsync(Machines);
            RefreshMachineView();
            return;
        }

        if (dialog.MachineChanged)
        {
            await _storage.SaveMachinesAsync(Machines);
            RefreshMachineView();
            await RefreshStatusesAsync();
        }
    }
}
