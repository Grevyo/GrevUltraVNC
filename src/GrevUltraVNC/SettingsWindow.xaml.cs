using Microsoft.Win32;
using System.Windows;
using GrevUltraVNC.Models;
using GrevUltraVNC.Services;

namespace GrevUltraVNC;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly UltraVncSessionService _vnc;

    public SettingsWindow(AppSettings settings, UltraVncSessionService vnc)
    {
        InitializeComponent();
        _settings = settings;
        _vnc = vnc;
        ViewerPathBox.Text = settings.UltraVncViewerPath;
        AutoScaleCheck.IsChecked = settings.AutoScaling;
        FullScreenCheck.IsChecked = settings.FullScreenByDefault;
        IntervalBox.Text = settings.StatusCheckSeconds.ToString();
        ThemeBox.SelectedIndex = ThemeService.Normalize(settings.Theme) == ThemeService.Light ? 1 : 0;
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select UltraVNC Viewer",
            Filter = "UltraVNC Viewer (vncviewer.exe)|vncviewer.exe|Executable files (*.exe)|*.exe"
        };
        if (dialog.ShowDialog(this) == true) ViewerPathBox.Text = dialog.FileName;
    }

    private void Detect_Click(object sender, RoutedEventArgs e)
    {
        var path = _vnc.FindViewer(ViewerPathBox.Text.Trim());
        if (path is null)
        {
            MessageBox.Show(this, "UltraVNC Viewer was not found in the usual install folders.", "GrevUltraVNC",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        ViewerPathBox.Text = path;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(IntervalBox.Text, out var seconds) || seconds is < 3 or > 300)
        {
            MessageBox.Show(this, "Status interval must be between 3 and 300 seconds.", "GrevUltraVNC",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var path = ViewerPathBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(path) && !File.Exists(path))
        {
            MessageBox.Show(this, "That UltraVNC Viewer path does not exist.", "GrevUltraVNC",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _settings.UltraVncViewerPath = path;
        _settings.AutoScaling = AutoScaleCheck.IsChecked == true;
        _settings.FullScreenByDefault = FullScreenCheck.IsChecked == true;
        _settings.StatusCheckSeconds = seconds;
        _settings.Theme = ThemeBox.SelectedIndex == 1 ? ThemeService.Light : ThemeService.Dark;
        DialogResult = true;
    }
}
