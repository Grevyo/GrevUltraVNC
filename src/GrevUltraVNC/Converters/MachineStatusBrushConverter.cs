using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using GrevUltraVNC.Models;

namespace GrevUltraVNC.Converters;

public sealed class MachineStatusBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        MachineStatus.Online => new SolidColorBrush(Color.FromRgb(80, 220, 145)),
        MachineStatus.VncUnavailable => new SolidColorBrush(Color.FromRgb(255, 190, 92)),
        MachineStatus.Offline => new SolidColorBrush(Color.FromRgb(255, 107, 107)),
        _ => new SolidColorBrush(Color.FromRgb(155, 165, 180))
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
