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
    private readonly PowerService _power = new();
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
        MachineAddressText.Text = $"{machine.ConnectDisplayText}  ·  VNC {machine.VncPort}";
        UpdateAgentButton.IsEnabled = false;
        SessionActionPanel.IsEnabled = false;

        Loaded += GrevControlPanelWindow_Loaded;
        Closed += GrevControlPanelWindow_Closed;
        _agentTimer.Tick += AgentTimer_Tick;
    }

    private async void GrevControlPanelWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _agentTimer.Start();
        DockCompactPanel();
        await RefreshAgentHealthAsync();
    }

    private void GrevControlPanelWindow_Closed(object? sender, EventArgs e)
    {
        _agentTimer.Stop();
        _agent.Dispose();
    }

    private async void AgentTimer_Tick(object? sender, EventArgs e) => await RefreshAgentHealthAsync();

    private async Task RefreshAgentHealthAsync()
    {
        if (_agentRefreshRunning || _agentUpdateRunning || _agentActionRunning) return;
        _agentRefreshRunning = true;

        try
        {
            MachineAddressText.Text = $"{_machine.ConnectDisplayText}  ·  VNC {_machine.VncPort}";
            var result = await _agent.ProbeAsync(_machine);
            _machine.AgentState = result.State;
            _machine.AgentStatus = result.Status;
            _machine.AgentMessage = result.Message;

            var sessionActionsReady = result.State == GrevAgentState.Connected && string.IsNullOrWhiteSpace(result.Message);
            SessionActionPanel.IsEnabled = sessionActionsReady;
            UpdateAgentButton.IsEnabled = result.State == GrevAgentState.Connected;
            UpdateAgentButton.Content = result.State == GrevAgentState.Connected && !string.IsNullOrWhiteSpace(result.Message)
                ? "⇩ Update Grev Agent · recommended"
                : "⇩ Update Grev Agent";

            if (result.State == GrevAgentState.Connected && result.Status is not null)
            {
                var status = result.Status;
                var usedMemory = Math.Max(0, status.TotalMemoryBytes - status.AvailableMemoryBytes);
                AgentConnectionText.Text = sessionActionsReady ? "● AGENT CONNECTED" : "● AGENT UPDATE RECOMMENDED";
                AgentConnectionText.Foreground = sessionActionsReady
                    ? new SolidColorBrush(Color.FromRgb(80, 220, 145))
                    : (Brush)FindResource("Accent2Brush");
                AgentCpuRamText.Text = $"CPU {status.CpuUsagePercent:0.#}%   ·   RAM {FormatGiB(usedMemory)} / {FormatGiB(status.TotalMemoryBytes)}";
                AgentUserUptimeText.Text = $"{status.InteractiveUser ?? "No console user"}   ·   Uptime {FormatUptime(status.UptimeSeconds)}";
                AgentVncHealthText.Text = $"UltraVNC {status.UltraVncServiceStatus}   ·   TCP {status.UltraVncPort} {(status.UltraVncPortListening ? "listening" : "not listening")}";
                AgentDiskText.Text = status.Disks.Count == 0
                    ? "No fixed-disk telemetry"
                    : string.Join("   ·   ", status.Disks.Take(2).Select(d => $"{d.Name.TrimEnd('\\')} {FormatGiB(d.FreeBytes)} free"));
                AgentUpdateStatusText.Text = string.IsNullOrWhiteSpace(result.Message) ? string.Empty : result.Message;
                return;
            }

            SessionActionPanel.IsEnabled = false;
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
        SessionActionPanel.IsEnabled = false;
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

    private void ManageMachine_Click(object sender, RoutedEventArgs e)
    {
        if (_machineOverview is not null)
        {
            if (!_machineOverview.IsVisible)
                _machineOverview.Show();

            _machineOverview.Activate();
            return;
        }

        var overview = new MachineOverviewWindow(_machine, _vnc) { Owner = this };
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
    private void FullScreen_Click(object sender, RoutedEventArgs e) => SendViewerAction(() => _vnc.ToggleFullScreen(_machine.Id));
    private void RefreshScreen_Click(object sender, RoutedEventArgs e) => SendViewerAction(() => _vnc.RequestScreenRefresh(_machine.Id));
    private void FileTransfer_Click(object sender, RoutedEventArgs e) => SendViewerAction(() => _vnc.OpenFileTransfer(_machine.Id));

    private async void LockMachine_Click(object sender, RoutedEventArgs e) =>
        await RunAgentQuickActionAsync("lock", "Lock workstation");

    private async void RestartExplorerAgent_Click(object sender, RoutedEventArgs e) =>
        await RunAgentQuickActionAsync("restart-explorer", "Restart Explorer");

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

    private string CurrentHost()
    {
        if (string.IsNullOrWhiteSpace(_machine.ActiveAddress))
            throw new InvalidOperationException("The current LAN / Grev Connect route is no longer available.");
        return _machine.ActiveAddress;
    }

    private async void Restart_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, $"Restart {_machine.Name} now?", "Confirm restart", MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        try
        {
            var result = await _power.RestartAsync(CurrentHost());
            MessageBox.Show(this, result.Message, "Restart", MessageBoxButton.OK,
                result.Success ? MessageBoxImage.Information : MessageBoxImage.Error);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Restart", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Shutdown_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, $"Shut down {_machine.Name} now?", "Confirm shutdown", MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        try
        {
            var result = await _power.ShutdownAsync(CurrentHost());
            MessageBox.Show(this, result.Message, "Shut down", MessageBoxButton.OK,
                result.Success ? MessageBoxImage.Information : MessageBoxImage.Error);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Shut down", MessageBoxButton.OK, MessageBoxImage.Error);
        }
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
