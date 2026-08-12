using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using GrevUltraVNC.Models;
using GrevUltraVNC.Services;

namespace GrevUltraVNC;

public partial class GrevControlPanelWindow : Window
{
    private readonly Machine _machine;
    private readonly UltraVncSessionService _vnc;
    private readonly GrevAgentClient _agent = new();
    private readonly AgentUpdateService _agentUpdater;
    private readonly WakeOnLanService _wake = new();
    private readonly PowerService _power = new();
    private readonly NetworkStatusService _network = new();
    private readonly RemoteUltraVncService _remoteVnc = new();
    private readonly DispatcherTimer _dockTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private readonly DispatcherTimer _agentTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool _agentRefreshRunning;
    private bool _agentUpdateRunning;
    private bool _agentActionRunning;
    private MachineOverviewWindow? _machineOverview;

    public GrevControlPanelWindow(Machine machine, UltraVncSessionService vnc)
    {
        InitializeComponent();
        _machine = machine;
        _vnc = vnc;
        _agentUpdater = new AgentUpdateService(_agent);

        MachineNameText.Text = machine.Name;
        MachineAddressText.Text = $"{machine.IpAddress}  ·  VNC {machine.VncPort}";
        UpdateAgentButton.IsEnabled = false;

        Loaded += GrevControlPanelWindow_Loaded;
        Closed += GrevControlPanelWindow_Closed;
        _dockTimer.Tick += DockTimer_Tick;
        _agentTimer.Tick += AgentTimer_Tick;
    }

