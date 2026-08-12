using System.Windows;
using System.Windows.Media;

namespace GrevUltraVNC.Services;

public static class ThemeService
{
    public const string Dark = "Dark";
    public const string Light = "Light";

    // Tuned against the supplied Grev logo: electric cyan/blue is the primary
    // identity, with a deeper blue-violet used as the secondary accent.
    private static readonly IReadOnlyDictionary<string, string> DarkPalette = new Dictionary<string, string>
    {
        ["WindowBrush"] = "#060912",
        ["PanelBrush"] = "#0D1220",
        ["PanelHoverBrush"] = "#121B2D",
        ["SubtlePanelBrush"] = "#080D18",
        ["BorderBrush"] = "#24324A",
        ["TextBrush"] = "#F2F7FF",
        ["MutedTextBrush"] = "#A3B2C8",
        ["FaintTextBrush"] = "#60708B",
        ["AccentBrush"] = "#32CFF0",
        ["Accent2Brush"] = "#5155D6",
        ["AccentSoftBrush"] = "#11183B",
        ["DangerBrush"] = "#FF6178",
        ["PrimaryButtonTextBrush"] = "#FFFFFF",
        ["SecondaryButtonBrush"] = "#111827",
        ["SecondaryButtonTextBrush"] = "#F0F5FF",
        ["DangerButtonBrush"] = "#321722",
        ["DangerButtonTextBrush"] = "#FFA0AE",
        ["TextBoxBrush"] = "#080E19"
    };

    private static readonly IReadOnlyDictionary<string, string> LightPalette = new Dictionary<string, string>
    {
        ["WindowBrush"] = "#F3F7FC",
        ["PanelBrush"] = "#FFFFFF",
        ["PanelHoverBrush"] = "#EAF4FC",
        ["SubtlePanelBrush"] = "#EEF4FA",
        ["BorderBrush"] = "#CBD8E7",
        ["TextBrush"] = "#111827",
        ["MutedTextBrush"] = "#5D6D82",
        ["FaintTextBrush"] = "#8796A9",
        ["AccentBrush"] = "#168CC7",
        ["Accent2Brush"] = "#5054C7",
        ["AccentSoftBrush"] = "#E7EDFF",
        ["DangerBrush"] = "#D84C61",
        ["PrimaryButtonTextBrush"] = "#FFFFFF",
        ["SecondaryButtonBrush"] = "#E8EFF7",
        ["SecondaryButtonTextBrush"] = "#172033",
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

        // BrandGradientBrush is defined in App.xaml and intentionally left alone here.
        // WPF may freeze StaticResource Freezables; mutating that shared gradient during
        // startup can be fragile. The logo-matched dark gradient remains the stable
        // primary-button treatment while the solid theme resources switch dynamically.
    }
}
