using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using GrevUltraVNC.Models;
using GrevUltraVNC.Services;

namespace GrevUltraVNC;

public partial class MainWindow : Window
{
    private readonly JsonStorage _storage = new();
    private readonly NetworkStatusService _network = new();
    private readonly UltraVncSessionService _vnc = new();
    private readonly DispatcherTimer _statusTimer = new();
    private AppSettings _settings = new();
    private bool _statusRefreshRunning;

    public ObservableCollection<Machine> Machines { get; } = [];

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        Loaded += MainWindow_Loaded;
        _statusTimer.Tick += StatusTimer_Tick;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _settings = await _storage.LoadSettingsAsync();
        _settings.Theme = ThemeService.Normalize(_settings.Theme);
        ThemeService.Apply(_settings.Theme);

        foreach (var machine in await _storage.LoadMachinesAsync()) Machines.Add(machine);
        ConfigureStatusTimer();
        await RefreshStatusesAsync();
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
            var probes = Machines.Select(async machine =>
            {
                machine.Status = MachineStatus.Checking;
                var result = await _network.ProbeAsync(machine);
                machine.LatencyMs = result.LatencyMs;
                machine.VncAvailable = result.VncAvailable;
                machine.Status = result.Status;
            });
            await Task.WhenAll(probes);
            FooterStatus.Text = $"Last checked {DateTime.Now:HH:mm:ss}";
        }
        finally
        {
            _statusRefreshRunning = false;
        }
    }

    private async void AddMachine_Click(object sender, RoutedEventArgs e)
    {
        var machine = new Machine();
        var dialog = new MachineDialog(machine) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        Machines.Add(machine);
        await _storage.SaveMachinesAsync(Machines);
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
            Theme = _settings.Theme
        };
        var dialog = new SettingsWindow(working, _vnc) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        _settings = working;
        _settings.Theme = ThemeService.Normalize(_settings.Theme);
        ThemeService.Apply(_settings.Theme);
        await _storage.SaveSettingsAsync(_settings);
        ConfigureStatusTimer();
    }

    private void MachineCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2 || (sender as FrameworkElement)?.DataContext is not Machine machine) return;

        try
        {
            _vnc.Launch(machine, _settings);
            var controlPanel = new GrevControlPanelWindow(machine, _vnc);
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
        await RefreshStatusesAsync();
    }
}
