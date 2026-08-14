using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using GrevUltraVNC.Contracts;
using GrevUltraVNC.Models;
using GrevUltraVNC.Services;
using Shapes = System.Windows.Shapes;

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

            // Our own cursor is intentionally not drawn from the collaboration heartbeat.
            // It is rendered directly from the controller's current Windows pointer below so
            // the local Grev cursor stays glued to the real mouse with no network round trip.
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
            _localCursorVisual = CreateCursorVisual(
                localParticipant?.DisplayName ?? "YOU",
                localParticipant?.HasControl == true,
                isLocal: true,
                localParticipant?.Color ?? _preferredColor,
                localParticipant?.CursorStyle ?? _preferredCursorStyle);
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
        panel.Children.Add(CreatePointerVisual(cursorStyle, brush));
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

    private static FrameworkElement CreatePointerVisual(string cursorStyle, Brush brush)
    {
        return CursorStyleCatalog.Normalize(cursorStyle) switch
        {
            CursorStyleCatalog.Arrow => CreateArrowPointer(brush),
            CursorStyleCatalog.Crosshair => CreateCrosshairPointer(brush),
            CursorStyleCatalog.Ring => CreateRingPointer(brush),
            CursorStyleCatalog.Diamond => CreateDiamondPointer(brush),
            CursorStyleCatalog.Pixel => CreatePixelPointer(brush),
            _ => CreateGrevPointer(brush)
        };
    }

    private static FrameworkElement CreateGrevPointer(Brush brush)
    {
        // Recreated from the cyan outline sketch supplied for GrevUltraVNC. The intentionally
        // wonky silhouette is the point; it stays a clean vector and inherits each user's colour.
        var canvas = new Canvas { Width = 34, Height = 34, Tag = new Point(3, 3) };
        var path = new Shapes.Path
        {
            Data = Geometry.Parse("M 8,4 C 13,1 23,2 27,6 L 29,14 C 33,19 39,25 47,31 L 57,30 C 64,29 72,31 77,36 C 82,41 83,49 80,55 C 77,61 71,64 65,64 C 63,70 58,75 52,78 C 46,81 39,79 35,75 C 31,71 30,65 32,59 L 37,52 L 29,45 C 24,40 20,34 17,31 L 11,31 C 7,31 4,28 3,24 L 1,14 C 1,9 3,6 8,4 Z"),
            Stroke = brush,
            StrokeThickness = 4.5,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Fill = Brushes.Transparent,
            Stretch = Stretch.Uniform,
            Width = 32,
            Height = 32
        };
        Canvas.SetLeft(path, 1);
        Canvas.SetTop(path, 1);
        canvas.Children.Add(path);
        return canvas;
    }

    private static FrameworkElement CreateArrowPointer(Brush brush)
    {
        var grid = new Grid { Width = 26, Height = 28, Tag = new Point(1, 1) };
        grid.Children.Add(new Shapes.Path
        {
            Data = Geometry.Parse("M 1,1 L 2,23 L 8,17 L 13,27 L 18,24 L 13,15 L 22,15 Z"),
            Fill = brush,
            Stroke = Brushes.Black,
            StrokeThickness = 1,
            StrokeLineJoin = PenLineJoin.Round
        });
        return grid;
    }

    private static FrameworkElement CreateCrosshairPointer(Brush brush)
    {
        var canvas = new Canvas { Width = 28, Height = 28, Tag = new Point(14, 14) };
        canvas.Children.Add(new Shapes.Ellipse
        {
            Width = 16,
            Height = 16,
            Stroke = brush,
            StrokeThickness = 2.5
        });
        Canvas.SetLeft(canvas.Children[^1], 6);
        Canvas.SetTop(canvas.Children[^1], 6);

        var horizontal = new Shapes.Line { X1 = 1, X2 = 27, Y1 = 14, Y2 = 14, Stroke = brush, StrokeThickness = 2 };
        var vertical = new Shapes.Line { X1 = 14, X2 = 14, Y1 = 1, Y2 = 27, Stroke = brush, StrokeThickness = 2 };
        canvas.Children.Add(horizontal);
        canvas.Children.Add(vertical);
        return canvas;
    }

    private static FrameworkElement CreateRingPointer(Brush brush)
    {
        var canvas = new Canvas { Width = 26, Height = 26, Tag = new Point(13, 13) };
        var ring = new Shapes.Ellipse
        {
            Width = 20,
            Height = 20,
            Stroke = brush,
            StrokeThickness = 3,
            Fill = new SolidColorBrush(Color.FromArgb(28, 255, 255, 255))
        };
        Canvas.SetLeft(ring, 3);
        Canvas.SetTop(ring, 3);
        canvas.Children.Add(ring);
        return canvas;
    }

    private static FrameworkElement CreateDiamondPointer(Brush brush)
    {
        var canvas = new Canvas { Width = 26, Height = 26, Tag = new Point(13, 13) };
        canvas.Children.Add(new Shapes.Polygon
        {
            Points = new PointCollection
            {
                new(13, 1), new(25, 13), new(13, 25), new(1, 13)
            },
            Stroke = brush,
            StrokeThickness = 2.5,
            Fill = new SolidColorBrush(Color.FromArgb(42, 255, 255, 255)),
            StrokeLineJoin = PenLineJoin.Round
        });
        var dot = new Shapes.Ellipse { Width = 5, Height = 5, Fill = brush };
        Canvas.SetLeft(dot, 10.5);
        Canvas.SetTop(dot, 10.5);
        canvas.Children.Add(dot);
        return canvas;
    }

    private static FrameworkElement CreatePixelPointer(Brush brush)
    {
        var grid = new Grid { Width = 27, Height = 29, Tag = new Point(1, 1) };
        grid.Children.Add(new Shapes.Path
        {
            Data = Geometry.Parse("M 1,1 L 1,22 L 6,22 L 6,17 L 10,17 L 15,28 L 20,25 L 15,15 L 23,15 L 23,11 L 18,11 L 18,8 L 13,8 L 13,5 L 8,5 L 8,1 Z"),
            Fill = brush,
            Stroke = Brushes.Black,
            StrokeThickness = 1,
            StrokeLineJoin = PenLineJoin.Miter
        });
        return grid;
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

        // The hotspot, not the whole cursor/name panel, is anchored to the remote coordinate.
        // That means the cursor can genuinely reach x=100% even with a long Grev Name attached.
        var visualLeft = x - hotspot.X;
        var visualTop = y - hotspot.Y;
        Canvas.SetLeft(visual, visualLeft);
        Canvas.SetTop(visual, visualTop);

        // Only flip the name tag. Never move the pointer hotspot away from the real mouse.
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
