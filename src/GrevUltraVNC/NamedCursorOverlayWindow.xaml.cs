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
    private static readonly string[] CursorPalette =
    [
        "#32CFF0",
        "#8C7CFF",
        "#50DC91",
        "#FFB84D",
        "#FF6B8A",
        "#5EA8FF"
    ];

    private readonly Machine _machine;
    private readonly UltraVncSessionService _vnc;
    private readonly bool _virtualDisplay;
    private readonly DispatcherTimer _dockTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private IReadOnlyList<AgentPresenceInfo> _participants = Array.Empty<AgentPresenceInfo>();
    private string _localControllerId = string.Empty;

    public NamedCursorOverlayWindow(Machine machine, UltraVncSessionService vnc, bool virtualDisplay)
    {
        InitializeComponent();
        _machine = machine;
        _vnc = vnc;
        _virtualDisplay = virtualDisplay;

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
    }

    private void NamedCursorOverlayWindow_Closed(object? sender, EventArgs e) => _dockTimer.Stop();

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
        var expectedSurface = _virtualDisplay ? "screen2" : "screen1";

        foreach (var participant in _participants)
        {
            if (!participant.CursorVisible || participant.CursorX is null || participant.CursorY is null ||
                !string.Equals(participant.CursorSurface, expectedSurface, StringComparison.OrdinalIgnoreCase))
                continue;

            var x = Math.Clamp(participant.CursorX.Value, 0, 1) * CursorCanvas.ActualWidth;
            var y = Math.Clamp(participant.CursorY.Value, 0, 1) * CursorCanvas.ActualHeight;
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(GetCursorColor(participant.ControllerId))!);
            brush.Freeze();

            var isLocal = string.Equals(participant.ControllerId, _localControllerId, StringComparison.OrdinalIgnoreCase);
            var label = participant.HasControl
                ? $"{participant.DisplayName} · CONTROL"
                : participant.DisplayName;

            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            panel.Children.Add(new TextBlock
            {
                Text = "↖",
                Foreground = brush,
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, -5, 2, 0)
            });
            panel.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(225, 7, 12, 22)),
                BorderBrush = brush,
                BorderThickness = new Thickness(isLocal ? 1.4 : 1),
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(7, 3, 7, 3),
                Child = new TextBlock
                {
                    Text = label,
                    Foreground = Brushes.White,
                    FontSize = 11,
                    FontWeight = participant.HasControl ? FontWeights.SemiBold : FontWeights.Normal
                }
            });

            CursorCanvas.Children.Add(panel);
            panel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var desired = panel.DesiredSize;
            Canvas.SetLeft(panel, Math.Clamp(x, 0, Math.Max(0, CursorCanvas.ActualWidth - desired.Width)));
            Canvas.SetTop(panel, Math.Clamp(y, 0, Math.Max(0, CursorCanvas.ActualHeight - desired.Height)));
        }
    }

    private static string GetCursorColor(string controllerId)
    {
        unchecked
        {
            var hash = 17;
            foreach (var character in controllerId)
                hash = hash * 31 + char.ToUpperInvariant(character);
            return CursorPalette[(hash & int.MaxValue) % CursorPalette.Length];
        }
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
