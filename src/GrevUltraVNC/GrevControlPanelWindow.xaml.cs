using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using GrevUltraVNC.Models;
using GrevUltraVNC.Services;

namespace GrevUltraVNC;

public partial class GrevControlPanelWindow : Window
{
    private readonly Machine _machine;
    private readonly UltraVncSessionService _vnc;
    private readonly WakeOnLanService _wake = new();
    private readonly PowerService _power = new();
    private readonly NetworkStatusService _network = new();
    private readonly RemoteUltraVncService _remoteVnc = new();
    private readonly DispatcherTimer _dockTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };

    public GrevControlPanelWindow(Machine machine, UltraVncSessionService vnc)
    {
        InitializeComponent();
        _machine = machine;
        _vnc = vnc;

        MachineNameText.Text = machine.Name;
        MachineAddressText.Text = $"{machine.IpAddress}  ·  VNC {machine.VncPort}";

        Loaded += GrevControlPanelWindow_Loaded;
        Closed += GrevControlPanelWindow_Closed;
        _dockTimer.Tick += DockTimer_Tick;
    }

    private void GrevControlPanelWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _dockTimer.Start();
        DockToViewer();
    }

    private void GrevControlPanelWindow_Closed(object? sender, EventArgs e) => _dockTimer.Stop();

    private void DockTimer_Tick(object? sender, EventArgs e)
    {
        if (!_vnc.HasActiveSession(_machine.Id))
        {
            SessionStatusText.Text = "● SESSION ENDED";
            Close();
            return;
        }

        DockToViewer();
    }

    private void DockToViewer()
    {
        if (!_vnc.TryGetViewerWindowHandle(_machine.Id, out var handle) || handle == IntPtr.Zero)
            return;

        if (!GetWindowRect(handle, out var viewerRect))
            return;

        var monitor = MonitorFromWindow(handle, MONITOR_DEFAULTTONEAREST);
        if (monitor == IntPtr.Zero)
            return;

        var info = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref info))
            return;

        var dpi = GetDpiForWindow(handle);
        var scale = dpi == 0 ? 1.0 : dpi / 96.0;

        var workLeft = info.rcWork.Left / scale;
        var workTop = info.rcWork.Top / scale;
        var workRight = info.rcWork.Right / scale;
        var workBottom = info.rcWork.Bottom / scale;

        var viewerLeft = viewerRect.Left / scale;
        var viewerTop = viewerRect.Top / scale;
        var viewerRight = viewerRect.Right / scale;
        var viewerBottom = viewerRect.Bottom / scale;

        const double gap = 8.0;
        var panelWidth = Width;
        var availableHeight = Math.Max(300, workBottom - workTop);
        var viewerHeight = Math.Max(300, viewerBottom - viewerTop);

        Height = Math.Min(availableHeight, Math.Max(580, viewerHeight));
        Top = Math.Clamp(viewerTop, workTop, Math.Max(workTop, workBottom - Height));

        if (viewerRight + gap + panelWidth <= workRight)
            Left = viewerRight + gap;
        else if (viewerLeft - gap - panelWidth >= workLeft)
            Left = viewerLeft - gap - panelWidth;
        else
            Left = Math.Max(workLeft, workRight - panelWidth);
    }

    private void Cad_Click(object sender, RoutedEventArgs e) => SendViewerAction(() => _vnc.SendCtrlAltDelete(_machine.Id));
    private void WindowsKey_Click(object sender, RoutedEventArgs e) => SendViewerAction(() => _vnc.SendWindowsKey(_machine.Id));
    private void TaskManager_Click(object sender, RoutedEventArgs e) => SendViewerAction(() => _vnc.SendCtrlShiftEscape(_machine.Id));
    private void AltTab_Click(object sender, RoutedEventArgs e) => SendViewerAction(() => _vnc.SendAltTab(_machine.Id));
    private void AltF4_Click(object sender, RoutedEventArgs e) => SendViewerAction(() => _vnc.SendAltF4(_machine.Id));
    private void WinR_Click(object sender, RoutedEventArgs e) => SendViewerAction(() => _vnc.SendWinR(_machine.Id));
    private void WinE_Click(object sender, RoutedEventArgs e) => SendViewerAction(() => _vnc.SendWinE(_machine.Id));
    private void WinL_Click(object sender, RoutedEventArgs e) => SendViewerAction(() => _vnc.SendWinL(_machine.Id));
    private void FullScreen_Click(object sender, RoutedEventArgs e) => SendViewerAction(() => _vnc.ToggleFullScreen(_machine.Id));
    private void RefreshScreen_Click(object sender, RoutedEventArgs e) => SendViewerAction(() => _vnc.RequestScreenRefresh(_machine.Id));
    private void FileTransfer_Click(object sender, RoutedEventArgs e) => SendViewerAction(() => _vnc.OpenFileTransfer(_machine.Id));

    private void SendViewerAction(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "VNC session", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void BringToFront_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _vnc.BringViewerToFront(_machine.Id);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "VNC session", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void Disconnect_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, $"Disconnect the VNC session to {_machine.Name}?", "Disconnect VNC",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        _vnc.Disconnect(_machine.Id);
        Close();
    }

    private async void Wake_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _wake.SendAsync(_machine.MacAddress);
            MessageBox.Show(this, "Wake-on-LAN packet sent.", "GrevUltraVNC", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Wake-on-LAN", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void StartVncService_Click(object sender, RoutedEventArgs e)
    {
        await RunVncServiceActionAsync(() => _remoteVnc.StartAsync(_machine.IpAddress), "Start UltraVNC");
    }

    private async void RestartVncService_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this,
                "Restarting the UltraVNC service will normally disconnect this active VNC session. Continue?",
                "Restart UltraVNC service", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        await RunVncServiceActionAsync(() => _remoteVnc.RestartAsync(_machine.IpAddress), "Restart UltraVNC");
    }

    private async void StopVncService_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this,
                "Stopping the UltraVNC service will disconnect this session. Continue?",
                "Stop UltraVNC service", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        await RunVncServiceActionAsync(() => _remoteVnc.StopAsync(_machine.IpAddress), "Stop UltraVNC");
    }

    private async Task RunVncServiceActionAsync(Func<Task<RemoteServiceResult>> action, string title)
    {
        try
        {
            var result = await action();
            MessageBox.Show(this, result.Message, title, MessageBoxButton.OK,
                result.Success ? MessageBoxImage.Information : MessageBoxImage.Error);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void EnableVncAutoStart_Click(object sender, RoutedEventArgs e)
    {
        await RunVncServiceActionAsync(() => _remoteVnc.EnableAutoStartAndStartAsync(_machine.IpAddress), "UltraVNC at boot");
    }

    private async void Restart_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, $"Restart {_machine.Name} now?", "Confirm restart", MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        var result = await _power.RestartAsync(_machine.IpAddress);
        MessageBox.Show(this, result.Message, "Restart", MessageBoxButton.OK,
            result.Success ? MessageBoxImage.Information : MessageBoxImage.Error);
    }

    private async void Shutdown_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, $"Shut down {_machine.Name} now?", "Confirm shutdown", MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        var result = await _power.ShutdownAsync(_machine.IpAddress);
        MessageBox.Show(this, result.Message, "Shut down", MessageBoxButton.OK,
            result.Success ? MessageBoxImage.Information : MessageBoxImage.Error);
    }

    private void Shares_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\\\\{_machine.IpAddress}\\",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Network shares", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Diagnostics_Click(object sender, RoutedEventArgs e)
    {
        var networkResult = await _network.ProbeAsync(_machine);
        var serviceResult = await _remoteVnc.QueryAsync(_machine.IpAddress);
        var latency = networkResult.LatencyMs is null ? "No ping response" : $"{networkResult.LatencyMs} ms";
        var vnc = networkResult.VncAvailable ? $"Reachable on TCP {_machine.VncPort}" : $"Not reachable on TCP {_machine.VncPort}";
        var service = serviceResult.Success ? serviceResult.Message : $"Could not query: {serviceResult.Message}";

        MessageBox.Show(this,
            $"Machine: {_machine.Name}\nIP: {_machine.IpAddress}\nPing: {latency}\nVNC port: {vnc}\nService: {service}\nProbe result: {networkResult.Status}",
            "Connection info", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void HidePanel_Click(object sender, RoutedEventArgs e) => Hide();

    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);
}
