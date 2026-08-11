using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using GrevUltraVNC.Models;

namespace GrevUltraVNC.Converters;

public sealed class AgentStateBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        GrevAgentState.Connected => new SolidColorBrush(Color.FromRgb(80, 220, 145)),
        GrevAgentState.ReadyToPair => new SolidColorBrush(Color.FromRgb(85, 118, 216)),
        GrevAgentState.AuthenticationFailed => new SolidColorBrush(Color.FromRgb(255, 107, 119)),
        GrevAgentState.Error => new SolidColorBrush(Color.FromRgb(255, 170, 92)),
        _ => new SolidColorBrush(Color.FromRgb(98, 111, 130))
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
