using System.Net;
using System.Windows;
using GrevUltraVNC.Models;
using GrevUltraVNC.Services;

namespace GrevUltraVNC;

public partial class MachineDialog : Window
{
    private readonly Machine _target;
    private readonly Machine _working;
    private readonly VncCredentialService _credentials = new();
    private readonly AgentCredentialService _agentCredentials = new();
    private bool _forgetPasswordRequested;
    private bool _forgetAgentKeyRequested;
    private readonly bool _hadSavedPassword;
    private readonly bool _hadSavedAgentKey;

    public MachineDialog(Machine target)
    {
        InitializeComponent();
        _target = target;
        _working = target.Clone();
        NameBox.Text = _working.Name;
        IpBox.Text = _working.IpAddress;
        MacBox.Text = _working.MacAddress;
        PortBox.Text = _working.VncPort.ToString();
        AgentPortBox.Text = _working.AgentPort.ToString();
        GroupBox.Text = _working.Group;
        NotesBox.Text = _working.Notes;
        FavoriteCheck.IsChecked = _working.IsFavorite;

        _hadSavedPassword = _credentials.HasSavedPassword(_working.Id);
        PasswordStateText.Text = _hadSavedPassword
            ? "A VNC password is saved securely in Windows Credential Manager. Leave this blank to keep it, or enter a new password to replace it."
            : "No VNC password is saved. Enter one here and GrevUltraVNC will use it automatically when connecting.";

        _hadSavedAgentKey = _agentCredentials.HasSavedKey(_working.Id);
        AgentKeyStateText.Text = _hadSavedAgentKey
            ? "This machine is paired with Grev Agent. Leave the key blank to keep the saved pairing, or paste a new key to replace it."
            : "No Grev Agent pairing key is saved. Install the agent on the target PC, then paste the pairing key printed by its installer here.";
    }

    private void ForgetPassword_Click(object sender, RoutedEventArgs e)
    {
        _forgetPasswordRequested = true;
        VncPasswordBox.Clear();
        PasswordStateText.Text = _hadSavedPassword
            ? "The saved VNC password will be removed when you click Save machine."
            : "No saved VNC password to remove.";
    }

    private void ForgetAgentKey_Click(object sender, RoutedEventArgs e)
    {
        _forgetAgentKeyRequested = true;
        AgentKeyBox.Clear();
        AgentKeyStateText.Text = _hadSavedAgentKey
            ? "The saved Grev Agent pairing key will be removed when you click Save machine."
            : "No Grev Agent pairing key is currently saved.";
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
        if (!int.TryParse(AgentPortBox.Text, out var agentPort) || agentPort is < 1 or > 65535)
        {
            MessageBox.Show(this, "Enter a valid Grev Agent port (1-65535).", "GrevUltraVNC", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!string.IsNullOrWhiteSpace(mac) && new string(mac.Where(Uri.IsHexDigit).ToArray()).Length != 12)
        {
            MessageBox.Show(this, "The MAC address must contain 12 hexadecimal digits.", "GrevUltraVNC", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            if (!string.IsNullOrEmpty(VncPasswordBox.Password))
                _credentials.Save(_working.Id, VncPasswordBox.Password);
            else if (_forgetPasswordRequested)
                _credentials.Delete(_working.Id);

            if (!string.IsNullOrWhiteSpace(AgentKeyBox.Password))
                _agentCredentials.Save(_working.Id, AgentKeyBox.Password);
            else if (_forgetAgentKeyRequested)
                _agentCredentials.Delete(_working.Id);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not save secure machine credentials", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        _working.Name = name;
        _working.IpAddress = ip;
        _working.MacAddress = mac;
        _working.VncPort = port;
        _working.AgentPort = agentPort;
        _working.Group = string.IsNullOrWhiteSpace(group) ? "My PCs" : group;
        _working.Notes = NotesBox.Text.Trim();
        _working.IsFavorite = FavoriteCheck.IsChecked == true;
        _target.ApplyFrom(_working);
        DialogResult = true;
    }
}
