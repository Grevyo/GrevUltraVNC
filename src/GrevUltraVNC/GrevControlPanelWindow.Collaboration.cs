using System.Windows;
using System.Windows.Threading;
using GrevUltraVNC.Contracts;
using GrevUltraVNC.Models;
using GrevUltraVNC.Services;

namespace GrevUltraVNC;

public partial class GrevControlPanelWindow
{
    private AppSettings _collaborationSettings = new();
    private readonly GrevAgentClient _collaborationClient = new();
    private readonly DispatcherTimer _collaborationTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly List<AgentWhiteboardEvent> _whiteboardHistory = [];
    private bool _collaborationRefreshRunning;
    private long _lastWhiteboardEventId;
    private RemoteAudioPlaybackService? _remoteAudio;
    private WhiteboardOverlayWindow? _whiteboardOverlay;

    public GrevControlPanelWindow(Machine machine, UltraVncSessionService vnc, AppSettings settings)
        : this(machine, vnc)
    {
        _collaborationSettings = settings;
        _collaborationTimer.Tick += CollaborationTimer_Tick;
        Loaded += GrevCollaboration_Loaded;
        Closed += GrevCollaboration_Closed;
    }

    private async void GrevCollaboration_Loaded(object sender, RoutedEventArgs e)
    {
        _collaborationTimer.Start();
        await RefreshCollaborationAsync();
    }

    private void GrevCollaboration_Closed(object? sender, EventArgs e)
    {
        _collaborationTimer.Stop();
        try { _whiteboardOverlay?.Close(); } catch { }
        _whiteboardOverlay = null;
        try { _remoteAudio?.Dispose(); } catch { }
        _remoteAudio = null;
        _collaborationClient.Dispose();
    }

    private async void CollaborationTimer_Tick(object? sender, EventArgs e) =>
        await RefreshCollaborationAsync();

    private async Task RefreshCollaborationAsync()
    {
        if (_collaborationRefreshRunning) return;
        _collaborationRefreshRunning = true;

        try
        {
            var response = await _collaborationClient.RunCollaborationAsync(
                _machine,
                new AgentCollaborationRequest(
                    "heartbeat",
                    _collaborationSettings.ControllerId,
                    _collaborationSettings.GrevName,
                    _lastWhiteboardEventId));

            if (!response.Success)
                throw new InvalidOperationException(response.Message);

            UpdateParticipants(response.Participants);
            ConsumeWhiteboardEvents(response.WhiteboardEvents);
            _lastWhiteboardEventId = Math.Max(_lastWhiteboardEventId, response.LastEventId);
            AudioButton.IsEnabled = true;
            WhiteboardButton.IsEnabled = true;

            if (_remoteAudio?.IsRunning != true)
                CollaborationStatusText.Text = "Collaboration ready";
        }
        catch (Exception ex)
        {
            ParticipantsHeaderText.Text = "CONNECTED · —";
            ParticipantsItems.ItemsSource = Array.Empty<string>();
            AudioButton.IsEnabled = false;
            WhiteboardButton.IsEnabled = false;
            CollaborationStatusText.Text = ex.Message.Contains("too old", StringComparison.OrdinalIgnoreCase)
                ? "Update Agent"
                : "Unavailable";
        }
        finally
        {
            _collaborationRefreshRunning = false;
        }
    }

    private void UpdateParticipants(IReadOnlyList<AgentPresenceInfo> participants)
    {
        ParticipantsHeaderText.Text = $"CONNECTED · {participants.Count}";
        ParticipantsItems.ItemsSource = participants
            .Select(participant => string.Equals(
                    participant.ControllerId,
                    _collaborationSettings.ControllerId,
                    StringComparison.OrdinalIgnoreCase)
                ? $"● {participant.DisplayName} · YOU"
                : $"● {participant.DisplayName}")
            .ToArray();
    }

