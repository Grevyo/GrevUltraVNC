using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using GrevUltraVNC.Contracts;
using GrevUltraVNC.Models;
using GrevUltraVNC.Services;

namespace GrevUltraVNC;

public partial class NamedCursorOverlayWindow : Window
{
    private readonly Machine _machine;
    private readonly UltraVncSessionService _vnc;
    private readonly bool _virtualDisplay;
    private string _preferredColor;
    private string _preferredCursorStyle;
    private readonly DispatcherTimer _dockTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private IReadOnlyList<AgentPresenceInfo> _participants = Array.Empty<AgentPresenceInfo>();
    private string _localControllerId = string.Empty;
    private FrameworkElement? _localCursorVisual;

    public NamedCursorOverlayWindow(
        Machine machine,
        UltraVncSessionService vnc,
        bool virtualDisplay,
        string preferredColor,
        string preferredCursorStyle)
    {
        InitializeComponent();
        _machine = machine;
        _vnc = vnc;
        _virtualDisplay = virtualDisplay;
        _preferredColor = CollaborationColors.Normalize(preferredColor);
        _preferredCursorStyle = CursorStyleCatalog.Normalize(preferredCursorStyle);

        SourceInitialized += NamedCursorOverlayWindow_SourceInitialized;
        Loaded += NamedCursorOverlayWindow_Loaded;
        Closed += NamedCursorOverlayWindow_Closed;
        SizeChanged += (_, _) => RenderCursors();
        _dockTimer.Tick += (_, _) => DockToViewer();
    }

    public void UpdateParticipants(IReadOnlyList<AgentPresenceInfo> participants, string localControllerId)
    {
        _participants = participants;
        _localControllerId = localControllerId;
        RenderCursors();
    }

    public void UpdatePreferredColor(string preferredColor)
    {
        _preferredColor = CollaborationColors.Normalize(preferredColor);
        RenderCursors();
    }

    public void UpdatePreferredCursorStyle(string preferredCursorStyle)
    {
        _preferredCursorStyle = CursorStyleCatalog.Normalize(preferredCursorStyle);
        RenderCursors();
    }

