using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using GrevUltraVNC.Contracts;
using GrevUltraVNC.Models;
using GrevUltraVNC.Services;

namespace GrevUltraVNC;

public partial class GrevControlPanelWindow
{
    private AppSettings _collaborationSettings = new();
    private readonly GrevAgentClient _collaborationClient = new();
    private readonly DispatcherTimer _collaborationTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private readonly List<AgentWhiteboardEvent> _whiteboardHistory = [];
    private readonly SemaphoreSlim _whiteboardPublishGate = new(1, 1);
    private bool _collaborationRefreshRunning;
    private bool _virtualDisplayStarting;
    private long _lastWhiteboardEventId;
    private string? _controlOwnerId;
    private RemoteAudioPlaybackService? _remoteAudio;
    private WhiteboardOverlayWindow? _whiteboardOverlay;
    private NamedCursorOverlayWindow? _screen1CursorOverlay;
    private NamedCursorOverlayWindow? _screen2CursorOverlay;
    private CursorStyleSelector? _cursorStyleQuickSelector;

    public event EventHandler? CollaborationSettingsChanged;

    public GrevControlPanelWindow(Machine machine, UltraVncSessionService vnc, AppSettings settings)
        : this(machine, vnc)
    {
        _collaborationSettings = settings;
        BuildCursorStylePicker();
        _collaborationTimer.Tick += CollaborationTimer_Tick;
        Loaded += GrevCollaboration_Loaded;
        Closed += GrevCollaboration_Closed;
    }

    public void UpdateCollaborationSettings(AppSettings settings)
    {
        _collaborationSettings = settings;
        var preferredColor = CollaborationColors.Normalize(settings.CollaborationColor);
        var preferredCursorStyle = CursorStyleCatalog.Normalize(settings.CursorStyle);
        _screen1CursorOverlay?.UpdatePreferredColor(preferredColor);
        _screen2CursorOverlay?.UpdatePreferredColor(preferredColor);
        _screen1CursorOverlay?.UpdatePreferredCursorStyle(preferredCursorStyle);
        _screen2CursorOverlay?.UpdatePreferredCursorStyle(preferredCursorStyle);
        UpdateCursorStyleQuickPickerSelection();
    }

    private void BuildCursorStylePicker()
    {
        _cursorStyleQuickSelector = new CursorStyleSelector(
            _collaborationSettings.CursorStyle,
            _collaborationSettings.CollaborationColor,
            compact: true)
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            MaxWidth = 400
        };
        _cursorStyleQuickSelector.SelectedStyleChanged += CursorStyleQuickSelector_SelectedStyleChanged;

