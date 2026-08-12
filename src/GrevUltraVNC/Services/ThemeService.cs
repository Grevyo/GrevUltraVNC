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

        var isLight = Normalize(theme) == Light;
        var palette = isLight ? LightPalette : DarkPalette;
        foreach (var (key, value) in palette)
        {
            var color = (Color)ColorConverter.ConvertFromString(value)!;
            Application.Current.Resources[key] = new SolidColorBrush(color);
        }

        ApplyBrandGradient(isLight);
    }

    private static void ApplyBrandGradient(bool isLight)
    {
        if (Application.Current?.Resources["BrandGradientBrush"] is not LinearGradientBrush gradient)
            return;

        // StaticResource users hold this brush instance, so mutate it in place when
        // possible. Application resources are normally mutable, but clone defensively.
        if (gradient.IsFrozen)
        {
            gradient = gradient.Clone();
            Application.Current.Resources["BrandGradientBrush"] = gradient;
        }

        gradient.GradientStops.Clear();
        if (isLight)
        {
            gradient.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#258FCB")!, 0));
            gradient.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#356FDC")!, 0.55));
            gradient.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#514FC4")!, 1));
        }
        else
        {
            gradient.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#2D8AD7")!, 0));
            gradient.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#3269E2")!, 0.55));
            gradient.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#5147C8")!, 1));
        }
    }
}
