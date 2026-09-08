using System.Globalization;
using System.Windows.Data;
using GrevUltraVNC.Models;
using GrevUltraVNC.Services;

namespace GrevUltraVNC.Converters;

/// <summary>Grev Agent pairing state to a themed status colour.</summary>
public sealed class AgentStateBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        ThemeService.ThemeBrush(value switch
        {
            GrevAgentState.Connected => "OkBrush",
            GrevAgentState.ReadyToPair => "InfoBrush",
            GrevAgentState.AuthenticationFailed => "DangerBrush",
            GrevAgentState.Error => "WarnBrush",
            _ => "IdleBrush"
        });

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
