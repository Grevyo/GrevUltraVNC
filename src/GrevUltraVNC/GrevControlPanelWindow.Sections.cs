using System.Windows;
using System.Windows.Controls;

namespace GrevUltraVNC;

public partial class GrevControlPanelWindow
{
    private void TogglePanelSection_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string section)
            return;

        var (body, label) = section switch
        {
            "viewer" => ((FrameworkElement)ViewerSectionBody, "VIEWER"),
            "keys" => (RemoteKeysPanel, "REMOTE KEYS"),
            "pc" => ((FrameworkElement)PcSectionBody, "PC"),
            _ => (null, string.Empty)
        };

        if (body is null)
            return;

        var collapse = body.Visibility == Visibility.Visible;
        body.Visibility = collapse ? Visibility.Collapsed : Visibility.Visible;
        button.Content = $"{label}  {(collapse ? "▸" : "▾")}";
    }
}
