using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using GrevUltraVNC.Contracts;

namespace GrevUltraVNC;

public partial class GrevControlPanelWindow
{
    private readonly DispatcherTimer _adaptivePanelTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private bool _suppressScaleSlider;
    private bool _viewerScaleDragging;
    private bool _virtualDisplayLeaseActive;
    private bool _virtualDisplayLeaseRefreshRunning;
    private DateTimeOffset _lastVirtualDisplayLeaseRefreshUtc = DateTimeOffset.MinValue;
    private int _virtualMonitorIndex = -1;

    private void AdaptivePanel_ContentRendered(object? sender, EventArgs e)
    {
        _dockTimer.Stop();
        _adaptivePanelTimer.Tick -= AdaptivePanelTimer_Tick;
        _adaptivePanelTimer.Tick += AdaptivePanelTimer_Tick;
        _adaptivePanelTimer.Start();
        Dispatcher.BeginInvoke(DockAdaptivePanel, DispatcherPriority.Loaded);
    }

    private void AdaptivePanel_Closed(object? sender, EventArgs e) => _adaptivePanelTimer.Stop();

    private async void AdaptivePanelTimer_Tick(object? sender, EventArgs e)
    {
        if (!_viewerScaleDragging)
            DockAdaptivePanel();
        await RefreshVirtualDisplayLeaseAsync();
    }

