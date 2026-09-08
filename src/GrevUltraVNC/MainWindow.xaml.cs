using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
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
    private bool _firstRunPromptShown;
    private string _searchText = string.Empty;
    private string _machineFilter = "all";
    private TrayIconService? _tray;

    public ObservableCollection<Machine> Machines { get; } = [];
    public ICollectionView MachinesView { get; }

    public MainWindow()
    {
        MachinesView = CollectionViewSource.GetDefaultView(Machines);
        MachinesView.Filter = FilterMachine;

        InitializeComponent();
        Title = ProductBranding.ProductName;
        DataContext = this;
        _uiReady = true;

        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
        StateChanged += MainWindow_StateChanged;
        PreviewKeyDown += MainWindow_AboutKeyDown;
        _statusTimer.Tick += StatusTimer_Tick;
        _searchDebounceTimer.Tick += SearchDebounceTimer_Tick;

        // Keep the requested credits permanently visible at the very bottom of the dashboard.
        FooterViewerStatus.Text = ProductBranding.Credits;
        FooterViewerStatus.Cursor = Cursors.Hand;
        FooterViewerStatus.ToolTip = "About GrevConnect";
        FooterViewerStatus.MouseLeftButtonUp += (_, _) => OpenAbout();
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _settings = await _storage.LoadSettingsAsync();
        var settingsChanged = false;

        if (string.IsNullOrWhiteSpace(_settings.ControllerId))
        {
            _settings.ControllerId = Guid.NewGuid().ToString("N");
            settingsChanged = true;
        }

        if (string.IsNullOrWhiteSpace(_settings.GrevName))
        {
            _settings.GrevName = "User";
            settingsChanged = true;
        }

        var bundledViewer = Path.Combine(AppContext.BaseDirectory, "UltraVNC", "vncviewer.exe");
        if (string.IsNullOrWhiteSpace(_settings.UltraVncViewerPath) && File.Exists(bundledViewer))
        {
            _settings.UltraVncViewerPath = bundledViewer;
            settingsChanged = true;
        }

        if (settingsChanged)
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

        _tray = new TrayIconService(
            this,
            () => Machines.Where(machine => machine.IsFavorite),
            () => Machines,
            ConnectMachine,
            RefreshStatusesAsync,
            OpenAbout);
        ConfigureStatusTimer();
        UpdateMachineFilterStyles();
        ApplyViewArrangement();
        UpdateViewerStatusText();
        await RefreshStatusesAsync();

        if (Machines.Count == 0 && !_firstRunPromptShown)
        {
            _firstRunPromptShown = true;
            await OpenQuickConnectAsync();
        }
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

    private void MainWindow_AboutKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.F1) return;
        e.Handled = true;
        OpenAbout();
    }

    private void OpenAbout()
    {
        var about = new AboutWindow { Owner = this };
        about.ShowDialog();
    }

    private void ConfigureStatusTimer()
    {
        _statusTimer.Stop();
        _statusTimer.Interval = TimeSpan.FromSeconds(Math.Clamp(_settings.StatusCheckSeconds, 3, 300));
        _statusTimer.Start();
    }

    /// <summary>
    /// Preserve viewer health information without replacing the requested permanent credits footer.
    /// </summary>
    private void UpdateViewerStatusText()
    {
        if (!_uiReady) return;

        var viewer = _vnc.FindViewer(_settings.UltraVncViewerPath);
        FooterViewerStatus.Text = ProductBranding.Credits;

        if (string.IsNullOrWhiteSpace(viewer))
        {
            FooterViewerStatus.ToolTip = "UltraVNC Viewer not found · set its path in Settings · click for About";
            FooterViewerStatus.Foreground = ThemeService.ThemeBrush("WarnBrush");
            return;
        }

        FooterViewerStatus.ToolTip = $"UltraVNC Viewer ready · auto-check every {Math.Clamp(_settings.StatusCheckSeconds, 3, 300)}s · click for About";
        FooterViewerStatus.Foreground = ThemeService.ThemeBrush("FaintTextBrush");
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
