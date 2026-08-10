using System.Diagnostics;
using System.Windows;
using GrevUltraVNC.Models;
using GrevUltraVNC.Services;

namespace GrevUltraVNC;

public partial class MachineActionWindow : Window
{
    private readonly Machine _machine;
    private readonly AppSettings _settings;
    private readonly UltraVncSessionService _vnc;
    private readonly WakeOnLanService _wake = new();
    private readonly PowerService _power = new();
    private readonly NetworkStatusService _network = new();

    public bool MachineChanged { get; private set; }
    public bool MachineDeleted { get; private set; }

    public MachineActionWindow(Machine machine, AppSettings settings, UltraVncSessionService vnc)
    {
        InitializeComponent();
        _machine = machine;
        _settings = settings;
        _vnc = vnc;
        RefreshHeader();
    }

    private void RefreshHeader()
    {
        MachineNameText.Text = _machine.Name;
        MachineAddressText.Text = $"{_machine.IpAddress}  ·  VNC {_machine.VncPort}";
    }

    private void Vnc_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _vnc.Launch(_machine, _settings);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not open VNC", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Cad_Click(object sender, RoutedEventArgs e) => SendRemoteKey(() => _vnc.SendCtrlAltDelete(_machine.Id));
    private void WindowsKey_Click(object sender, RoutedEventArgs e) => SendRemoteKey(() => _vnc.SendWindowsKey(_machine.Id));
    private void TaskManager_Click(object sender, RoutedEventArgs e) => SendRemoteKey(() => _vnc.SendCtrlShiftEscape(_machine.Id));

    private void SendRemoteKey(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Remote key", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private async void Wake_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _wake.SendAsync(_machine.MacAddress);
            MessageBox.Show(this, "Wake-on-LAN packet sent.", "GrevUltraVNC", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Wake-on-LAN", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void Restart_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, $"Restart {_machine.Name} now?", "Confirm restart", MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        var result = await _power.RestartAsync(_machine.IpAddress);
        MessageBox.Show(this, result.Message, "Restart", MessageBoxButton.OK,
            result.Success ? MessageBoxImage.Information : MessageBoxImage.Error);
    }

    private async void Shutdown_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, $"Shut down {_machine.Name} now?", "Confirm shutdown", MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        var result = await _power.ShutdownAsync(_machine.IpAddress);
        MessageBox.Show(this, result.Message, "Shut down", MessageBoxButton.OK,
            result.Success ? MessageBoxImage.Information : MessageBoxImage.Error);
    }

    private void Shares_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\\\\{_machine.IpAddress}\\",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Network shares", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Diagnostics_Click(object sender, RoutedEventArgs e)
    {
        var result = await _network.ProbeAsync(_machine);
        var latency = result.LatencyMs is null ? "No ping response" : $"{result.LatencyMs} ms";
        var vnc = result.VncAvailable ? $"Open on TCP {_machine.VncPort}" : $"Not reachable on TCP {_machine.VncPort}";
        MessageBox.Show(this,
            $"Machine: {_machine.Name}\nIP: {_machine.IpAddress}\nPing: {latency}\nVNC: {vnc}\nStatus: {result.Status}",
            "Connection info", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new MachineDialog(_machine) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        MachineChanged = true;
        RefreshHeader();
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, $"Remove {_machine.Name} from GrevUltraVNC?", "Remove machine",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        MachineDeleted = true;
        Close();
    }
}