        CursorStyleHost.Content = _cursorStyleQuickSelector;
        UpdateCursorStyleCurrentLabel();
    }

    private void UpdateCursorStyleQuickPickerSelection()
    {
        if (_cursorStyleQuickSelector is null) return;
        _cursorStyleQuickSelector.SetColor(_collaborationSettings.CollaborationColor);
        _cursorStyleQuickSelector.SetSelectedStyle(_collaborationSettings.CursorStyle);
        UpdateCursorStyleCurrentLabel();
    }

    private void UpdateCursorStyleCurrentLabel() =>
        CursorStyleNameText.Text = CursorStyleCatalog.DisplayName(_collaborationSettings.CursorStyle);

    private async void CursorStyleQuickSelector_SelectedStyleChanged(string selected)
    {
        var normalized = CursorStyleCatalog.Normalize(selected);
        if (string.Equals(_collaborationSettings.CursorStyle, normalized, StringComparison.OrdinalIgnoreCase))
            return;

        _collaborationSettings.CursorStyle = normalized;

        // Change the controller's own overlay immediately. Do not wait for the Agent heartbeat to
        // echo the new preference back before showing the chosen shape under the local mouse.
        _screen1CursorOverlay?.UpdatePreferredCursorStyle(normalized);
        _screen2CursorOverlay?.UpdatePreferredCursorStyle(normalized);
        UpdateCursorStyleCurrentLabel();
        CollaborationStatusText.Text = $"Cursor · {CursorStyleCatalog.DisplayName(normalized)}";
        CollaborationSettingsChanged?.Invoke(this, EventArgs.Empty);

        // Also push a cursor snapshot immediately so the other connected Grev controllers see the
        // new shape straight away instead of waiting for the next periodic heartbeat.
        try
        {
            var response = await _collaborationClient.RunCollaborationAsync(
                _machine,
                BuildCollaborationRequest("cursor"));
            if (response.Success)
                ApplyCollaborationResponse(response);
        }
        catch
        {
            // The local choice remains selected/saved. The normal heartbeat will retry Agent sync.
            CollaborationStatusText.Text = $"Cursor · {CursorStyleCatalog.DisplayName(normalized)} · sync pending";
        }
    }

    private async void GrevCollaboration_Loaded(object sender, RoutedEventArgs e)
    {
        // Connecting never grants remote input automatically. The named pointer is available
        // immediately; mouse/keyboard input is enabled only after Take Control succeeds.
        _vnc.SetViewOnly(_machine.Id, true);
        ApplyStoredSectionState();
        EnsureCursorOverlays();
        UpdateDisplayState();
        _collaborationTimer.Start();
        await RefreshCollaborationAsync();
    }

    private async void GrevCollaboration_Closed(object? sender, EventArgs e)
    {
        _collaborationTimer.Stop();
        try { _vnc.SetViewOnly(_machine.Id, true); } catch { }
        try { _whiteboardOverlay?.Close(); } catch { }
        _whiteboardOverlay = null;
        CloseCursorOverlays();
        try { _remoteAudio?.Dispose(); } catch { }
        _remoteAudio = null;

        try
        {
            await _collaborationClient.RunCollaborationAsync(
                _machine,
                BuildCollaborationRequest("leave"));
        }
        catch
        {
            // The Agent also expires presence/control automatically if a controller disappears.
        }
        finally
        {
            _collaborationClient.Dispose();
            _whiteboardPublishGate.Dispose();
        }
    }

    private async void CollaborationTimer_Tick(object? sender, EventArgs e)
    {
        UpdateDisplayState();
        EnsureCursorOverlays();
        await RefreshCollaborationAsync();
    }

    private async Task RefreshCollaborationAsync()
    {
        if (_collaborationRefreshRunning) return;
        _collaborationRefreshRunning = true;

        try
        {
            var response = await _collaborationClient.RunCollaborationAsync(
                _machine,
                BuildCollaborationRequest("heartbeat"));

            if (!response.Success)
                throw new InvalidOperationException(response.Message);

            ApplyCollaborationResponse(response);
            AudioButton.IsEnabled = true;
            WhiteboardButton.IsEnabled = true;
            TakeControlButton.IsEnabled = true;

            if (_remoteAudio?.IsRunning != true && !_virtualDisplayStarting)
                CollaborationStatusText.Text = "Grev Collaboration";
        }
        catch (Exception ex)
        {
            var needsAgentUpdate = ex.Message.Contains("too old", StringComparison.OrdinalIgnoreCase);

            ParticipantsHeaderText.Text = "CONNECTED · —";
            ParticipantsItems.ItemsSource = Array.Empty<ParticipantRow>();
            ParticipantsEmptyText.Visibility = Visibility.Visible;
            ParticipantsEmptyText.Text = needsAgentUpdate
                ? "Update the Grev Agent on this machine to see who else is connected."
                : "Grev collaboration is not reachable, so nobody can be listed.";

            AudioButton.IsEnabled = false;
            WhiteboardButton.IsEnabled = false;
            TakeControlButton.IsEnabled = false;
            RemoteKeysPanel.IsEnabled = false;
            try { _vnc.SetViewOnly(_machine.Id, true); } catch { }

            ApplyControlState(
                available: false,
                localHasControl: false,
                ownerName: null,
                unavailableReason: needsAgentUpdate
                    ? "This machine's Grev Agent is too old for shared control. Update it from Machine actions."
                    : ex.Message);

            CollaborationStatusText.Text = needsAgentUpdate ? "update agent" : "unavailable";
        }
        finally
        {
            _collaborationRefreshRunning = false;
        }
    }

    private AgentCollaborationRequest BuildCollaborationRequest(
        string operation,
        AgentWhiteboardEvent? whiteboardEvent = null)
    {
        var cursorVisible = _vnc.TryGetLocalPointer(
            _machine.Id,
            out var surface,
            out var cursorX,
            out var cursorY);

        return new AgentCollaborationRequest(
            operation,
            _collaborationSettings.ControllerId,
            _collaborationSettings.GrevName,
            _lastWhiteboardEventId,
            whiteboardEvent,
            cursorVisible ? cursorX : null,
            cursorVisible ? cursorY : null,
            cursorVisible,
            cursorVisible ? surface : "screen1",
            CollaborationColors.Normalize(_collaborationSettings.CollaborationColor),
            CursorStyleCatalog.Normalize(_collaborationSettings.CursorStyle));
    }

    private void ApplyCollaborationResponse(AgentCollaborationResponse response)
    {
        _controlOwnerId = response.ControlOwnerId;
        UpdateParticipants(response.Participants);
        ConsumeWhiteboardEvents(response.WhiteboardEvents);
        _lastWhiteboardEventId = Math.Max(_lastWhiteboardEventId, response.LastEventId);
        _screen1CursorOverlay?.UpdateParticipants(response.Participants, _collaborationSettings.ControllerId);
        _screen2CursorOverlay?.UpdateParticipants(response.Participants, _collaborationSettings.ControllerId);

        var localHasControl = string.Equals(
            response.ControlOwnerId,
            _collaborationSettings.ControllerId,
            StringComparison.OrdinalIgnoreCase);

        _vnc.SetViewOnly(_machine.Id, !localHasControl);
        ApplyControlState(
            available: true,
            localHasControl: localHasControl,
            ownerName: response.ControlOwnerName,
            unavailableReason: null);
    }

    /// <summary>
    /// Single source of truth for the remote-input banner: the headline, the explanation,
    /// the colour, the button caption, and whether the remote keys are usable.
    /// </summary>
    private void ApplyControlState(bool available, bool localHasControl, string? ownerName, string? unavailableReason)
    {
        RemoteKeysPanel.IsEnabled = available && localHasControl;
        RemoteKeysHintText.Visibility = available && localHasControl
            ? Visibility.Collapsed
            : Visibility.Visible;

        if (!available)
        {
            SetControlBannerColour("DangerBrush");
            ControlStatusText.Text = "Remote control unavailable";
            ControlHintText.Text = unavailableReason ?? "Grev collaboration could not be reached on this machine.";
            TakeControlButton.Content = "Take control";
            RemoteKeysNoteText.Text = "unavailable";
            RemoteKeysNoteText.Foreground = ThemeService.ThemeBrush("DangerBrush");
            RemoteKeysHintText.Text = "Remote keys need shared control, which is not available on this machine right now.";
            return;
        }

        TakeControlButton.IsEnabled = true;

        if (localHasControl)
        {
            SetControlBannerColour("OkBrush");
            ControlStatusText.Text = "You have control";
            ControlHintText.Text = "Your mouse and keyboard now go to the remote PC. Release control to hand it back.";
            TakeControlButton.Content = "Release control";
            RemoteKeysNoteText.Text = "ready";
            RemoteKeysNoteText.Foreground = ThemeService.ThemeBrush("OkBrush");
            return;
        }

        TakeControlButton.Content = "Take control";
        RemoteKeysHintText.Text = "Take control first — these keys are sent to the remote PC, not this one.";

        if (string.IsNullOrWhiteSpace(ownerName))
        {
            SetControlBannerColour("AccentBrush");
            ControlStatusText.Text = "View only · nobody has control";
            ControlHintText.Text = "Your named pointer is already visible to everyone. Take control to send mouse and keyboard to this PC.";
            RemoteKeysNoteText.Text = "needs control";
            RemoteKeysNoteText.Foreground = ThemeService.ThemeBrush("FaintTextBrush");
            return;
        }

        SetControlBannerColour("WarnBrush");
        ControlStatusText.Text = $"View only · {ownerName} has control";
        ControlHintText.Text = $"Taking control will move it away from {ownerName}. Your pointer stays visible either way.";
        RemoteKeysNoteText.Text = $"{ownerName} has control";
        RemoteKeysNoteText.Foreground = ThemeService.ThemeBrush("WarnBrush");
    }

    private void SetControlBannerColour(string brushKey)
    {
        var brush = ThemeService.ThemeBrush(brushKey);
        ControlAccentBar.Background = brush;
        ControlBanner.BorderBrush = brush;
        ControlStatusText.Foreground = brush;
    }

    private void UpdateParticipants(IReadOnlyList<AgentPresenceInfo> participants)
    {
        ParticipantsHeaderText.Text = $"CONNECTED · {participants.Count}";

        ParticipantsItems.ItemsSource = participants
            .Select(participant =>
            {
                var mine = string.Equals(
                    participant.ControllerId,
                    _collaborationSettings.ControllerId,
                    StringComparison.OrdinalIgnoreCase);

                return new ParticipantRow(
                    mine ? $"{participant.DisplayName} (you)" : participant.DisplayName,
                    participant.HasControl ? "CONTROL" : "VIEWING",
                    ParticipantBrush(participant.Color));
            })
            // You first, then whoever holds control, then everyone else alphabetically.
            .OrderByDescending(row => row.Name.EndsWith("(you)", StringComparison.Ordinal))
            .ThenByDescending(row => row.Role == "CONTROL")
            .ThenBy(row => row.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        // One participant is just you, which is worth saying rather than showing a lone row
        // with no context.
        ParticipantsEmptyText.Visibility = participants.Count > 1 ? Visibility.Collapsed : Visibility.Visible;
        ParticipantsEmptyText.Text = participants.Count == 0
            ? "Nobody is listed as connected yet."
            : "Nobody else is connected to this machine right now.";
    }

    /// <summary>A participant's own collaboration colour, falling back to the Grev default.</summary>
    private static Brush ParticipantBrush(string? color)
    {
        try
        {
            var normalized = CollaborationColors.Normalize(color);
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(normalized)!);
            brush.Freeze();
            return brush;
        }
        catch
        {
            return ThemeService.ThemeBrush("AccentBrush");
        }
    }

    private sealed record ParticipantRow(string Name, string Role, Brush Colour);

    private async void TakeControl_Click(object sender, RoutedEventArgs e)
    {
        TakeControlButton.IsEnabled = false;
        try
        {
            var localHasControl = string.Equals(
                _controlOwnerId,
                _collaborationSettings.ControllerId,
                StringComparison.OrdinalIgnoreCase);
            var operation = localHasControl ? "release-control" : "take-control";

            var response = await _collaborationClient.RunCollaborationAsync(
                _machine,
                BuildCollaborationRequest(operation));
            if (!response.Success)
                throw new InvalidOperationException(response.Message);

            ApplyCollaborationResponse(response);
            CollaborationStatusText.Text = localHasControl ? "Control released" : "You have control";
        }
        catch (Exception ex)
        {
            try { _vnc.SetViewOnly(_machine.Id, true); } catch { }
            RemoteKeysPanel.IsEnabled = false;
            ControlStatusText.Text = "VIEW ONLY · could not change control owner";
            MessageBox.Show(this, ex.Message, "Remote control", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            TakeControlButton.IsEnabled = true;
        }
    }

    private void EnsureCursorOverlays()
    {
        if (_screen1CursorOverlay is null && _vnc.HasActiveSession(_machine.Id))
        {
            var overlay = new NamedCursorOverlayWindow(
                _machine,
                _vnc,
                virtualDisplay: false,
                _collaborationSettings.CollaborationColor,
                _collaborationSettings.CursorStyle);
            _screen1CursorOverlay = overlay;
            overlay.Closed += (_, _) =>
            {
                if (ReferenceEquals(_screen1CursorOverlay, overlay))
                    _screen1CursorOverlay = null;
            };
            overlay.Show();
        }

        var needsScreen2 = _vnc.HasVirtualSession(_machine.Id);
        if (needsScreen2 && _screen2CursorOverlay is null)
        {
            var overlay = new NamedCursorOverlayWindow(
                _machine,
                _vnc,
                virtualDisplay: true,
                _collaborationSettings.CollaborationColor,
                _collaborationSettings.CursorStyle);
            _screen2CursorOverlay = overlay;
            overlay.Closed += (_, _) =>
            {
                if (ReferenceEquals(_screen2CursorOverlay, overlay))
                    _screen2CursorOverlay = null;
            };
            overlay.Show();
        }
        else if (!needsScreen2 && _screen2CursorOverlay is not null)
        {
            try { _screen2CursorOverlay.Close(); } catch { }
            _screen2CursorOverlay = null;
        }
    }

    private void CloseCursorOverlays()
    {
        try { _screen1CursorOverlay?.Close(); } catch { }
        try { _screen2CursorOverlay?.Close(); } catch { }
        _screen1CursorOverlay = null;
        _screen2CursorOverlay = null;
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

            if (string.Equals(item.Kind, "delete", StringComparison.OrdinalIgnoreCase))
            {
                _whiteboardHistory.RemoveAll(existing =>
                    string.Equals(existing.Kind, "stroke", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(existing.StrokeId, item.StrokeId, StringComparison.OrdinalIgnoreCase));
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
                SetAudioButtonState(playing: false);
                CollaborationStatusText.Text = "sound off";
                return;
            }

            _remoteAudio.StatusChanged -= RemoteAudio_StatusChanged;
            _remoteAudio.StatusChanged += RemoteAudio_StatusChanged;
            await _remoteAudio.StartAsync();
            SetAudioButtonState(playing: true);
            CollaborationStatusText.Text = "sound on";
        }
        catch (Exception ex)
        {
            SetAudioButtonState(playing: false);
            CollaborationStatusText.Text = "sound unavailable";
            MessageBox.Show(this, ex.Message, "Computer audio", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SetAudioButtonState(bool playing)
    {
        AudioGlyphText.Text = playing ? "🔇" : "🔊";
        AudioLabelText.Text = playing ? "Mute" : "Sound";
        AudioButton.ToolTip = playing
            ? "Stop playing the remote PC's sound on this computer"
            : "Hear the remote PC's sound on this computer";
    }

    private void RemoteAudio_StatusChanged(string message)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => RemoteAudio_StatusChanged(message));
            return;
        }

        if (message.StartsWith("Audio reconnecting", StringComparison.OrdinalIgnoreCase))
            CollaborationStatusText.Text = "sound reconnecting";
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
        overlay.WhiteboardEventsCreated += WhiteboardEventsCreated;
        overlay.Closed += (_, _) =>
        {
            if (ReferenceEquals(_whiteboardOverlay, overlay))
                _whiteboardOverlay = null;
        };
        overlay.Show();
        overlay.ApplyEvents(_whiteboardHistory);
    }

    private async void WhiteboardEventsCreated(IReadOnlyList<AgentWhiteboardEvent> items)
    {
        await _whiteboardPublishGate.WaitAsync();
        try
        {
            foreach (var item in items)
            {
                var response = await _collaborationClient.RunCollaborationAsync(
                    _machine,
                    BuildCollaborationRequest("publish", item));

                if (!response.Success)
                    throw new InvalidOperationException(response.Message);

                ApplyCollaborationResponse(response);
            }
        }
        catch (Exception ex)
        {
            CollaborationStatusText.Text = "Whiteboard sync failed";
            MessageBox.Show(this, ex.Message, "Grev Whiteboard", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _whiteboardPublishGate.Release();
        }
    }

    private void UpdateDisplayState()
    {
        if (_virtualDisplayStarting) return;

        var screen2Active = _vnc.HasVirtualSession(_machine.Id);
        VirtualDisplayButton.IsEnabled = true;
        VirtualDisplayButton.Content = screen2Active ? "▣  Screen 2 open" : "＋  Screen 2";
        CloseVirtualDisplayButton.Visibility = screen2Active ? Visibility.Visible : Visibility.Collapsed;
        DisplayStatusText.Text = screen2Active
            ? "Screen 1 is the real monitor · Screen 2 is a virtual monitor added by Grev"
            : "Screen 1 · the remote PC's real display. Add Screen 2 for a second workspace.";
        SessionStatusText.Text = screen2Active ? "Screen 1 + 2 active" : "Screen 1 active";
        SessionStatusDot.Fill = ThemeService.ThemeBrush("OkBrush");
    }
}
