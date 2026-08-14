using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using GrevUltraVNC.Contracts;
using GrevUltraVNC.Models;
using GrevUltraVNC.Services;

namespace GrevUltraVNC;

public partial class WhiteboardOverlayWindow : Window
{
    private readonly Machine _machine;
    private readonly UltraVncSessionService _vnc;
    private readonly AppSettings _settings;
    private readonly DispatcherTimer _dockTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly Dictionary<string, AgentWhiteboardEvent> _strokes = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Point> _activePoints = [];
    private Polyline? _activePolyline;
    private bool _drawing;
    private string _selectedColour = "#32CFF0";

    public event Action<AgentWhiteboardEvent>? WhiteboardEventCreated;

    public WhiteboardOverlayWindow(Machine machine, UltraVncSessionService vnc, AppSettings settings)
    {
        InitializeComponent();
        _machine = machine;
        _vnc = vnc;
        _settings = settings;

        Loaded += WhiteboardOverlayWindow_Loaded;
        Closed += WhiteboardOverlayWindow_Closed;
        SizeChanged += (_, _) => RenderAll();
        _dockTimer.Tick += (_, _) => DockToViewer();
    }

    private void WhiteboardOverlayWindow_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateColourSelection(DefaultColourButton);
        DockToViewer();
        _dockTimer.Start();
    }

    private void WhiteboardOverlayWindow_Closed(object? sender, EventArgs e)
    {
        _dockTimer.Stop();
    }

    private void DockToViewer()
    {
        if (!_vnc.TryGetViewerWindowHandle(_machine.Id, out var handle) || handle == IntPtr.Zero)
        {
            Close();
            return;
        }

        if (!GetWindowRect(handle, out var rect)) return;
        var dpi = GetDpiForWindow(handle);
        var scale = dpi == 0 ? 1d : dpi / 96d;

        var left = rect.Left / scale;
        var top = rect.Top / scale;
        var width = Math.Max(320, (rect.Right - rect.Left) / scale);
        var height = Math.Max(220, (rect.Bottom - rect.Top) / scale);

        if (Math.Abs(Left - left) > 0.5) Left = left;
        if (Math.Abs(Top - top) > 0.5) Top = top;
        if (Math.Abs(Width - width) > 0.5) Width = width;
        if (Math.Abs(Height - height) > 0.5) Height = height;
    }

    public void ApplyEvents(IEnumerable<AgentWhiteboardEvent> events)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => ApplyEvents(events.ToArray()));
            return;
        }

        var changed = false;
        foreach (var item in events)
        {
            if (string.Equals(item.Kind, "clear", StringComparison.OrdinalIgnoreCase))
            {
                _strokes.Clear();
                changed = true;
                continue;
            }

            if (!string.Equals(item.Kind, "stroke", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(item.StrokeId))
                continue;

            if (_strokes.ContainsKey(item.StrokeId)) continue;
            _strokes[item.StrokeId] = item;
            changed = true;
        }

        if (changed) RenderAll();
    }

    private void Colour_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string colour || string.IsNullOrWhiteSpace(colour))
            return;

        _selectedColour = colour;
        UpdateColourSelection(button);
        e.Handled = true;
    }

    private void UpdateColourSelection(Button selectedButton)
    {
        foreach (var swatch in ColourPalette.Children.OfType<Button>())
        {
            swatch.ClearValue(Control.BorderBrushProperty);
            swatch.ClearValue(Control.BorderThicknessProperty);
        }

        selectedButton.BorderBrush = Brushes.White;
        selectedButton.BorderThickness = new Thickness(3);
        SelectedColourText.Text = selectedButton.ToolTip?.ToString() ?? _selectedColour;
    }

    private void DrawingCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DrawingCanvas.ActualWidth <= 0 || DrawingCanvas.ActualHeight <= 0) return;

        _drawing = true;
        _activePoints.Clear();
        var point = e.GetPosition(DrawingCanvas);
        _activePoints.Add(point);

        _activePolyline = new Polyline
        {
            Stroke = GetStrokeBrush(_selectedColour),
            StrokeThickness = 3,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round
        };
        _activePolyline.Points.Add(point);
        DrawingCanvas.Children.Add(_activePolyline);
        Mouse.Capture(DrawingCanvas);
        e.Handled = true;
    }

    private void DrawingCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_drawing || _activePolyline is null || e.LeftButton != MouseButtonState.Pressed) return;

        var point = e.GetPosition(DrawingCanvas);
        if (_activePoints.Count > 0)
        {
            var previous = _activePoints[^1];
            if ((point - previous).Length < 2) return;
        }

        _activePoints.Add(point);
        _activePolyline.Points.Add(point);
    }

    private void DrawingCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_drawing) return;
        _drawing = false;
        Mouse.Capture(null);

        if (_activePoints.Count >= 2 && DrawingCanvas.ActualWidth > 0 && DrawingCanvas.ActualHeight > 0)
        {
            var points = _activePoints
                .Select(point => new AgentWhiteboardPoint(
                    Math.Clamp(point.X / DrawingCanvas.ActualWidth, 0, 1),
                    Math.Clamp(point.Y / DrawingCanvas.ActualHeight, 0, 1)))
                .ToArray();

            var stroke = new AgentWhiteboardEvent(
                0,
                _settings.ControllerId,
                _settings.GrevName,
                "stroke",
                Guid.NewGuid().ToString("N"),
                _selectedColour,
                3,
                points,
                DateTimeOffset.UtcNow);

            _strokes[stroke.StrokeId] = stroke;
            WhiteboardEventCreated?.Invoke(stroke);
        }

        _activePolyline = null;
        _activePoints.Clear();
        RenderAll();
        e.Handled = true;
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        _strokes.Clear();
        RenderAll();

        WhiteboardEventCreated?.Invoke(new AgentWhiteboardEvent(
            0,
            _settings.ControllerId,
            _settings.GrevName,
            "clear",
            $"clear-{Guid.NewGuid():N}",
            _selectedColour,
            3,
            Array.Empty<AgentWhiteboardPoint>(),
            DateTimeOffset.UtcNow));
    }

    private void Done_Click(object sender, RoutedEventArgs e) => Close();

    private void RenderAll()
    {
        if (DrawingCanvas.ActualWidth <= 0 || DrawingCanvas.ActualHeight <= 0) return;

        DrawingCanvas.Children.Clear();
        foreach (var stroke in _strokes.Values.OrderBy(item => item.EventId))
        {
            if (!string.Equals(stroke.Kind, "stroke", StringComparison.OrdinalIgnoreCase) || stroke.Points.Count < 2)
                continue;

            var line = new Polyline
            {
                Stroke = GetStrokeBrush(stroke.Color),
                StrokeThickness = Math.Clamp(stroke.Thickness, 1, 20),
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                IsHitTestVisible = false
            };

            foreach (var point in stroke.Points)
            {
                line.Points.Add(new Point(
                    point.X * DrawingCanvas.ActualWidth,
                    point.Y * DrawingCanvas.ActualHeight));
            }

            DrawingCanvas.Children.Add(line);
        }
    }

    private Brush GetStrokeBrush(string colour)
    {
        try
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(colour)!);
        }
        catch
        {
            return (Brush)FindResource("AccentBrush");
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);
}
