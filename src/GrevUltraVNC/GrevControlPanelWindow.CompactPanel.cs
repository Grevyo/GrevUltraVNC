using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace GrevUltraVNC;

public partial class GrevControlPanelWindow
{
    private const double CompactPanelDesiredHeight = 820d;
    private const double CompactPanelMinimumHeight = 480d;

    /// <summary>
    /// Compact-panel docking is deterministic: the panel has one normal height and only becomes
    /// shorter when the local monitor work area cannot fit it. This avoids feeding the current
    /// ScrollViewer extent back into the next resize and producing the old slow-expansion loop.
    /// </summary>
    private void CompactPanel_ContentRendered(object? sender, EventArgs e)
    {
        _dockTimer.Stop();

        // ViewerEnhancements owns this timer because it also refreshes the Screen 2 lease. Swap
        // its old fixed-height handler for the compact handler so there is one positioning loop.
        _adaptivePanelTimer.Tick -= AdaptivePanelTimer_Tick;
        _adaptivePanelTimer.Tick -= CompactPanelTimer_Tick;
        _adaptivePanelTimer.Tick += CompactPanelTimer_Tick;
        _adaptivePanelTimer.Start();

        Dispatcher.BeginInvoke(DockCompactPanel, DispatcherPriority.Loaded);
    }

    private void CompactPanel_Closed(object? sender, EventArgs e)
    {
        _adaptivePanelTimer.Stop();
        _adaptivePanelTimer.Tick -= CompactPanelTimer_Tick;
    }

    private async void CompactPanelTimer_Tick(object? sender, EventArgs e)
    {
        // The old companion-panel timer used to perform this session-ended check. When compact
        // docking replaced that timer the check was accidentally lost, so manually closing the
        // UltraVNC window left the Grev panel alive. Require both the tracked session and an
        // actual viewer window to remain present.
        if (!_vnc.HasActiveSession(_machine.Id) ||
            !_vnc.TryGetViewerWindowHandle(_machine.Id, out var viewerHandle) ||
            viewerHandle == IntPtr.Zero)
        {
            SessionStatusText.Text = "● SESSION ENDED";
            Close();
            return;
        }

        if (!_viewerScaleDragging)
            DockCompactPanel();

        // Keep the existing Screen 2 lease behaviour intact while this timer owns docking.
        await RefreshVirtualDisplayLeaseAsync();
    }

    private void DockCompactPanel()
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

        var workHeight = Math.Max(320d, workBottom - workTop);
        var availableHeight = Math.Max(320d, workHeight - 12d);
        var targetHeight = Math.Min(CompactPanelDesiredHeight, availableHeight);
        var needsScroll = targetHeight + 1d < CompactPanelDesiredHeight;

        // Do not assign Height every 300 ms when nothing changed. Apart from avoiding needless
        // WPF layout passes, this makes the panel appear at its final size immediately instead of
        // visibly walking towards it over several timer ticks.
        var minimumHeight = Math.Min(CompactPanelMinimumHeight, targetHeight);
        if (Math.Abs(MinHeight - minimumHeight) > 0.5d)
            MinHeight = minimumHeight;
        if (Math.Abs(MaxHeight - targetHeight) > 0.5d)
            MaxHeight = targetHeight;
        if (Math.Abs(Height - targetHeight) > 0.5d)
            Height = targetHeight;

        var requestedScrollMode = needsScroll
            ? ScrollBarVisibility.Auto
            : ScrollBarVisibility.Disabled;
        if (PanelScrollViewer.VerticalScrollBarVisibility != requestedScrollMode)
            PanelScrollViewer.VerticalScrollBarVisibility = requestedScrollMode;

        Top = workTop + Math.Max(0d, (workHeight - targetHeight) / 2d);

        const double gap = 8d;
        var panelWidth = ActualWidth > 0 ? ActualWidth : Width;
        if (viewerRight + gap + panelWidth <= workRight)
            Left = viewerRight + gap;
        else if (viewerLeft - gap - panelWidth >= workLeft)
            Left = viewerLeft - gap - panelWidth;
        else
            Left = Math.Max(workLeft, workRight - panelWidth);
    }
}