    private async void GrevControlPanelWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _dockTimer.Start();
        _agentTimer.Start();
        DockToViewer();
        await RefreshAgentHealthAsync();
    }

    private void GrevControlPanelWindow_Closed(object? sender, EventArgs e)
    {
        _dockTimer.Stop();
        _agentTimer.Stop();
        _agent.Dispose();
    }

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

    private async void AgentTimer_Tick(object? sender, EventArgs e) => await RefreshAgentHealthAsync();

    private async Task RefreshAgentHealthAsync()
    {
        if (_agentRefreshRunning || _agentUpdateRunning || _agentActionRunning) return;
        _agentRefreshRunning = true;

        try
        {
            var result = await _agent.ProbeAsync(_machine);
            _machine.AgentState = result.State;
            _machine.AgentStatus = result.Status;
            _machine.AgentMessage = result.Message;
            UpdateAgentButton.IsEnabled = result.State == GrevAgentState.Connected;
            UpdateAgentButton.Content = result.State == GrevAgentState.Connected && !string.IsNullOrWhiteSpace(result.Message)
                ? "⇩ Update Grev Agent · recommended"
                : "⇩ Update Grev Agent";

            if (result.State == GrevAgentState.Connected && result.Status is not null)
            {
                var status = result.Status;
                var usedMemory = Math.Max(0, status.TotalMemoryBytes - status.AvailableMemoryBytes);
                AgentConnectionText.Text = "● AGENT CONNECTED";
                AgentConnectionText.Foreground = new SolidColorBrush(Color.FromRgb(80, 220, 145));
                AgentCpuRamText.Text = $"CPU {status.CpuUsagePercent:0.#}%   ·   RAM {FormatGiB(usedMemory)} / {FormatGiB(status.TotalMemoryBytes)}";
                AgentUserUptimeText.Text = $"{status.InteractiveUser ?? "No console user"}   ·   Uptime {FormatUptime(status.UptimeSeconds)}";
                AgentVncHealthText.Text = $"UltraVNC {status.UltraVncServiceStatus}   ·   TCP {status.UltraVncPort} {(status.UltraVncPortListening ? "listening" : "not listening")}";
                AgentDiskText.Text = status.Disks.Count == 0
                    ? "No fixed-disk telemetry"
                    : string.Join("   ·   ", status.Disks.Take(2).Select(d => $"{d.Name.TrimEnd('\\')} {FormatGiB(d.FreeBytes)} free"));
                AgentUpdateStatusText.Text = string.IsNullOrWhiteSpace(result.Message) ? string.Empty : result.Message;
                return;
            }

            AgentConnectionText.Text = result.State switch
            {
                GrevAgentState.ReadyToPair => "● AGENT READY TO PAIR",
                GrevAgentState.AuthenticationFailed => "● AGENT KEY REJECTED",
                GrevAgentState.Error => "● AGENT ERROR",
                _ => "AGENT NOT DETECTED"
            };

            AgentConnectionText.Foreground = result.State is GrevAgentState.AuthenticationFailed or GrevAgentState.Error
                ? new SolidColorBrush(Color.FromRgb(255, 107, 119))
                : new SolidColorBrush(Color.FromRgb(98, 111, 130));
            AgentCpuRamText.Text = result.Message ?? "Install or pair Grev Agent to enable system telemetry.";
            AgentUserUptimeText.Text = string.Empty;
            AgentVncHealthText.Text = string.Empty;
            AgentDiskText.Text = string.Empty;
            AgentUpdateStatusText.Text = string.Empty;
        }
        finally
        {
            _agentRefreshRunning = false;
        }
    }

    private async void UpdateAgent_Click(object sender, RoutedEventArgs e)
    {
        if (_agentUpdateRunning) return;

        if (MessageBox.Show(this,
                $"Update Grev Agent on {_machine.Name} from the latest GitHub release?\n\nThe Agent service will restart. Your VNC session should remain open and the existing pairing key will be preserved.",
                "Update Grev Agent",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        _agentUpdateRunning = true;
        UpdateAgentButton.IsEnabled = false;
        AgentUpdateStatusText.Text = "Preparing Agent update…";

        try
        {
            var progress = new Progress<string>(message => AgentUpdateStatusText.Text = message);
            var result = await _agentUpdater.UpdateFromGitHubAsync(_machine, progress);
            _machine.AgentState = result.State;
            _machine.AgentStatus = result.Status;
            _machine.AgentMessage = result.Message;
            AgentUpdateStatusText.Text = "Grev Agent updated successfully.";

            MessageBox.Show(this,
                $"Grev Agent on {_machine.Name} updated successfully and is responding again.",
                "Grev Agent updated",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            AgentUpdateStatusText.Text = ex.Message;
            MessageBox.Show(this, ex.Message, "Grev Agent update", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _agentUpdateRunning = false;
            await RefreshAgentHealthAsync();
        }
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

        Height = Math.Min(availableHeight, Math.Max(600, viewerHeight));
        Top = Math.Clamp(viewerTop, workTop, Math.Max(workTop, workBottom - Height));

        if (viewerRight + gap + panelWidth <= workRight)
            Left = viewerRight + gap;
        else if (viewerLeft - gap - panelWidth >= workLeft)
            Left = viewerLeft - gap - panelWidth;
        else
            Left = Math.Max(workLeft, workRight - panelWidth);
    }

    private void ManageMachine_Click(object sender, RoutedEventArgs e)
    {
        if (_machineOverview is not null)
        {
            if (!_machineOverview.IsVisible)
                _machineOverview.Show();

            _machineOverview.Activate();
            return;
        }

        var overview = new MachineOverviewWindow(_machine) { Owner = this };
        _machineOverview = overview;
        overview.Closed += (_, _) => _machineOverview = null;
        overview.Show();
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

    private async void LockMachine_Click(object sender, RoutedEventArgs e) =>
        await RunAgentQuickActionAsync("lock", "Lock workstation");

    private async void RestartExplorerAgent_Click(object sender, RoutedEventArgs e) =>
        await RunAgentQuickActionAsync("restart-explorer", "Restart Explorer");

    private async void SignOutMachine_Click(object sender, RoutedEventArgs e) =>
        await RunAgentQuickActionAsync(
            "sign-out",
            "Sign out",
            $"Sign out the active user on {_machine.Name}?\n\nAny unsaved work in that Windows session can be lost.");

    private async void SleepMachine_Click(object sender, RoutedEventArgs e) =>
        await RunAgentQuickActionAsync(
            "sleep",
            "Sleep machine",
            $"Put {_machine.Name} to sleep?\n\nThe VNC session will disconnect until the machine wakes again.");

    private async void HibernateMachine_Click(object sender, RoutedEventArgs e) =>
        await RunAgentQuickActionAsync(
            "hibernate",
            "Hibernate machine",
            $"Hibernate {_machine.Name}?\n\nThe VNC session will disconnect until the machine is powered or woken again.");

    private async Task RunAgentQuickActionAsync(string action, string title, string? confirmation = null)
    {
        if (_agentActionRunning) return;

        if (!string.IsNullOrWhiteSpace(confirmation) &&
            MessageBox.Show(this, confirmation, title, MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        _agentActionRunning = true;
        AgentUpdateStatusText.Text = $"{title}…";

        try
        {
            var result = await _agent.RunQuickActionAsync(_machine, action);
            AgentUpdateStatusText.Text = result.Message;

            if (!result.Success)
                MessageBox.Show(this, result.Message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            AgentUpdateStatusText.Text = ex.Message;
            MessageBox.Show(this, ex.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _agentActionRunning = false;
        }
    }

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
        var agentResult = await _agent.ProbeAsync(_machine);
        var latency = networkResult.LatencyMs is null ? "No ping response" : $"{networkResult.LatencyMs} ms";
        var vnc = networkResult.VncAvailable ? $"Reachable on TCP {_machine.VncPort}" : $"Not reachable on TCP {_machine.VncPort}";
        var service = serviceResult.Success ? serviceResult.Message : $"Could not query: {serviceResult.Message}";
        var agent = agentResult.Status is null
            ? $"{agentResult.State}: {agentResult.Message}"
            : $"Connected · CPU {agentResult.Status.CpuUsagePercent:0.#}% · RAM {FormatGiB(agentResult.Status.TotalMemoryBytes - agentResult.Status.AvailableMemoryBytes)}/{FormatGiB(agentResult.Status.TotalMemoryBytes)}";

        MessageBox.Show(this,
            $"Machine: {_machine.Name}\nIP: {_machine.IpAddress}\nPing: {latency}\nVNC port: {vnc}\nUltraVNC service: {service}\nGrev Agent: {agent}\nProbe result: {networkResult.Status}",
            "Connection info", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void HidePanel_Click(object sender, RoutedEventArgs e) => Hide();

    private static string FormatGiB(long bytes) => $"{Math.Max(0, bytes) / 1024d / 1024d / 1024d:0.#} GB";

    private static string FormatUptime(long seconds)
    {
        var span = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return span.TotalDays >= 1
            ? $"{(int)span.TotalDays}d {span.Hours}h"
            : $"{(int)span.TotalHours}h {span.Minutes}m";
    }

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
