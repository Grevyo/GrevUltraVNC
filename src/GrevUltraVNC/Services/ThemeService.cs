using System.Windows;
using System.Windows.Media;

namespace GrevUltraVNC.Services;

public static class ThemeService
{
    public const string Dark = "Dark";
    public const string Light = "Light";

    private static readonly IReadOnlyDictionary<string, string> DarkPalette = new Dictionary<string, string>
    {
        ["WindowBrush"] = "#0F1115",
        ["PanelBrush"] = "#171A21",
        ["PanelHoverBrush"] = "#20242D",
        ["SubtlePanelBrush"] = "#12151B",
        ["BorderBrush"] = "#2A2F3A",
        ["TextBrush"] = "#F4F6F8",
        ["MutedTextBrush"] = "#9BA5B4",
        ["FaintTextBrush"] = "#626C7A",
        ["AccentBrush"] = "#6EA8FE",
        ["DangerBrush"] = "#FF6B6B",
        ["PrimaryButtonTextBrush"] = "#101216",
        ["SecondaryButtonBrush"] = "#252A35",
        ["SecondaryButtonTextBrush"] = "#F4F6F8",
        ["DangerButtonBrush"] = "#3B2024",
        ["DangerButtonTextBrush"] = "#FF9C9C",
        ["TextBoxBrush"] = "#101318"
    };

    private static readonly IReadOnlyDictionary<string, string> LightPalette = new Dictionary<string, string>
    {
        ["WindowBrush"] = "#F5F7FB",
        ["PanelBrush"] = "#FFFFFF",
        ["PanelHoverBrush"] = "#EDF2F7",
        ["SubtlePanelBrush"] = "#EEF2F7",
        ["BorderBrush"] = "#D5DCE6",
        ["TextBrush"] = "#18202A",
        ["MutedTextBrush"] = "#657181",
        ["FaintTextBrush"] = "#8490A0",
        ["AccentBrush"] = "#3B82F6",
        ["DangerBrush"] = "#D84C4C",
        ["PrimaryButtonTextBrush"] = "#FFFFFF",
        ["SecondaryButtonBrush"] = "#E7ECF3",
        ["SecondaryButtonTextBrush"] = "#18202A",
        ["DangerButtonBrush"] = "#FDE8E8",
        ["DangerButtonTextBrush"] = "#B42318",
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
