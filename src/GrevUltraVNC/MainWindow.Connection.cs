using System.Windows;
using GrevUltraVNC.Models;

namespace GrevUltraVNC;

public partial class MainWindow
{
    private async void ConnectMachine(Machine machine)
    {
        try
        {
            var originalConnectId = machine.ConnectId;
            await _connectResolver.ResolveAsync(machine);
            if (!string.Equals(originalConnectId, machine.ConnectId, StringComparison.Ordinal))
                await _storage.SaveMachinesAsync(Machines);

            if (string.IsNullOrWhiteSpace(machine.ActiveAddress))
                throw new InvalidOperationException($"{machine.ConnectId} could not be found on the current LAN or Grev Connect networks.");

            var alreadyConnected = _vnc.HasActiveSession(machine.Id);
            _vnc.Launch(machine, _settings);

            if (!alreadyConnected)
                _ = RecordActivityAsync(machine, "VNC", "Connect", $"Connected through {machine.ResolvedRoute} · {machine.ActiveAddress}:{machine.VncPort}", true);

            if (_controlPanels.TryGetValue(machine.Id, out var existing))
            {
                if (!existing.IsVisible)
                    existing.Show();

                existing.Activate();
                return;
            }

            var controlPanel = new GrevControlPanelWindow(machine, _vnc, _settings);
            _controlPanels[machine.Id] = controlPanel;
            controlPanel.Closed += (_, _) => _controlPanels.Remove(machine.Id);
            controlPanel.CollaborationSettingsChanged += async (_, _) =>
            {
                try
                {
                    await _storage.SaveSettingsAsync(_settings);
                }
                catch
                {
                    // A live cursor choice should never interrupt the remote-control session.
                }
            };
            controlPanel.Show();
        }
        catch (Exception ex)
        {
            _ = RecordActivityAsync(machine, "VNC", "Connect", ex.Message, false);
            MessageBox.Show(this, ex.Message, "Could not open VNC", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task RecordActivityAsync(Machine machine, string category, string action, string detail, bool success)
    {
        try
        {
            await _workspace.AppendActivityAsync(new ActivityEntry
            {
                MachineId = machine.Id,
                MachineName = machine.Name,
                TimestampUtc = DateTime.UtcNow,
                Category = category,
                Action = action,
                Detail = detail,
                Success = success
            });
        }
        catch
        {
            // History must never prevent a connection or dashboard action.
        }
    }
}
