using System.Net;
using System.Windows;
using GrevUltraVNC.Models;

namespace GrevUltraVNC;

public partial class MachineDialog : Window
{
    private readonly Machine _target;
    private readonly Machine _working;

    public MachineDialog(Machine target)
    {
        InitializeComponent();
        _target = target;
        _working = target.Clone();
        NameBox.Text = _working.Name;
        IpBox.Text = _working.IpAddress;
        MacBox.Text = _working.MacAddress;
        PortBox.Text = _working.VncPort.ToString();
        GroupBox.Text = _working.Group;
        NotesBox.Text = _working.Notes;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        var ip = IpBox.Text.Trim();
        var mac = MacBox.Text.Trim();
        var group = GroupBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show(this, "Give the machine a name.", "GrevUltraVNC", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!IPAddress.TryParse(ip, out _))
        {
            MessageBox.Show(this, "Enter a valid static IP address.", "GrevUltraVNC", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!int.TryParse(PortBox.Text, out var port) || port is < 1 or > 65535)
        {
            MessageBox.Show(this, "Enter a valid VNC port (1-65535).", "GrevUltraVNC", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!string.IsNullOrWhiteSpace(mac) && new string(mac.Where(Uri.IsHexDigit).ToArray()).Length != 12)
        {
            MessageBox.Show(this, "The MAC address must contain 12 hexadecimal digits.", "GrevUltraVNC", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _working.Name = name;
        _working.IpAddress = ip;
        _working.MacAddress = mac;
        _working.VncPort = port;
        _working.Group = string.IsNullOrWhiteSpace(group) ? "My PCs" : group;
        _working.Notes = NotesBox.Text.Trim();
        _target.ApplyFrom(_working);
        DialogResult = true;
    }
}
