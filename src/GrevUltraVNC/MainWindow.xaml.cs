using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using GrevUltraVNC.Models;
using GrevUltraVNC.Services;

namespace GrevUltraVNC;

public partial class MainWindow : Window
{
    private readonly JsonStorage _storage = new();
    private readonly AdminWorkspaceStorage _workspace = new();
    private readonly NetworkStatusService _network = new();
    private readonly GrevAgentClient _agent = new();
    private readonly GrevConnectResolver _connectResolver = new();
    private readonly UltraVncSessionService _vnc = new();
    private readonly VncCredentialService _credentials = new();
    private readonly AgentCredentialService _agentCredentials = new();
    private readonly DispatcherTimer _statusTimer = new();
    private readonly DispatcherTimer _searchDebounceTimer = new() { Interval = TimeSpan.FromMilliseconds(220) };
    private readonly Dictionary<Guid, GrevControlPanelWindow> _controlPanels = [];
    private AppSettings _settings = new();
    private bool _statusRefreshRunning;
    private bool _uiReady;
    private string _searchText = string.Empty;
    private string _machineFilter = "all";
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
        _searchDebounceTimer.Tick += SearchDebounceTimer_Tick;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _settings = await _storage.LoadSettingsAsync();
        var identityChanged = false;

        if (string.IsNullOrWhiteSpace(_settings.ControllerId))
        {
            _settings.ControllerId = Guid.NewGuid().ToString("N");
            identityChanged = true;
        }

        if (string.IsNullOrWhiteSpace(_settings.GrevName))
        {
            _settings.GrevName = string.IsNullOrWhiteSpace(Environment.UserName)
                ? "Grev User"
                : Environment.UserName;
            identityChanged = true;
        }

        if (identityChanged)
            await _storage.SaveSettingsAsync(_settings);

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
        UpdateMachineFilterStyles();
        RefreshMachineView();
        await RefreshStatusesAsync();
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _statusTimer.Stop();
        _searchDebounceTimer.Stop();
        _tray?.Dispose();
        _agent.Dispose();
        _connectResolver.Dispose();
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
