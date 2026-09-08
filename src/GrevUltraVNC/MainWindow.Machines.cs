using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
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
            "agent" => machine.AgentState == GrevAgentState.Connected,
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
            : $"{shown} of {Machines.Count} shown";

        UpdateFleetSummary();

        SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
        ClearSearchButton.Visibility = string.IsNullOrEmpty(SearchBox.Text)
            ? Visibility.Collapsed
            : Visibility.Visible;

        EmptyStatePanel.Visibility = shown == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (shown == 0)
        {
            var noMachinesAtAll = Machines.Count == 0;
            EmptyStateTitle.Text = noMachinesAtAll ? "No machines yet" : "Nothing matches that";
            EmptyStateDetail.Text = noMachinesAtAll
                ? "Got a GC- ID? Use Connect by ID and GrevUltraVNC will find the current route for you, on your LAN or across your Zima server."
                : "Try a different search, or switch the filter back to All.";
            EmptyStateActions.Visibility = noMachinesAtAll ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    /// <summary>Header counters, so the window answers "is the house healthy?" at a glance.</summary>
    private void UpdateFleetSummary()
    {
        var online = 0;
        var offline = 0;
        var agents = 0;
        var attention = 0;

        foreach (var machine in Machines)
        {
            switch (machine.Status)
            {
                case MachineStatus.Online:
                    online++;
                    break;
                case MachineStatus.VncUnavailable:
                    online++;
                    attention++;
                    break;
                case MachineStatus.Offline:
                    offline++;
                    break;
            }

            if (machine.AgentState == GrevAgentState.Connected)
                agents++;
            else if (machine.AgentState is GrevAgentState.AuthenticationFailed or GrevAgentState.Error)
                attention++;

            // A disk that is nearly full is the household problem that creeps up silently.
            if (machine.HasTelemetry && machine.DiskPercent >= 90)
                attention++;
        }

        SummaryOnlineText.Text = online.ToString();
        SummaryOfflineText.Text = offline.ToString();
        SummaryAgentText.Text = agents.ToString();
        SummaryAttentionText.Text = attention.ToString();
    }

    private async void QuickConnect_Click(object sender, RoutedEventArgs e) =>
        await OpenQuickConnectAsync();

    private async Task OpenQuickConnectAsync()
    {
        var dialog = new GrevConnectQuickWindow(Machines, _connectResolver, _credentials, _agentCredentials)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true || dialog.ResultMachine is null)
            return;

        var machine = dialog.ResultMachine;
        if (!Machines.Any(item => item.Id == machine.Id))
        {
            Machines.Add(machine);
            await _storage.SaveMachinesAsync(Machines);
            RefreshMachineView();
        }

        ConnectMachine(machine);
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

        SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
        ClearSearchButton.Visibility = string.IsNullOrEmpty(SearchBox.Text)
            ? Visibility.Collapsed
            : Visibility.Visible;

        _searchDebounceTimer.Stop();
        _searchDebounceTimer.Start();
    }

    private void SearchDebounceTimer_Tick(object? sender, EventArgs e)
    {
        _searchDebounceTimer.Stop();
        RefreshMachineView();
    }

    private void ClearSearch_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Clear();
        SearchBox.Focus();
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.F5:
                e.Handled = true;
                _ = RefreshStatusesAsync();
                break;

            case Key.F when Keyboard.Modifiers == ModifierKeys.Control:
                e.Handled = true;
                SearchBox.Focus();
                SearchBox.SelectAll();
                break;

            case Key.N when Keyboard.Modifiers == ModifierKeys.Control:
                e.Handled = true;
                _ = OpenQuickConnectAsync();
                break;

            case Key.Escape when SearchBox.IsKeyboardFocusWithin && SearchBox.Text.Length > 0:
                e.Handled = true;
                SearchBox.Clear();
                break;
        }
    }

    private void MachineFilter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton button || button.Tag is not string filter)
            return;

        _machineFilter = filter;
        UpdateMachineFilterStyles();
        RefreshMachineView();
    }

    /// <summary>Keeps the segmented filter behaving like a single-choice control.</summary>
    private void UpdateMachineFilterStyles()
    {
        if (!_uiReady) return;

        AllFilterButton.IsChecked = _machineFilter == "all";
        OnlineFilterButton.IsChecked = _machineFilter == "online";
        OfflineFilterButton.IsChecked = _machineFilter == "offline";
        AgentFilterButton.IsChecked = _machineFilter == "agent";
        FavoritesFilterButton.IsChecked = _machineFilter == "favorites";
    }

    private void GroupToggle_Click(object sender, RoutedEventArgs e) => ApplyViewArrangement();

    private void SortToggle_Click(object sender, RoutedEventArgs e) => ApplyViewArrangement();

    /// <summary>
    /// Rebuilds grouping and sorting together. Grouped mode stacks group headers vertically
    /// and wraps the cards inside each group; ungrouped mode wraps every card in one flow.
    /// </summary>
    private void ApplyViewArrangement()
    {
        if (!_uiReady) return;

        var grouped = GroupToggleButton.IsChecked == true;
        var byStatus = SortStatusButton.IsChecked == true;

        using (MachinesView.DeferRefresh())
        {
            MachinesView.GroupDescriptions.Clear();
            if (grouped)
                MachinesView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(Machine.Group)));

            MachinesView.SortDescriptions.Clear();
            if (grouped)
                MachinesView.SortDescriptions.Add(new SortDescription(nameof(Machine.Group), ListSortDirection.Ascending));
            if (byStatus)
                MachinesView.SortDescriptions.Add(new SortDescription(nameof(Machine.Status), ListSortDirection.Ascending));
            MachinesView.SortDescriptions.Add(new SortDescription(nameof(Machine.IsFavorite), ListSortDirection.Descending));
            MachinesView.SortDescriptions.Add(new SortDescription(nameof(Machine.Name), ListSortDirection.Ascending));
        }

        MachineItems.ItemsPanel = (ItemsPanelTemplate)FindResource(grouped ? "GroupedItemsPanel" : "WrapItemsPanel");
        RefreshMachineView();
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
        var dialog = new MachineActionWindow(machine, _vnc) { Owner = this };
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

        if (dialog.ConnectRequested)
            ConnectMachine(machine);
    }
}
