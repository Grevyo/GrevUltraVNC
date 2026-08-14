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
    private const double PenThickness = 3d;
    private const double HighlighterThickness = 16d;
    private const byte HighlighterAlpha = 0x66;
    private const int MaxUndoActions = 40;

    private readonly Machine _machine;
    private readonly UltraVncSessionService _vnc;
    private readonly AppSettings _settings;
    private readonly DispatcherTimer _dockTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly Dictionary<string, AgentWhiteboardEvent> _strokes = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Point> _activePoints = [];
    private readonly List<WhiteboardUndoAction> _undoHistory = [];
    private readonly Dictionary<string, AgentWhiteboardEvent> _erasedThisGesture = new(StringComparer.OrdinalIgnoreCase);
    private Polyline? _activePolyline;
    private Button? _eraserButton;
    private Button? _undoButton;
    private Point? _lastErasePoint;
    private bool _drawing;
    private string _selectedColour = "#32CFF0";
    private string _selectedTool = "pen";

    public event Action<IReadOnlyList<AgentWhiteboardEvent>>? WhiteboardEventsCreated;

    public WhiteboardOverlayWindow(Machine machine, UltraVncSessionService vnc, AppSettings settings)
    {
        InitializeComponent();
        _machine = machine;
        _vnc = vnc;
        _settings = settings;
        AddEditingButtons();

        Loaded += WhiteboardOverlayWindow_Loaded;
        Closed += WhiteboardOverlayWindow_Closed;
        SizeChanged += (_, _) => RenderAll();
        _dockTimer.Tick += (_, _) => DockToViewer();
    }

    private void AddEditingButtons()
    {
        if (PenButton.Parent is not StackPanel toolPanel)
            return;

        PenButton.MinWidth = 68;
        HighlighterButton.MinWidth = 86;

        _eraserButton = new Button
        {
            Content = "⌫  Eraser",
            Tag = "eraser",
            Style = (Style)FindResource("WhiteboardToolButton"),
            MinWidth = 78
        };
        _eraserButton.Click += Tool_Click;

        _undoButton = new Button
        {
            Content = "↶  Undo",
            Style = (Style)FindResource("WhiteboardToolButton"),
            MinWidth = 72,
            IsEnabled = false,
            ToolTip = "Undo your last whiteboard action"
        };
        _undoButton.Click += Undo_Click;

        toolPanel.Children.Add(_eraserButton);
        toolPanel.Children.Add(_undoButton);
    }

    private void WhiteboardOverlayWindow_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateColourSelection(DefaultColourButton);
        UpdateToolSelection(PenButton);
        UpdateUndoButton();
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
        var undoChanged = false;
        foreach (var item in events)
        {
            if (string.Equals(item.Kind, "clear", StringComparison.OrdinalIgnoreCase))
            {
                _strokes.Clear();
                changed = true;

                if (!string.Equals(item.ControllerId, _settings.ControllerId, StringComparison.OrdinalIgnoreCase))
                {
                    _undoHistory.Clear();
                    undoChanged = true;
                }
                continue;
            }

            if (string.Equals(item.Kind, "delete", StringComparison.OrdinalIgnoreCase))
            {
                if (_strokes.Remove(item.StrokeId))
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
        if (undoChanged) UpdateUndoButton();
    }

    private void Tool_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string tool || string.IsNullOrWhiteSpace(tool))
            return;

        _selectedTool = tool;
        UpdateToolSelection(button);
        e.Handled = true;
    }

    private void UpdateToolSelection(Button selectedButton)
    {
        var buttons = new List<Button> { PenButton, HighlighterButton };
        if (_eraserButton is not null) buttons.Add(_eraserButton);

        foreach (var button in buttons)
        {
            button.ClearValue(Control.BorderBrushProperty);
            button.ClearValue(Control.BorderThicknessProperty);
        }

        selectedButton.BorderBrush = (Brush)FindResource("AccentBrush");
        selectedButton.BorderThickness = new Thickness(2);
        SelectedToolText.Text = IsEraser
            ? "Eraser · whole stroke"
            : IsHighlighter
                ? $"Highlighter · {HighlighterThickness:0} px · transparent"
                : $"Pen · {PenThickness:0} px";
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
        var point = e.GetPosition(DrawingCanvas);

        if (IsEraser)
        {
            _erasedThisGesture.Clear();
            _lastErasePoint = point;
            if (EraseAt(point)) RenderAll();
            Mouse.Capture(DrawingCanvas);
            e.Handled = true;
            return;
        }

        _activePoints.Clear();
        _activePoints.Add(point);

        _activePolyline = new Polyline
        {
            Stroke = GetStrokeBrush(CurrentStrokeColour),
            StrokeThickness = CurrentStrokeThickness,
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
        if (!_drawing || e.LeftButton != MouseButtonState.Pressed) return;

        var point = e.GetPosition(DrawingCanvas);
        if (IsEraser)
        {
            var changed = false;
            if (_lastErasePoint is Point previous)
            {
                var delta = point - previous;
                var distance = delta.Length;
                var steps = Math.Max(1, (int)Math.Ceiling(distance / 5d));
                for (var step = 1; step <= steps; step++)
                {
                    var sample = previous + (delta * (step / (double)steps));
                    changed |= EraseAt(sample);
                }
            }
            else
            {
                changed = EraseAt(point);
            }

            _lastErasePoint = point;
            if (changed) RenderAll();
            return;
        }

        if (_activePolyline is null) return;
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

        if (IsEraser)
        {
            var removed = _erasedThisGesture.Values.ToArray();
            _erasedThisGesture.Clear();
            _lastErasePoint = null;

            if (removed.Length > 0)
            {
                PushUndoAction(Array.Empty<string>(), removed);
                PublishEvents(removed.Select(item => CreateDeleteEvent(item.StrokeId)).ToArray());
            }

            RenderAll();
            e.Handled = true;
            return;
        }

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
                CurrentStrokeColour,
                CurrentStrokeThickness,
                points,
                DateTimeOffset.UtcNow);

            _strokes[stroke.StrokeId] = stroke;
            PushUndoAction(new[] { stroke.StrokeId }, Array.Empty<AgentWhiteboardEvent>());
            PublishEvents(new[] { stroke });
        }

        _activePolyline = null;
        _activePoints.Clear();
        RenderAll();
        e.Handled = true;
    }

    private bool EraseAt(Point point)
    {
        var changed = false;
        foreach (var pair in _strokes.ToArray())
        {
            if (!StrokeHitTest(pair.Value, point)) continue;

            if (!_erasedThisGesture.ContainsKey(pair.Key))
                _erasedThisGesture[pair.Key] = pair.Value;
            _strokes.Remove(pair.Key);
            changed = true;
        }

        return changed;
    }

    private bool StrokeHitTest(AgentWhiteboardEvent stroke, Point point)
    {
        if (stroke.Points.Count < 2 || DrawingCanvas.ActualWidth <= 0 || DrawingCanvas.ActualHeight <= 0)
            return false;

        var radius = Math.Max(8d, (stroke.Thickness / 2d) + 6d);
        var radiusSquared = radius * radius;
        var previous = ToCanvasPoint(stroke.Points[0]);

        for (var index = 1; index < stroke.Points.Count; index++)
        {
            var current = ToCanvasPoint(stroke.Points[index]);
            if (DistanceSquaredToSegment(point, previous, current) <= radiusSquared)
                return true;
            previous = current;
        }

        return false;
    }

    private Point ToCanvasPoint(AgentWhiteboardPoint point) =>
        new(point.X * DrawingCanvas.ActualWidth, point.Y * DrawingCanvas.ActualHeight);

    private static double DistanceSquaredToSegment(Point point, Point start, Point end)
    {
        var segment = end - start;
        var lengthSquared = segment.LengthSquared;
        if (lengthSquared < 0.0001)
            return (point - start).LengthSquared;

        var fromStart = point - start;
        var t = Math.Clamp(Vector.Multiply(fromStart, segment) / lengthSquared, 0d, 1d);
        var closest = start + (segment * t);
        return (point - closest).LengthSquared;
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        var removed = _strokes.Values.ToArray();
        if (removed.Length > 0)
            PushUndoAction(Array.Empty<string>(), removed);

        _strokes.Clear();
        RenderAll();
        PublishEvents(new[] { CreateClearEvent() });
    }

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (_undoHistory.Count == 0) return;

        var action = _undoHistory[^1];
        _undoHistory.RemoveAt(_undoHistory.Count - 1);
        var outgoing = new List<AgentWhiteboardEvent>();

        foreach (var strokeId in action.AddedStrokeIds)
        {
            if (_strokes.Remove(strokeId))
                outgoing.Add(CreateDeleteEvent(strokeId));
        }

        foreach (var removed in action.RemovedStrokes)
        {
            if (_strokes.ContainsKey(removed.StrokeId)) continue;
            _strokes[removed.StrokeId] = removed;
            outgoing.Add(CreateRepublishedStroke(removed));
        }

        RenderAll();
        UpdateUndoButton();
        if (outgoing.Count > 0)
            PublishEvents(outgoing);
        e.Handled = true;
    }

    private void PushUndoAction(
        IReadOnlyList<string> addedStrokeIds,
        IReadOnlyList<AgentWhiteboardEvent> removedStrokes)
    {
        if (addedStrokeIds.Count == 0 && removedStrokes.Count == 0) return;

        _undoHistory.Add(new WhiteboardUndoAction(addedStrokeIds.ToArray(), removedStrokes.ToArray()));
        if (_undoHistory.Count > MaxUndoActions)
            _undoHistory.RemoveAt(0);
        UpdateUndoButton();
    }

    private void UpdateUndoButton()
    {
        if (_undoButton is not null)
            _undoButton.IsEnabled = _undoHistory.Count > 0;
    }

    private AgentWhiteboardEvent CreateDeleteEvent(string strokeId) =>
        new(
            0,
            _settings.ControllerId,
            _settings.GrevName,
            "delete",
            strokeId,
            CurrentStrokeColour,
            CurrentStrokeThickness,
            Array.Empty<AgentWhiteboardPoint>(),
            DateTimeOffset.UtcNow);

    private AgentWhiteboardEvent CreateClearEvent() =>
        new(
            0,
            _settings.ControllerId,
            _settings.GrevName,
            "clear",
            $"clear-{Guid.NewGuid():N}",
            CurrentStrokeColour,
            CurrentStrokeThickness,
            Array.Empty<AgentWhiteboardPoint>(),
            DateTimeOffset.UtcNow);

    private AgentWhiteboardEvent CreateRepublishedStroke(AgentWhiteboardEvent source) =>
        new(
            0,
            _settings.ControllerId,
            _settings.GrevName,
            "stroke",
            source.StrokeId,
            source.Color,
            source.Thickness,
            source.Points,
            DateTimeOffset.UtcNow);

    private void PublishEvents(IReadOnlyList<AgentWhiteboardEvent> events)
    {
        if (events.Count > 0)
            WhiteboardEventsCreated?.Invoke(events);
    }

    private void Done_Click(object sender, RoutedEventArgs e) => Close();

    private void RenderAll()
    {
        if (DrawingCanvas.ActualWidth <= 0 || DrawingCanvas.ActualHeight <= 0) return;

        DrawingCanvas.Children.Clear();
        foreach (var stroke in _strokes.Values
                     .Where(item => string.Equals(item.Kind, "stroke", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(item => item.EventId)
                     .ThenBy(item => item.CreatedAtUtc))
        {
            if (stroke.Points.Count < 2) continue;

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

    private bool IsHighlighter => string.Equals(_selectedTool, "highlighter", StringComparison.OrdinalIgnoreCase);
    private bool IsEraser => string.Equals(_selectedTool, "eraser", StringComparison.OrdinalIgnoreCase);

    private double CurrentStrokeThickness => IsHighlighter ? HighlighterThickness : PenThickness;

    private string CurrentStrokeColour => IsHighlighter ? WithAlpha(_selectedColour, HighlighterAlpha) : _selectedColour;

    private static string WithAlpha(string colour, byte alpha)
    {
        var rgb = colour.Trim();
        if (rgb.StartsWith('#')) rgb = rgb[1..];
        if (rgb.Length == 8) rgb = rgb[2..];
        return rgb.Length == 6 ? $"#{alpha:X2}{rgb.ToUpperInvariant()}" : colour;
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

    private sealed record WhiteboardUndoAction(
        IReadOnlyList<string> AddedStrokeIds,
        IReadOnlyList<AgentWhiteboardEvent> RemovedStrokes);

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
