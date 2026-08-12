using System.Windows;
using System.Windows.Media;

namespace GrevUltraVNC.Services;

public static class ThemeService
{
    public const string Dark = "Dark";
    public const string Light = "Light";

    private static readonly IReadOnlyDictionary<string, string> DarkPalette = new Dictionary<string, string>
    {
        ["WindowBrush"] = "#090A10",
        ["PanelBrush"] = "#12131C",
        ["PanelHoverBrush"] = "#1B1C2B",
        ["SubtlePanelBrush"] = "#0D0E16",
        ["BorderBrush"] = "#2C2E40",
        ["TextBrush"] = "#F5F4FB",
        ["MutedTextBrush"] = "#AAA9BC",
        ["FaintTextBrush"] = "#6F7085",
        ["AccentBrush"] = "#6678E8",
        ["Accent2Brush"] = "#945EFF",
        ["AccentSoftBrush"] = "#251E3F",
        ["DangerBrush"] = "#FF6B7C",
        ["PrimaryButtonTextBrush"] = "#FFFFFF",
        ["SecondaryButtonBrush"] = "#1A1C29",
        ["SecondaryButtonTextBrush"] = "#F4F3FA",
        ["DangerButtonBrush"] = "#3A1E2A",
        ["DangerButtonTextBrush"] = "#FFA5B0",
        ["TextBoxBrush"] = "#0E0F18"
    };

    private static readonly IReadOnlyDictionary<string, string> LightPalette = new Dictionary<string, string>
    {
        ["WindowBrush"] = "#F5F4FA",
        ["PanelBrush"] = "#FFFFFF",
        ["PanelHoverBrush"] = "#F0EDFA",
        ["SubtlePanelBrush"] = "#F0EEF7",
        ["BorderBrush"] = "#D8D4E5",
        ["TextBrush"] = "#1C1B25",
        ["MutedTextBrush"] = "#6F6B7C",
        ["FaintTextBrush"] = "#9590A3",
        ["AccentBrush"] = "#5369D4",
        ["Accent2Brush"] = "#7B4FD0",
        ["AccentSoftBrush"] = "#ECE6FA",
        ["DangerBrush"] = "#D84C61",
        ["PrimaryButtonTextBrush"] = "#FFFFFF",
        ["SecondaryButtonBrush"] = "#EAE7F2",
        ["SecondaryButtonTextBrush"] = "#1C1B25",
        ["DangerButtonBrush"] = "#FDE8ED",
        ["DangerButtonTextBrush"] = "#B4233A",
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
