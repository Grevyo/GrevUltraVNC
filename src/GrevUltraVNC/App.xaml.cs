using GrevUltraVNC.Services;

namespace GrevUltraVNC;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        // Apply the restrained Grev dark palette before StartupUri creates the main window.
        ThemeService.Apply(ThemeService.Dark);
        base.OnStartup(e);
    }
}
