using System.Windows;
using GrevUltraVNC.Models;
using GrevUltraVNC.Services;

namespace GrevUltraVNC;

public partial class MainWindow
{
    private async void Settings_Click(object sender, RoutedEventArgs e)
    {
        var working = new AppSettings
        {
            UltraVncViewerPath = _settings.UltraVncViewerPath,
            AutoScaling = _settings.AutoScaling,
            FullScreenByDefault = _settings.FullScreenByDefault,
            StatusCheckSeconds = _settings.StatusCheckSeconds,
            Theme = _settings.Theme,
            StartWithWindows = _settings.StartWithWindows,
            MinimizeToTray = _settings.MinimizeToTray,
            GrevName = _settings.GrevName,
            ControllerId = _settings.ControllerId,
            CollaborationColor = _settings.CollaborationColor,
            CursorStyle = _settings.CursorStyle
        };

        var dialog = new SettingsWindow(working, _vnc) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        _settings = working;
        _settings.Theme = ThemeService.Normalize(_settings.Theme);
        _settings.CursorStyle = CursorStyleCatalog.Normalize(_settings.CursorStyle);
        ThemeService.Apply(_settings.Theme);
        await _storage.SaveSettingsAsync(_settings);
        ConfigureStatusTimer();

        foreach (var controlPanel in _controlPanels.Values.ToArray())
            controlPanel.UpdateCollaborationSettings(_settings);

        try
        {
            StartupService.SetEnabled(_settings.StartWithWindows);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Start with Windows", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
