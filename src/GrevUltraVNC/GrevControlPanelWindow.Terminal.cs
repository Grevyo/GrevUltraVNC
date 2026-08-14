using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace GrevUltraVNC;

public partial class GrevControlPanelWindow
{
    private Button? _terminalToolButton;

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        EnsureTerminalToolButton();
    }

    private void EnsureTerminalToolButton()
    {
        if (_terminalToolButton is not null || WhiteboardButton.Parent is not UniformGrid toolsGrid)
            return;

        toolsGrid.Columns = 7;

        var button = new Button
        {
            Content = ">_",
            ToolTip = "Open Manage Terminal"
        };
        button.SetResourceReference(FrameworkElement.StyleProperty, "ToolIconButton");
        button.Click += TerminalTool_Click;

        var insertIndex = toolsGrid.Children.IndexOf(AudioButton);
        if (insertIndex < 0)
            insertIndex = toolsGrid.Children.Count;

        toolsGrid.Children.Insert(insertIndex, button);
        _terminalToolButton = button;
    }

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
