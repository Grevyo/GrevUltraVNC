using System.Windows;

namespace GrevUltraVNC;

public partial class MachineOverviewWindow
{
    private void Files_Click(object sender, RoutedEventArgs e)
    {
        var files = new RemoteFileManagerWindow(_machine) { Owner = this };
        files.ShowDialog();
    }
}
