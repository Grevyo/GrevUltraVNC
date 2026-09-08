using System.Globalization;
using System.Windows.Data;
using GrevUltraVNC.Models;
using GrevUltraVNC.Services;

namespace GrevUltraVNC.Converters;

/// <summary>Machine reachability to a themed status colour.</summary>
public sealed class MachineStatusBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        ThemeService.ThemeBrush(value switch
        {
            MachineStatus.Online => "OkBrush",
            MachineStatus.VncUnavailable => "WarnBrush",
            MachineStatus.Offline => "DangerBrush",
            _ => "IdleBrush"
        });

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>The same status, as the soft background used behind a status pill.</summary>
public sealed class MachineStatusSoftBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        ThemeService.ThemeBrush(value switch
        {
            MachineStatus.Online => "OkSoftBrush",
            MachineStatus.VncUnavailable => "WarnSoftBrush",
            MachineStatus.Offline => "DangerSoftBrush",
            _ => "SubtlePanelBrush"
        });

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Usage percentage to a pressure colour: comfortable, busy, or saturated.
/// Used by every CPU, memory and disk meter so the thresholds stay identical.
/// </summary>
public sealed class UsageBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var percent = value switch
        {
            double d => d,
            int i => i,
            long l => l,
            _ => 0d
        };

        return ThemeService.ThemeBrush(percent switch
        {
            >= 90 => "DangerBrush",
            >= 75 => "WarnBrush",
            _ => "AccentBrush"
        });
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Collapses an element when its bound string is null, empty or whitespace.</summary>
public sealed class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        string.IsNullOrWhiteSpace(value as string)
            ? System.Windows.Visibility.Collapsed
            : System.Windows.Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Collapses an element when its bound boolean is false.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var visible = value is true;
        if (parameter is string text && text.Equals("invert", StringComparison.OrdinalIgnoreCase))
            visible = !visible;

        return visible ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
