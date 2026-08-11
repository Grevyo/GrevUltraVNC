using System.Windows;
using System.Windows.Media;

namespace GrevUltraVNC.Services;

public static class ThemeService
{
    public const string Dark = "Dark";
    public const string Light = "Light";

    private static readonly IReadOnlyDictionary<string, string> DarkPalette = new Dictionary<string, string>
    {
        ["WindowBrush"] = "#080A0F",
        ["PanelBrush"] = "#11151E",
        ["PanelHoverBrush"] = "#171E2C",
        ["SubtlePanelBrush"] = "#0D1119",
        ["BorderBrush"] = "#252D3B",
        ["TextBrush"] = "#F5F8FC",
        ["MutedTextBrush"] = "#9AA7BA",
        ["FaintTextBrush"] = "#626F82",
        ["AccentBrush"] = "#19D8FF",
        ["Accent2Brush"] = "#8B5CFF",
        ["AccentSoftBrush"] = "#153044",
        ["DangerBrush"] = "#FF6B77",
        ["PrimaryButtonTextBrush"] = "#061116",
        ["SecondaryButtonBrush"] = "#1A2230",
        ["SecondaryButtonTextBrush"] = "#F4F7FB",
        ["DangerButtonBrush"] = "#3A1E28",
        ["DangerButtonTextBrush"] = "#FFA2AB",
        ["TextBoxBrush"] = "#0C1119"
    };

    private static readonly IReadOnlyDictionary<string, string> LightPalette = new Dictionary<string, string>
    {
        ["WindowBrush"] = "#F4F7FC",
        ["PanelBrush"] = "#FFFFFF",
        ["PanelHoverBrush"] = "#EDF4FB",
        ["SubtlePanelBrush"] = "#EEF3F9",
        ["BorderBrush"] = "#D4DDE9",
        ["TextBrush"] = "#17202B",
        ["MutedTextBrush"] = "#657387",
        ["FaintTextBrush"] = "#8996A8",
        ["AccentBrush"] = "#00A9E8",
        ["Accent2Brush"] = "#6847E8",
        ["AccentSoftBrush"] = "#DFF6FF",
        ["DangerBrush"] = "#D84C5C",
        ["PrimaryButtonTextBrush"] = "#FFFFFF",
        ["SecondaryButtonBrush"] = "#E7EDF5",
        ["SecondaryButtonTextBrush"] = "#17202B",
        ["DangerButtonBrush"] = "#FDE8EB",
        ["DangerButtonTextBrush"] = "#B42332",
        ["TextBoxBrush"] = "#FFFFFF"
    };

    public static string Normalize(string? theme) =>
        string.Equals(theme, Light, StringComparison.OrdinalIgnoreCase) ? Light : Dark;

    public static void Apply(string? theme)
    {
        if (Application.Current is null) return;

        var palette = Normalize(theme) == Light ? LightPalette : DarkPalette;
        foreach (var (key, value) in palette)
        {
            var color = (Color)ColorConverter.ConvertFromString(value)!;
            Application.Current.Resources[key] = new SolidColorBrush(color);
        }
    }
}
