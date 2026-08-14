using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GrevUltraVNC.Contracts;
using GrevUltraVNC.Models;
using GrevUltraVNC.Services;

namespace GrevUltraVNC;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly UltraVncSessionService _vnc;
    private readonly string _originalTheme;
    private bool _initializing = true;
    private string _selectedCollaborationColor = CollaborationColors.Default;

    public SettingsWindow(AppSettings settings, UltraVncSessionService vnc)
    {
        InitializeComponent();
        _settings = settings;
        _vnc = vnc;
        _originalTheme = ThemeService.Normalize(settings.Theme);
        _selectedCollaborationColor = CollaborationColors.Normalize(settings.CollaborationColor);

        GrevNameBox.Text = settings.GrevName;
        ControllerIdText.Text = string.IsNullOrWhiteSpace(settings.ControllerId)
            ? "A private controller identity will be created when you save."
            : $"Private controller ID · {settings.ControllerId[..Math.Min(12, settings.ControllerId.Length)]}";
        ViewerPathBox.Text = settings.UltraVncViewerPath;
        AutoScaleCheck.IsChecked = settings.AutoScaling;
        FullScreenCheck.IsChecked = settings.FullScreenByDefault;
        IntervalBox.Text = settings.StatusCheckSeconds.ToString();
        StartWithWindowsCheck.IsChecked = settings.StartWithWindows;
        MinimizeToTrayCheck.IsChecked = settings.MinimizeToTray;

        var selectedColourButton = CollaborationColourPalette.Children
            .OfType<Button>()
            .FirstOrDefault(button => string.Equals(
                button.Tag?.ToString(),
                _selectedCollaborationColor,
                StringComparison.OrdinalIgnoreCase))
            ?? DefaultCollaborationColourButton;
        UpdateCollaborationColourSelection(selectedColourButton);

        if (_originalTheme == ThemeService.Light)
            LightThemeRadio.IsChecked = true;
        else
            DarkThemeRadio.IsChecked = true;

        _initializing = false;
        Closed += SettingsWindow_Closed;
    }

    private void CollaborationColour_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string colour)
            return;

        _selectedCollaborationColor = CollaborationColors.Normalize(colour);
        UpdateCollaborationColourSelection(button);
        e.Handled = true;
    }

    private void UpdateCollaborationColourSelection(Button selectedButton)
    {
        foreach (var swatch in CollaborationColourPalette.Children.OfType<Button>())
        {
            swatch.ClearValue(Control.BorderBrushProperty);
            swatch.ClearValue(Control.BorderThicknessProperty);
        }

        selectedButton.BorderBrush = Brushes.White;
        selectedButton.BorderThickness = new Thickness(3);
        SelectedCollaborationColourText.Text = selectedButton.ToolTip?.ToString() ?? _selectedCollaborationColor;
    }

    private void ThemeChoice_Checked(object sender, RoutedEventArgs e)
    {
        if (_initializing) return;
        ThemeService.Apply(LightThemeRadio.IsChecked == true ? ThemeService.Light : ThemeService.Dark);
    }

    private void SettingsWindow_Closed(object? sender, EventArgs e)
    {
        if (DialogResult != true)
            ThemeService.Apply(_originalTheme);
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

        var grevName = GrevNameBox.Text.Trim();
        if (grevName.Length is < 1 or > 40)
        {
            MessageBox.Show(this, "Grev Name must be between 1 and 40 characters.", "GrevUltraVNC",
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

        _settings.GrevName = grevName;
        if (string.IsNullOrWhiteSpace(_settings.ControllerId))
            _settings.ControllerId = Guid.NewGuid().ToString("N");
        _settings.CollaborationColor = CollaborationColors.Normalize(_selectedCollaborationColor);
        _settings.UltraVncViewerPath = path;
        _settings.AutoScaling = AutoScaleCheck.IsChecked == true;
        _settings.FullScreenByDefault = FullScreenCheck.IsChecked == true;
        _settings.StatusCheckSeconds = seconds;
        _settings.Theme = LightThemeRadio.IsChecked == true ? ThemeService.Light : ThemeService.Dark;
        _settings.StartWithWindows = StartWithWindowsCheck.IsChecked == true;
        _settings.MinimizeToTray = MinimizeToTrayCheck.IsChecked == true;
        DialogResult = true;
    }
}