    private void NamedCursorOverlayWindow_SourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        var extendedStyle = GetWindowLongPtr(handle, GWL_EXSTYLE).ToInt64();
        extendedStyle |= WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
        SetWindowLongPtr(handle, GWL_EXSTYLE, new IntPtr(extendedStyle));
    }

    private void NamedCursorOverlayWindow_Loaded(object sender, RoutedEventArgs e)
    {
        DockToViewer();
        _dockTimer.Start();
        CompositionTarget.Rendering += CompositionTarget_Rendering;
    }

    private void NamedCursorOverlayWindow_Closed(object? sender, EventArgs e)
    {
        _dockTimer.Stop();
        CompositionTarget.Rendering -= CompositionTarget_Rendering;
    }

    private void CompositionTarget_Rendering(object? sender, EventArgs e) => MoveLocalCursor();

    private void DockToViewer()
    {
        if (!_vnc.TryGetViewerSurfaceBounds(_machine.Id, _virtualDisplay, out var bounds))
        {
            if (IsVisible) Hide();
            return;
        }

        var handle = _virtualDisplay
            ? (_vnc.TryGetVirtualViewerWindowHandle(_machine.Id, out var virtualHandle) ? virtualHandle : IntPtr.Zero)
            : (_vnc.TryGetViewerWindowHandle(_machine.Id, out var primaryHandle) ? primaryHandle : IntPtr.Zero);
        var dpi = handle == IntPtr.Zero ? 96u : GetDpiForWindow(handle);
        var scale = dpi == 0 ? 1d : dpi / 96d;

        var left = bounds.Left / scale;
        var top = bounds.Top / scale;
        var width = bounds.Width / scale;
        var height = bounds.Height / scale;

        if (width < 32 || height < 32)
        {
            if (IsVisible) Hide();
            return;
        }

        if (Math.Abs(Left - left) > 0.5) Left = left;
        if (Math.Abs(Top - top) > 0.5) Top = top;
        if (Math.Abs(Width - width) > 0.5) Width = width;
        if (Math.Abs(Height - height) > 0.5) Height = height;
        if (!IsVisible) Show();

        RenderCursors();
    }

    private void RenderCursors()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(RenderCursors);
            return;
        }

        if (CursorCanvas.ActualWidth <= 0 || CursorCanvas.ActualHeight <= 0)
            return;

        CursorCanvas.Children.Clear();
        _localCursorVisual = null;
        var expectedSurface = _virtualDisplay ? "screen2" : "screen1";
        AgentPresenceInfo? localParticipant = null;

        foreach (var participant in _participants)
        {
            var isLocal = string.Equals(
                participant.ControllerId,
                _localControllerId,
                StringComparison.OrdinalIgnoreCase);

            // The local pointer is drawn from the controller's real Windows cursor rather than
            // waiting for an Agent heartbeat. Keep the participant only for name/control state.
            if (isLocal)
            {
                localParticipant = participant;
                continue;
            }

            if (!participant.CursorVisible || participant.CursorX is null || participant.CursorY is null ||
                !string.Equals(participant.CursorSurface, expectedSurface, StringComparison.OrdinalIgnoreCase))
                continue;

            var visual = CreateCursorVisual(
                participant.DisplayName,
                participant.HasControl,
                isLocal: false,
                participant.Color,
                participant.CursorStyle);
            CursorCanvas.Children.Add(visual);
            PositionCursor(visual, participant.CursorX.Value, participant.CursorY.Value);
        }

        if (!string.IsNullOrWhiteSpace(_localControllerId))
        {
            // IMPORTANT: always use the controller's current local preference here. Previously the
            // last Agent snapshot could override this with its old CursorStyle, which made the picker
            // appear broken until another heartbeat arrived (and could stay wrong with an older Agent).
            _localCursorVisual = CreateCursorVisual(
                localParticipant?.DisplayName ?? "YOU",
                localParticipant?.HasControl == true,
                isLocal: true,
                _preferredColor,
                _preferredCursorStyle);
            CursorCanvas.Children.Add(_localCursorVisual);
            MoveLocalCursor();
        }
    }

    private FrameworkElement CreateCursorVisual(
        string displayName,
        bool hasControl,
        bool isLocal,
        string color,
        string cursorStyle)
    {
        var normalizedColor = CollaborationColors.Normalize(color);
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(normalizedColor)!);
        brush.Freeze();

        var label = hasControl
            ? $"{displayName} · CONTROL"
            : displayName;

        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(CursorVisualFactory.CreatePointer(cursorStyle, brush));
        panel.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(225, 7, 12, 22)),
            BorderBrush = brush,
            BorderThickness = new Thickness(isLocal ? 1.4 : 1),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(7, 3, 7, 3),
            Margin = new Thickness(3, 1, 0, 0),
            Child = new TextBlock
            {
                Text = label,
                Foreground = Brushes.White,
                FontSize = 11,
                FontWeight = hasControl ? FontWeights.SemiBold : FontWeights.Normal
            }
        });

        return panel;
    }

    private void MoveLocalCursor()
    {
        if (_localCursorVisual is null || CursorCanvas.ActualWidth <= 0 || CursorCanvas.ActualHeight <= 0)
            return;

        var expectedSurface = _virtualDisplay ? "screen2" : "screen1";
        if (!_vnc.TryGetLocalPointer(_machine.Id, out var surface, out var x, out var y) ||
            !string.Equals(surface, expectedSurface, StringComparison.OrdinalIgnoreCase))
        {
            _localCursorVisual.Visibility = Visibility.Collapsed;
            return;
        }

        _localCursorVisual.Visibility = Visibility.Visible;
        PositionCursor(_localCursorVisual, x, y);
    }

    private void PositionCursor(FrameworkElement visual, double normalizedX, double normalizedY)
    {
        var canvasWidth = CursorCanvas.ActualWidth;
        var canvasHeight = CursorCanvas.ActualHeight;
        var x = Math.Clamp(normalizedX, 0, 1) * Math.Max(0, canvasWidth - 1);
        var y = Math.Clamp(normalizedY, 0, 1) * Math.Max(0, canvasHeight - 1);

        visual.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        if (visual is not StackPanel panel || panel.Children.Count < 2 ||
            panel.Children[0] is not FrameworkElement pointer ||
            panel.Children[1] is not FrameworkElement label)
        {
            Canvas.SetLeft(visual, x);
            Canvas.SetTop(visual, y);
            return;
        }

        pointer.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var hotspot = pointer.Tag is Point point ? point : new Point(0, 0);

        // Anchor the real cursor hotspot to the remote coordinate. The label is never allowed to
        // push the cursor inward, so x=100% is a genuine right-edge position.
        var visualLeft = x - hotspot.X;
        var visualTop = y - hotspot.Y;
        Canvas.SetLeft(visual, visualLeft);
        Canvas.SetTop(visual, visualTop);

        // Only flip the name tag. The pointer itself remains exactly under the real mouse hotspot.
        var rightEdgeWithLabel = visualLeft + pointer.DesiredSize.Width + label.DesiredSize.Width;
        var wouldOverflowRight = rightEdgeWithLabel > canvasWidth;
        var flippedLeftEdge = visualLeft - label.DesiredSize.Width - 4;
        var canFlipLeft = flippedLeftEdge >= 0;
        label.RenderTransform = wouldOverflowRight && canFlipLeft
            ? new TranslateTransform(-(pointer.DesiredSize.Width + label.DesiredSize.Width + 4), 0)
            : new TranslateTransform(0, 0);
    }

    private static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex) =>
        IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : new IntPtr(GetWindowLong32(hWnd, nIndex));

    private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong) =>
        IntPtr.Size == 8 ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong) : new IntPtr(SetWindowLong32(hWnd, nIndex, dwNewLong.ToInt32()));

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TRANSPARENT = 0x00000020L;
    private const long WS_EX_TOOLWINDOW = 0x00000080L;
    private const long WS_EX_NOACTIVATE = 0x08000000L;
}
