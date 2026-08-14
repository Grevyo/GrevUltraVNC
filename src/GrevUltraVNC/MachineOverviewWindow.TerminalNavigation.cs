namespace GrevUltraVNC;

public partial class MachineOverviewWindow
{
    public void ShowTerminal()
    {
        ShowSection(TerminalPanel, TerminalButton);
        TerminalCommandBox.Focus();
    }
}
