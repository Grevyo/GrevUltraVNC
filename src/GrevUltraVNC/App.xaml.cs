using System.Windows;
using GrevUltraVNC.Services;

namespace GrevUltraVNC;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Apply the restrained Grev dark palette before StartupUri creates the main window.
        ThemeService.Apply(ThemeService.Dark);

        // Publicly rebrand every window without renaming the established internal namespaces,
        // settings locations or protocol contracts during the release transition.
        EventManager.RegisterClassHandler(
            typeof(Window),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) =>
            {
                if (sender is Window window)
                    ProductBranding.Apply(window);
            }));

        base.OnStartup(e);
    }
}
