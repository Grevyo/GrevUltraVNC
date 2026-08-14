using System.Windows;
using GrevUltraVNC.Contracts;
using GrevUltraVNC.Models;
using GrevUltraVNC.Services;

namespace GrevUltraVNC;

public partial class GrevConnectQuickWindow : Window
{
    private readonly IReadOnlyList<Machine> _machines;
    private readonly GrevConnectResolver _resolver;
    private readonly VncCredentialService _vncCredentials;
    private readonly AgentCredentialService _agentCredentials;

    public Machine? ResultMachine { get; private set; }

    public GrevConnectQuickWindow(
        IEnumerable<Machine> machines,
        GrevConnectResolver resolver,
        VncCredentialService vncCredentials,
        AgentCredentialService agentCredentials)
    {
        InitializeComponent();
        _machines = machines.ToArray();
        _resolver = resolver;
        _vncCredentials = vncCredentials;
        _agentCredentials = agentCredentials;
        Loaded += (_, _) => ConnectIdBox.Focus();
    }

    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        if (!GrevConnectId.TryNormalize(ConnectIdBox.Text, out var connectId, out var validationError))
        {
            StatusText.Text = validationError;
            return;
        }

        var agentKey = AgentKeyBox.Password.Trim();
        if (!string.IsNullOrWhiteSpace(agentKey) && !AgentProtocol.IsValidSharedKey(agentKey))
        {
            StatusText.Text = "That Agent pairing key does not look valid. Check that the whole key was copied.";
            return;
        }

        ConnectButton.IsEnabled = false;
        StatusText.Text = $"Looking for {connectId} on your LAN / Zima / Grev Connect routes…";

        try
        {
            var machine = _machines.FirstOrDefault(item => GrevConnectId.Equals(item.ConnectId, connectId));
            var isNew = machine is null;
            machine ??= new Machine
            {
                Name = connectId[3..],
                IpAddress = string.Empty,
                ConnectId = connectId,
                Group = "Friends",
                VncPort = 5900,
                AgentPort = AgentProtocol.DefaultPort
            };

            machine.ConnectId = connectId;
            var resolution = await _resolver.ResolveAsync(machine);
            if (resolution is null || string.IsNullOrWhiteSpace(machine.ActiveAddress))
            {
                throw new InvalidOperationException(
                    $"I found the ID format, but I cannot currently see {connectId}. " +
                    "Make sure the Zima / private Grev Connect network is connected, then try again.");
            }

            if (isNew && !string.IsNullOrWhiteSpace(resolution.MachineName))
                machine.Name = resolution.MachineName!;

            var hasSavedAgentKey = _agentCredentials.HasSavedKey(machine.Id);
            if (!hasSavedAgentKey && string.IsNullOrWhiteSpace(agentKey))
            {
                throw new InvalidOperationException(
                    $"Found {connectId} via {machine.ResolvedRoute}, but this controller is not paired yet. " +
                    "Open First-time pairing and paste the Grev Agent pairing key supplied by the PC owner. " +
                    "You only need to save it once on this PC.");
            }

            if (!string.IsNullOrEmpty(VncPasswordBox.Password))
                _vncCredentials.Save(machine.Id, VncPasswordBox.Password);

            if (!string.IsNullOrWhiteSpace(agentKey))
                _agentCredentials.Save(machine.Id, agentKey);

            ResultMachine = machine;
            StatusText.Text = $"Found {connectId} via {machine.ResolvedRoute} · {machine.ActiveAddress}. Opening it now…";
            DialogResult = true;
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
            MessageBox.Show(this, ex.Message, "GrevConnect", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        finally
        {
            ConnectButton.IsEnabled = true;
        }
    }
}
