using System.Windows;

namespace GrevUltraVNC;

public partial class GrevControlPanelWindow
{
    private void TerminalTool_Click(object sender, RoutedEventArgs e)
    {
        if (_machineOverview is null)
        {
            var overview = new MachineOverviewWindow(_machine, _vnc) { Owner = this };
            _machineOverview = overview;
            overview.Closed += (_, _) => _machineOverview = null;
            overview.Show();
        }
        else if (!_machineOverview.IsVisible)
        {
            _machineOverview.Show();
        }

        _machineOverview.ShowTerminal();
        _machineOverview.Activate();
    }
}
