using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace GrevUltraVNC;

public partial class GrevControlPanelWindow
{
    /// <summary>
    /// Compact-panel docking replaces the older fixed-height companion layout. The panel first
    /// sizes itself to the controls it actually contains, and only turns on vertical scrolling
    /// when the local monitor work area cannot fit that natural height.
    /// </summary>
    private void CompactPanel_ContentRendered(object? sender, EventArgs e)
    {
        _dockTimer.Stop();

        // ViewerEnhancements owns the timer because it also refreshes the Screen 2 lease. Swap
        // its old fixed-height handler for the compact adaptive handler rather than starting a
        // second positioning loop.
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

        var workHeight = Math.Max(520d, workBottom - workTop);
        var availableHeight = Math.Max(520d, workHeight - 12d);

        // ScrollViewer measures its StackPanel content vertically, so ExtentHeight is the natural
        // middle-section height even when the current window is smaller. Everything outside the
        // ScrollViewer is fixed chrome/presence/footer and is kept visible at all times.
        var fixedChromeHeight = ActualHeight > 0 && PanelScrollViewer.ActualHeight > 0
            ? Math.Max(0d, ActualHeight - PanelScrollViewer.ActualHeight)
            : 210d;
        var middleContentHeight = PanelScrollViewer.ExtentHeight > 0
            ? PanelScrollViewer.ExtentHeight
            : 400d;

        var naturalHeight = Math.Clamp(fixedChromeHeight + middleContentHeight + 8d, 620d, 860d);
        var needsScroll = naturalHeight > availableHeight + 1d;
        var targetHeight = needsScroll ? availableHeight : naturalHeight;

        MinHeight = Math.Min(520d, targetHeight);
        MaxHeight = Math.Max(targetHeight, Math.Min(960d, availableHeight));
        Height = targetHeight;
        PanelScrollViewer.VerticalScrollBarVisibility = needsScroll
            ? ScrollBarVisibility.Auto
            : ScrollBarVisibility.Disabled;

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