    private void ConsumeWhiteboardEvents(IReadOnlyList<AgentWhiteboardEvent> events)
    {
        if (events.Count == 0) return;

        foreach (var item in events)
        {
            if (string.Equals(item.Kind, "clear", StringComparison.OrdinalIgnoreCase))
            {
                _whiteboardHistory.Clear();
                _whiteboardHistory.Add(item);
                continue;
            }

            if (!string.Equals(item.Kind, "stroke", StringComparison.OrdinalIgnoreCase) ||
                _whiteboardHistory.Any(existing => string.Equals(
                    existing.StrokeId,
                    item.StrokeId,
                    StringComparison.OrdinalIgnoreCase)))
                continue;

            _whiteboardHistory.Add(item);
        }

        if (_whiteboardHistory.Count > 400)
            _whiteboardHistory.RemoveRange(0, _whiteboardHistory.Count - 400);

        _whiteboardOverlay?.ApplyEvents(events);
    }

    private async void Audio_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _remoteAudio ??= new RemoteAudioPlaybackService(
                _machine,
                _collaborationSettings.ControllerId);

            if (_remoteAudio.IsRunning)
            {
                await _remoteAudio.StopAsync();
                AudioButton.Content = "🔊 Sound";
                CollaborationStatusText.Text = "Computer audio off";
                return;
            }

            _remoteAudio.StatusChanged -= RemoteAudio_StatusChanged;
            _remoteAudio.StatusChanged += RemoteAudio_StatusChanged;
            await _remoteAudio.StartAsync();
            AudioButton.Content = "🔇 Sound off";
            CollaborationStatusText.Text = "Computer audio on";
        }
        catch (Exception ex)
        {
            AudioButton.Content = "🔊 Sound";
            CollaborationStatusText.Text = "Audio unavailable";
            MessageBox.Show(this, ex.Message, "Computer audio", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void RemoteAudio_StatusChanged(string message)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => RemoteAudio_StatusChanged(message));
            return;
        }

        if (message.StartsWith("Audio reconnecting", StringComparison.OrdinalIgnoreCase))
            CollaborationStatusText.Text = "Audio reconnecting";
    }

    private void Whiteboard_Click(object sender, RoutedEventArgs e)
    {
        if (_whiteboardOverlay is not null)
        {
            if (_whiteboardOverlay.IsVisible)
            {
                _whiteboardOverlay.Activate();
                return;
            }

            _whiteboardOverlay = null;
        }

        var overlay = new WhiteboardOverlayWindow(
            _machine,
            _vnc,
            _collaborationSettings);
        _whiteboardOverlay = overlay;
        overlay.WhiteboardEventCreated += WhiteboardEventCreated;
        overlay.Closed += (_, _) =>
        {
            if (ReferenceEquals(_whiteboardOverlay, overlay))
                _whiteboardOverlay = null;
        };
        overlay.Show();
        overlay.ApplyEvents(_whiteboardHistory);
    }

    private async void WhiteboardEventCreated(AgentWhiteboardEvent item)
    {
        try
        {
            var response = await _collaborationClient.RunCollaborationAsync(
                _machine,
                new AgentCollaborationRequest(
                    "publish",
                    _collaborationSettings.ControllerId,
                    _collaborationSettings.GrevName,
                    _lastWhiteboardEventId,
                    item));

            if (!response.Success)
                throw new InvalidOperationException(response.Message);

            UpdateParticipants(response.Participants);
            ConsumeWhiteboardEvents(response.WhiteboardEvents);
            _lastWhiteboardEventId = Math.Max(_lastWhiteboardEventId, response.LastEventId);
        }
        catch (Exception ex)
        {
            CollaborationStatusText.Text = "Whiteboard sync failed";
            MessageBox.Show(this, ex.Message, "Grev Whiteboard", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void VirtualDisplay_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _vnc.OpenVirtualDisplay(_machine, _collaborationSettings);
            CollaborationStatusText.Text = "Screen 2 requested";
        }
        catch (Exception ex)
        {
            CollaborationStatusText.Text = "Screen 2 unavailable";
            MessageBox.Show(this,
                $"{ex.Message}\n\nThe target UltraVNC server must support its virtual-display / desktop-resize feature.",
                "Virtual Screen 2",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }
}
