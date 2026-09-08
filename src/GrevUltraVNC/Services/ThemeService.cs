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
        ["TextBoxBrush"] = "#080E19",
        ["StrongBorderBrush"] = "#33456A",
        ["ElevatedPanelBrush"] = "#111A2C",
        ["OkBrush"] = "#4ADE93",
        ["OkSoftBrush"] = "#0E2A1F",
        ["WarnBrush"] = "#FFBE5C",
        ["WarnSoftBrush"] = "#2C2110",
        ["DangerSoftBrush"] = "#2C1119",
        ["InfoBrush"] = "#7C8CF0",
        ["IdleBrush"] = "#62707F",
        ["TrackBrush"] = "#131C2E",
        ["ScrollThumbBrush"] = "#2A3A57",
        ["ScrollThumbHoverBrush"] = "#3D5480",
        ["ConsoleBrush"] = "#04060B",
        ["LogoPlateBrush"] = "#030509"
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
        ["TextBoxBrush"] = "#FFFFFF",
        ["StrongBorderBrush"] = "#A9BDD4",
        ["ElevatedPanelBrush"] = "#FFFFFF",
        ["OkBrush"] = "#12874F",
        ["OkSoftBrush"] = "#DFF5E9",
        ["WarnBrush"] = "#A96A05",
        ["WarnSoftBrush"] = "#FCEFD8",
        ["DangerSoftBrush"] = "#FCE3E8",
        ["InfoBrush"] = "#4A54C4",
        ["IdleBrush"] = "#7F8DA0",
        ["TrackBrush"] = "#DCE6F1",
        ["ScrollThumbBrush"] = "#B7C7D9",
        ["ScrollThumbHoverBrush"] = "#8FA6C2",
        ["ConsoleBrush"] = "#0B111C",
        ["LogoPlateBrush"] = "#0B111C"
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

    /// <summary>
    /// Resolves a themed brush by resource key, falling back to a neutral grey if the
    /// application resources are not available (design time, or during shutdown).
    /// </summary>
    public static Brush ThemeBrush(string resourceKey)
    {
        if (Application.Current?.TryFindResource(resourceKey) is Brush brush)
            return brush;

        return Brushes.Gray;
    }
}
