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
        if (_favoritesOnly && !machine.IsFavorite) return false;
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
        if (_uiReady) RefreshMachineView();
    }

    private void FavoritesOnlyCheck_Changed(object sender, RoutedEventArgs e)
    {
        _favoritesOnly = (sender as CheckBox)?.IsChecked == true;
        if (_uiReady) RefreshMachineView();
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

    private async void EditMachine_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if ((sender as FrameworkElement)?.DataContext is not Machine machine) return;

        var dialog = new MachineDialog(machine) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        await _storage.SaveMachinesAsync(Machines);
        RefreshMachineView();
        await RefreshStatusesAsync();
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