    private void DockAdaptivePanel()
    {
        if (!_vnc.TryGetViewerWindowHandle(_machine.Id, out var viewerHandle) || viewerHandle == IntPtr.Zero)
            return;
        if (!GetWindowRect(viewerHandle, out var viewerRect))
            return;

        var monitor = MonitorFromWindow(viewerHandle, MONITOR_DEFAULTTONEAREST);
        if (monitor == IntPtr.Zero)
            return;

        var info = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref info))
            return;

        var dpi = GetDpiForWindow(viewerHandle);
        var scale = dpi == 0 ? 1d : dpi / 96d;
        var workLeft = info.rcWork.Left / scale;
        var workTop = info.rcWork.Top / scale;
        var workRight = info.rcWork.Right / scale;
        var workBottom = info.rcWork.Bottom / scale;
        var viewerLeft = viewerRect.Left / scale;
        var viewerRight = viewerRect.Right / scale;

        const double gap = 8;
        const double desiredHeight = 900;
        const double chromeMargin = 14;
        const double minimumUsableHeight = 560;
        var workHeight = Math.Max(minimumUsableHeight, workBottom - workTop);
        var availableHeight = Math.Max(minimumUsableHeight, workHeight - chromeMargin);
        var targetHeight = Math.Min(desiredHeight, availableHeight);
        var needsScroll = availableHeight + 1 < desiredHeight;

        MinHeight = targetHeight;
        MaxHeight = Math.Max(targetHeight, Math.Min(1040, availableHeight));
        Height = targetHeight;
        Top = workTop + Math.Max(0, (workHeight - targetHeight) / 2d);
        PanelScrollViewer.VerticalScrollBarVisibility = needsScroll
            ? ScrollBarVisibility.Auto
            : ScrollBarVisibility.Disabled;

        var panelWidth = ActualWidth > 0 ? ActualWidth : Width;
        if (viewerRight + gap + panelWidth <= workRight)
            Left = viewerRight + gap;
        else if (viewerLeft - gap - panelWidth >= workLeft)
            Left = viewerLeft - gap - panelWidth;
        else
            Left = Math.Max(workLeft, workRight - panelWidth);
    }

    private void ViewerScale_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string tag)
            return;

        try
        {
            if (string.Equals(tag, "fit", StringComparison.OrdinalIgnoreCase))
            {
                _vnc.FitToWindow(_machine.Id);
                ZoomStatusText.Text = "Fit";
                return;
            }

            if (!int.TryParse(tag, out var percent))
                return;

            ApplyViewerScale(percent, syncSlider: true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Viewer size", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void ViewerScaleSlider_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _viewerScaleDragging = true;
    }

    private void ViewerScaleSlider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) =>
        EndViewerScaleDrag();

    private void ViewerScaleSlider_LostMouseCapture(object sender, MouseEventArgs e) =>
        EndViewerScaleDrag();

    private async void EndViewerScaleDrag()
    {
        if (!_viewerScaleDragging)
            return;

        _viewerScaleDragging = false;
        await Task.Delay(650);
        if (IsLoaded)
            DockCompactPanel();
    }

    private void ViewerScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressScaleSlider || !IsLoaded)
            return;

        try
        {
            var percent = (int)Math.Round(e.NewValue / 5d) * 5;
            ApplyViewerScale(percent, syncSlider: false);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Viewer size", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void ApplyViewerScale(int percent, bool syncSlider)
    {
        percent = Math.Clamp(percent, 10, 300);
        _vnc.SetScale(_machine.Id, percent);
        ZoomStatusText.Text = $"{percent}%";

        if (!syncSlider || ViewerScaleSlider is null)
            return;

        _suppressScaleSlider = true;
        try
        {
            ViewerScaleSlider.Value = percent;
        }
        finally
        {
            _suppressScaleSlider = false;
        }
    }

    private async void CreateScreen2_Click(object sender, RoutedEventArgs e)
    {
        if (_virtualDisplayStarting) return;

        if (_vnc.HasVirtualSession(_machine.Id))
        {
            try
            {
                await ForcePrimaryViewerToScreen1Async();
                _vnc.BringVirtualViewerToFront(_machine.Id);
                DisplayStatusText.Text = "Screen 1 physical · Screen 2 virtual";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Screen 2", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            return;
        }

        _virtualDisplayStarting = true;
        VirtualDisplayButton.IsEnabled = false;
        VirtualDisplayButton.Content = "Creating Screen 2…";
        DisplayStatusText.Text = "Creating virtual monitor on host…";
        CollaborationStatusText.Text = "Creating Screen 2";

        var leaseCreated = false;
        try
        {
            var (width, height) = GetPreferredVirtualDisplaySize();
            var display = await _collaborationClient.RunDisplayAsync(
                _machine,
                new AgentDisplayRequest(
                    "create",
                    _collaborationSettings.ControllerId,
                    width,
                    height));

            if (!display.Success || !display.VirtualDisplayActive || display.VirtualMonitorIndex < 1)
                throw new InvalidOperationException(display.Message);

            leaseCreated = true;
            _virtualDisplayLeaseActive = true;
            _virtualMonitorIndex = display.VirtualMonitorIndex;
            _lastVirtualDisplayLeaseRefreshUtc = DateTimeOffset.UtcNow;

            var virtualInfo = display.Displays.FirstOrDefault(item => item.IsVirtual);
            DisplayStatusText.Text = virtualInfo is null
                ? "Host Screen 2 attached · opening viewer…"
                : $"Host Screen 2 attached · {virtualInfo.Width}×{virtualInfo.Height} · opening viewer…";

            if (_machine.VncPort >= 65535)
                throw new InvalidOperationException("Screen 2 needs a second VNC port, but the primary VNC port is already 65535.");

            var screen2Machine = _machine.Clone();
            screen2Machine.VncPort = _machine.VncPort + 1;

            await _vnc.OpenVirtualDisplayAsync(
                screen2Machine,
                _collaborationSettings,
                display.VirtualMonitorIndex);

            await ForcePrimaryViewerToScreen1Async();

            EnsureCursorOverlays();
            var localHasControl = string.Equals(
                _controlOwnerId,
                _collaborationSettings.ControllerId,
                StringComparison.OrdinalIgnoreCase);
            _vnc.SetViewOnly(_machine.Id, !localHasControl);
            UpdateDisplayState();
            DisplayStatusText.Text = virtualInfo is null
                ? $"Screen 1 physical · Screen 2 virtual · VNC {_machine.VncPort + 1}"
                : $"Screen 1 physical · Screen 2 virtual · {virtualInfo.Width}×{virtualInfo.Height} · VNC {_machine.VncPort + 1}";
            CollaborationStatusText.Text = "Screen 2 ready";
        }
        catch (Exception ex)
        {
            try { _vnc.CloseVirtualDisplay(_machine.Id); } catch { }
            if (leaseCreated)
                await ReleaseVirtualDisplayLeaseAsync(closeViewer: false);
            UpdateDisplayState();
            CollaborationStatusText.Text = "Screen 2 unavailable";
            MessageBox.Show(this, ex.Message, "Virtual Screen 2", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        finally
        {
            _virtualDisplayStarting = false;
            UpdateDisplayState();
        }
    }

    private async Task ForcePrimaryViewerToScreen1Async()
    {
        if (!_vnc.TryGetViewerWindowHandle(_machine.Id, out var viewerHandle) || viewerHandle == IntPtr.Zero)
            return;

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var monitorPointer = Marshal.AllocHGlobal(sizeof(int));
            try
            {
                Marshal.WriteInt32(monitorPointer, 0);
                var data = new PrimaryMonitorCopyData
                {
                    dwData = new UIntPtr(2),
                    cbData = sizeof(int),
                    lpData = monitorPointer
                };
                SendPrimaryMonitorMessage(viewerHandle, WmCopyDataForMonitorSelection, IntPtr.Zero, ref data);
            }
            finally
            {
                Marshal.FreeHGlobal(monitorPointer);
            }

            if (attempt < 2)
                await Task.Delay(250);
        }
    }

    private async void CloseScreen2_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await ReleaseVirtualDisplayLeaseAsync(closeViewer: true);
            if (_screen2CursorOverlay is not null)
            {
                try { _screen2CursorOverlay.Close(); } catch { }
                _screen2CursorOverlay = null;
            }
            CollaborationStatusText.Text = "Screen 2 closed";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Screen 2", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        finally
        {
            UpdateDisplayState();
        }
    }

    private async Task RefreshVirtualDisplayLeaseAsync()
    {
        if (!_virtualDisplayLeaseActive || _virtualDisplayLeaseRefreshRunning)
            return;

        if (!_vnc.HasVirtualSession(_machine.Id))
        {
            await ReleaseVirtualDisplayLeaseAsync(closeViewer: false);
            return;
        }

        if (DateTimeOffset.UtcNow - _lastVirtualDisplayLeaseRefreshUtc < TimeSpan.FromSeconds(5))
            return;

        _virtualDisplayLeaseRefreshRunning = true;
        try
        {
            var response = await _collaborationClient.RunDisplayAsync(
                _machine,
                new AgentDisplayRequest("heartbeat", _collaborationSettings.ControllerId));
            if (!response.Success || !response.VirtualDisplayActive)
            {
                _virtualDisplayLeaseActive = false;
                _virtualMonitorIndex = -1;
                return;
            }
            _lastVirtualDisplayLeaseRefreshUtc = DateTimeOffset.UtcNow;
        }
        catch
        {
        }
        finally
        {
            _virtualDisplayLeaseRefreshRunning = false;
        }
    }

    private async Task ReleaseVirtualDisplayLeaseAsync(bool closeViewer)
    {
        if (closeViewer)
        {
            try { _vnc.CloseVirtualDisplay(_machine.Id); } catch { }
        }

        if (!_virtualDisplayLeaseActive)
        {
            _virtualMonitorIndex = -1;
            return;
        }

        try
        {
            await _collaborationClient.RunDisplayAsync(
                _machine,
                new AgentDisplayRequest("release", _collaborationSettings.ControllerId));
        }
        finally
        {
            _virtualDisplayLeaseActive = false;
            _virtualMonitorIndex = -1;
            _lastVirtualDisplayLeaseRefreshUtc = DateTimeOffset.MinValue;
        }
    }

    private (int Width, int Height) GetPreferredVirtualDisplaySize()
    {
        if (_vnc.TryGetViewerWindowHandle(_machine.Id, out var viewerHandle) && viewerHandle != IntPtr.Zero)
        {
            var screen = System.Windows.Forms.Screen.FromHandle(viewerHandle);
            return (
                Math.Clamp(screen.Bounds.Width, 800, 7680),
                Math.Clamp(screen.Bounds.Height, 600, 4320));
        }

        return (1920, 1080);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PrimaryMonitorCopyData
    {
        public UIntPtr dwData;
        public int cbData;
        public IntPtr lpData;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SendPrimaryMonitorMessage(
        IntPtr hWnd,
        uint msg,
        IntPtr wParam,
        ref PrimaryMonitorCopyData lParam);

    private const uint WmCopyDataForMonitorSelection = 0x004A;
}
