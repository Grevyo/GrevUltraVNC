using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GrevUltraVNC.Contracts;
using GrevUltraVNC.Services;

namespace GrevUltraVNC;

public sealed class CursorStyleSelector : UserControl
{
    private readonly Dictionary<string, Button> _buttons = new(StringComparer.OrdinalIgnoreCase);
    private readonly WrapPanel _panel;
    private string _selectedStyle;
    private string _color;

    public event Action<string>? SelectedStyleChanged;

    public CursorStyleSelector(string selectedStyle, string color, bool compact = false)
    {
        _selectedStyle = CursorStyleCatalog.Normalize(selectedStyle);
        _color = CollaborationColors.Normalize(color);

        _panel = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(-3, -3, -3, -3)
        };

        Content = _panel;
        BuildButtons(compact);
        RefreshSelection();
    }

    public string SelectedStyle => _selectedStyle;

    public void SetSelectedStyle(string style, bool raiseEvent = false)
    {
        var normalized = CursorStyleCatalog.Normalize(style);
        if (string.Equals(_selectedStyle, normalized, StringComparison.OrdinalIgnoreCase))
        {
            RefreshSelection();
            return;
        }

        _selectedStyle = normalized;
        RefreshSelection();
        if (raiseEvent)
            SelectedStyleChanged?.Invoke(_selectedStyle);
    }

    public void SetColor(string color)
    {
        _color = CollaborationColors.Normalize(color);
        RebuildPreviews();
        RefreshSelection();
    }

    private void BuildButtons(bool compact)
    {
        _buttons.Clear();
        _panel.Children.Clear();

        foreach (var option in CursorStyleCatalog.Options)
        {
            var button = new Button
            {
                Tag = option.Id,
                Width = compact ? 112 : 132,
                Height = compact ? 58 : 68,
                Margin = new Thickness(3),
                Padding = new Thickness(compact ? 6 : 8, 5, compact ? 6 : 8, 5),
                ToolTip = $"Use {option.Name} for your collaboration cursor"
            };
            button.SetResourceReference(StyleProperty, "SecondaryButton");
            button.Click += CursorButton_Click;
            button.Content = BuildButtonContent(option, compact);
            _buttons[option.Id] = button;
            _panel.Children.Add(button);
        }
    }

    private FrameworkElement BuildButtonContent(CursorStyleOption option, bool compact)
    {
        var brush = CreateCursorBrush();
        var stack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };

        stack.Children.Add(CursorVisualFactory.CreatePreview(option.Id, brush, compact ? 27 : 34));

        var name = new TextBlock
        {
            Text = option.Name,
            Margin = new Thickness(7, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = compact ? 8.5 : 10,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = compact ? 68 : 82
        };
        name.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        stack.Children.Add(name);
        return stack;
    }

    private void RebuildPreviews()
    {
        foreach (var option in CursorStyleCatalog.Options)
        {
            if (!_buttons.TryGetValue(option.Id, out var button)) continue;
            var compact = button.Width <= 115;
            button.Content = BuildButtonContent(option, compact);
        }
    }

    private Brush CreateCursorBrush()
    {
        try
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_color)!);
            brush.Freeze();
            return brush;
        }
        catch
        {
            var fallback = new SolidColorBrush((Color)ColorConverter.ConvertFromString(CollaborationColors.Default)!);
            fallback.Freeze();
            return fallback;
        }
    }

    private void CursorButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string style)
            return;

        SetSelectedStyle(style);
        SelectedStyleChanged?.Invoke(_selectedStyle);
        e.Handled = true;
    }

    private void RefreshSelection()
    {
        foreach (var pair in _buttons)
        {
            var selected = string.Equals(pair.Key, _selectedStyle, StringComparison.OrdinalIgnoreCase);
            pair.Value.BorderThickness = selected ? new Thickness(2) : new Thickness(1);
            pair.Value.SetResourceReference(
                Button.BorderBrushProperty,
                selected ? "AccentBrush" : "BorderBrush");
            pair.Value.SetResourceReference(
                Button.BackgroundProperty,
                selected ? "AccentSoftBrush" : "SecondaryButtonBrush");
        }
    }
}
