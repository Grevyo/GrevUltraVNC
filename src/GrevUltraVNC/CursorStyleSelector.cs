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
    private readonly bool _compact;
    private string _selectedStyle;
    private string _color;

    public event Action<string>? SelectedStyleChanged;

    public CursorStyleSelector(string selectedStyle, string color, bool compact = false)
    {
        _selectedStyle = CursorStyleCatalog.Normalize(selectedStyle);
        _color = CollaborationColors.Normalize(color);
        _compact = compact;

        _panel = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(-2, -2, -2, -2)
        };

        Content = _panel;
        BuildButtons();
        RefreshSelection();

        if (_compact)
            Loaded += (_, _) => HideCompactCurrentName();
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

    private void BuildButtons()
    {
        _buttons.Clear();
        _panel.Children.Clear();

        foreach (var option in CursorStyleCatalog.Options)
        {
            var button = new Button
            {
                Tag = option.Id,
                Width = _compact ? 34 : 104,
                Height = _compact ? 34 : 48,
                Margin = new Thickness(_compact ? 2 : 3),
                Padding = new Thickness(_compact ? 3 : 6),
                ToolTip = option.Name
            };
            button.SetResourceReference(StyleProperty, "SecondaryButton");
            button.Click += CursorButton_Click;
            button.Content = BuildButtonContent(option);
            _buttons[option.Id] = button;
            _panel.Children.Add(button);
        }
    }

    private FrameworkElement BuildButtonContent(CursorStyleOption option)
    {
        var brush = CreateCursorBrush();
        if (_compact)
            return CursorVisualFactory.CreatePreview(option.Id, brush, 18);

        var stack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        stack.Children.Add(CursorVisualFactory.CreatePreview(option.Id, brush, 23));

        var name = new TextBlock
        {
            Text = option.Name,
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 9,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 62
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
            button.Content = BuildButtonContent(option);
        }
    }

    private void HideCompactCurrentName()
    {
        // Grev Control is intentionally picture-only: the cursor names remain available as
        // tooltips, but the visible selector contains nothing except the small pointer icons.
        if (Parent is not StackPanel host) return;

        foreach (var grid in host.Children.OfType<Grid>())
        {
            var currentName = grid.Children
                .OfType<TextBlock>()
                .FirstOrDefault(item => string.Equals(
                    item.Tag?.ToString(),
                    "cursor-current-label",
                    StringComparison.Ordinal));
            if (currentName is null) continue;
            currentName.Visibility = Visibility.Collapsed;
            break;
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
