using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using GrevUltraVNC.Models;
using GrevUltraVNC.Services;

namespace GrevUltraVNC;

public partial class MainWindow : Window
{
    private readonly JsonStorage _storage = new();
    private readonly NetworkStatusService _network = new();
    private readonly GrevAgentClient _agent = new();
    private readonly UltraVncSessionService _vnc = new();
    private readonly VncCredentialService _credentials = new();
    private readonly AgentCredentialService _agentCredentials = new();
    private readonly DispatcherTimer _statusTimer = new();
    private readonly Dictionary<Guid, GrevControlPanelWindow> _controlPanels = [];
    private AppSettings _settings = new();
    private bool _statusRefreshRunning;
    private bool _uiReady;
    private string _searchText = string.Empty;
    private bool _favoritesOnly;
    private TrayIconService? _tray;

    public ObservableCollection<Machine> Machines { get; } = [];
    public ICollectionView MachinesView { get; }

    public MainWindow()
    {
        MachinesView = CollectionViewSource.GetDefaultView(Machines);
        MachinesView.Filter = FilterMachine;
        MachinesView.SortDescriptions.Add(new SortDescription(nameof(Machine.IsFavorite), ListSortDirection.Descending));
        MachinesView.SortDescriptions.Add(new SortDescription(nameof(Machine.Name), ListSortDirection.Ascending));

        InitializeComponent();
        DataContext = this;
        _uiReady = true;

        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
        StateChanged += MainWindow_StateChanged;
        _statusTimer.Tick += StatusTimer_Tick;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _settings = await _storage.LoadSettingsAsync();
        _settings.Theme = ThemeService.Normalize(_settings.Theme);
        ThemeService.Apply(_settings.Theme);

        foreach (var machine in await _storage.LoadMachinesAsync())
            Machines.Add(machine);

        try
        {
            StartupService.SetEnabled(_settings.StartWithWindows);
        }
        catch
        {
            // Startup registration can still be changed from Settings if Windows blocks it here.
        }

        _tray = new TrayIconService(this, () => Machines.Where(x => x.IsFavorite), ConnectMachine);
        ConfigureStatusTimer();
        RefreshMachineView();
        await RefreshStatusesAsync();
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _statusTimer.Stop();
        _tray?.Dispose();
        _agent.Dispose();
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized && _settings.MinimizeToTray && _tray is not null)
            Hide();
    }

    private void ConfigureStatusTimer()
    {
        _statusTimer.Stop();
        _statusTimer.Interval = TimeSpan.FromSeconds(Math.Clamp(_settings.StatusCheckSeconds, 3, 300));
        _statusTimer.Start();
    }

    private async void StatusTimer_Tick(object? sender, EventArgs e) => await RefreshStatusesAsync();

    private async Task RefreshStatusesAsync()
    {
        if (_statusRefreshRunning) return;
        _statusRefreshRunning = true;
        try
        {
            FooterStatus.Text = $"Checking {Machines.Count} machine{(Machines.Count == 1 ? string.Empty : "s")}…";
            var checkedAt = DateTime.Now;

            var probes = Machines.Select(async machine =>
            {
                machine.Status = MachineStatus.Checking;
                machine.AgentState = GrevAgentState.Unknown;
                machine.AgentStatus = null;
                machine.AgentMessage = null;

                var networkTask = _network.ProbeAsync(machine);
                var agentTask = _agent.ProbeAsync(machine);
                await Task.WhenAll(networkTask, agentTask);

                var networkResult = await networkTask;
                var agentResult = await agentTask;

                machine.LatencyMs = networkResult.LatencyMs;
                machine.VncAvailable = networkResult.VncAvailable;
                machine.LastCheckedAt = checkedAt;
                machine.Status = networkResult.Status;

                machine.AgentStatus = agentResult.Status;
                machine.AgentMessage = agentResult.Message;
                machine.AgentState = agentResult.State;
            });

            await Task.WhenAll(probes);
            FooterStatus.Text = $"Last checked {DateTime.Now:HH:mm:ss}";
            RefreshMachineView();
        }
        finally
        {
            _statusRefreshRunning = false;
        }
    }

    private bool FilterMachine(object item)
    {
        if (item is not Machine machine) return false;
        if (_favoritesOnly && !machine.IsFavorite) return false;
        if (string.IsNullOrWhiteSpace(_searchText)) return true;

        return machine.Name.Contains(_searchText, StringComparison.OrdinalIgnoreCase)
               || machine.IpAddress.Contains(_searchText, StringComparison.OrdinalIgnoreCase)
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

    private async void Settings_Click(object sender, RoutedEventArgs e)
    {
        var working = new AppSettings
        {
            UltraVncViewerPath = _settings.UltraVncViewerPath,
            AutoScaling = _settings.AutoScaling,
            FullScreenByDefault = _settings.FullScreenByDefault,
            StatusCheckSeconds = _settings.StatusCheckSeconds,
            Theme = _settings.Theme,
            StartWithWindows = _settings.StartWithWindows,
            MinimizeToTray = _settings.MinimizeToTray
        };

        var dialog = new SettingsWindow(working, _vnc) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        _settings = working;
        _settings.Theme = ThemeService.Normalize(_settings.Theme);
        ThemeService.Apply(_settings.Theme);
        await _storage.SaveSettingsAsync(_settings);
        ConfigureStatusTimer();

        try
        {
            StartupService.SetEnabled(_settings.StartWithWindows);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Start with Windows", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshStatusesAsync();

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

        var overview = new MachineOverviewWindow(machine) { Owner = this };
        overview.ShowDialog();
    }

    private void ConnectMachine(Machine machine)
    {
        try
        {
            _vnc.Launch(machine, _settings);

            if (_controlPanels.TryGetValue(machine.Id, out var existing))
            {
                if (!existing.IsVisible)
                    existing.Show();

                existing.Activate();
                return;
            }

            var controlPanel = new GrevControlPanelWindow(machine, _vnc);
            _controlPanels[machine.Id] = controlPanel;
            controlPanel.Closed += (_, _) => _controlPanels.Remove(machine.Id);
            controlPanel.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not open VNC", MessageBoxButton.OK, MessageBoxImage.Error);
        }
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

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match) return match;

            try
            {
                current = VisualTreeHelper.GetParent(current);
            }
            catch (InvalidOperationException)
            {
                current = LogicalTreeHelper.GetParent(current);
            }
        }
        return null;
    }
}
