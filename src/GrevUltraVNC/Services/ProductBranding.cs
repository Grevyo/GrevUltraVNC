using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GrevUltraVNC.Services;

/// <summary>
/// Keeps the public product name independent from the legacy internal namespaces and
/// project paths. This lets the application ship as GrevConnect without risking the
/// established GrevUltraVNC IPC/settings/contracts during a release rename.
/// </summary>
public static class ProductBranding
{
    public const string ProductName = "GrevConnect";
    public const string Credits = "Powered by UltraVNC · Connected by Zima · Thought up by Grev · Created by ChatGPT · Assisted by Claude";

    private const string LegacyFooterHint = "Double-click a card to connect  ·  right-click for actions  ·  F5 to refresh";

    public static void Apply(Window window)
    {
        window.Title = ReplaceLegacyBrand(window.Title);
        ApplyToVisualTree(window);
    }

    private static void ApplyToVisualTree(DependencyObject parent)
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);

            if (child is TextBlock textBlock)
            {
                if (textBlock.Text == "UltraVNC")
                    textBlock.Text = "Connect";
                else if (textBlock.Text == LegacyFooterHint)
                {
                    // Keep the product credits permanently visible at the bottom of the
                    // main dashboard without tying the public brand to legacy XAML text.
                    textBlock.Text = Credits;
                    textBlock.ToolTip = LegacyFooterHint;
                }
                else
                    textBlock.Text = ReplaceLegacyBrand(textBlock.Text);
            }
            else if (child is ContentControl contentControl && contentControl.Content is string content)
            {
                contentControl.Content = ReplaceLegacyBrand(content);
            }

            if (child is FrameworkElement element && element.ToolTip is string tooltip)
                element.ToolTip = ReplaceLegacyBrand(tooltip);

            ApplyToVisualTree(child);
        }
    }

    private static string ReplaceLegacyBrand(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value ?? string.Empty;

        return value
            .Replace("Grev UltraVNC", ProductName, StringComparison.Ordinal)
            .Replace("GrevUltraVNC", ProductName, StringComparison.Ordinal);
    }
}
