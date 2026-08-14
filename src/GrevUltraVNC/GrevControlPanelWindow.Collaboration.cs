using System.Windows;
using System.Windows.Controls;
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
        AddCursorStyleQuickPicker();
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

    private void AddCursorStyleQuickPicker()
    {
        if (ParticipantsItems.Parent is not StackPanel host)
            return;

        var heading = new Grid { Margin = new Thickness(1, 6, 1, 4) };
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var label = new TextBlock
        {
            Text = "YOUR CURSOR · CLICK A PREVIEW",
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 8,
            FontWeight = FontWeights.SemiBold
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, "FaintTextBrush");
        heading.Children.Add(label);

        var current = new TextBlock
        {
            Text = CursorStyleCatalog.DisplayName(_collaborationSettings.CursorStyle),
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 8,
            FontWeight = FontWeights.SemiBold
        };
        current.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        current.Tag = "cursor-current-label";
        Grid.SetColumn(current, 1);
        heading.Children.Add(current);

        _cursorStyleQuickSelector = new CursorStyleSelector(
            _collaborationSettings.CursorStyle,
            _collaborationSettings.CollaborationColor,
            compact: true)
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            MaxWidth = 360
        };
        _cursorStyleQuickSelector.SelectedStyleChanged += CursorStyleQuickSelector_SelectedStyleChanged;

        host.Children.Add(heading);
        host.Children.Add(_cursorStyleQuickSelector);
    }

    private void UpdateCursorStyleQuickPickerSelection()
    {
        if (_cursorStyleQuickSelector is null) return;
        _cursorStyleQuickSelector.SetColor(_collaborationSettings.CollaborationColor);
        _cursorStyleQuickSelector.SetSelectedStyle(_collaborationSettings.CursorStyle);
        UpdateCursorStyleCurrentLabel();
    }

    private void UpdateCursorStyleCurrentLabel()
    {
        if (ParticipantsItems.Parent is not StackPanel host) return;

        foreach (var grid in host.Children.OfType<Grid>())
        {
            var label = grid.Children
                .OfType<TextBlock>()
                .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), "cursor-current-label", StringComparison.Ordinal));
            if (label is null) continue;
            label.Text = CursorStyleCatalog.DisplayName(_collaborationSettings.CursorStyle);
            return;
        }
    }

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
                CollaborationStatusText.Text = response.ControlOwnerName is null
                    ? "No controller"
                    : $"Control · {response.ControlOwnerName}";
        }
        catch (Exception ex)
        {
            ParticipantsHeaderText.Text = "CONNECTED · —";
            ParticipantsItems.ItemsSource = Array.Empty<string>();
            AudioButton.IsEnabled = false;
            WhiteboardButton.IsEnabled = false;
            TakeControlButton.IsEnabled = false;
            ControlStatusText.Text = "VIEW ONLY · collaboration unavailable";
            RemoteKeysPanel.IsEnabled = false;
            try { _vnc.SetViewOnly(_machine.Id, true); } catch { }
            CollaborationStatusText.Text = ex.Message.Contains("too old", StringComparison.OrdinalIgnoreCase)
                ? "Update Agent"
                : "Unavailable";
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
        RemoteKeysPanel.IsEnabled = localHasControl;
        TakeControlButton.IsEnabled = true;
        TakeControlButton.Content = localHasControl ? "Release control" : "Take control";
        ControlStatusText.Text = localHasControl
            ? "CONTROL · YOU · remote mouse + keyboard enabled"
            : response.ControlOwnerName is null
                ? "VIEW ONLY · move your pointer freely · no one has control"
                : $"VIEW ONLY · {response.ControlOwnerName} has control";
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
                var role = participant.HasControl ? " · CONTROL" : " · VIEWING";
                return $"● {participant.DisplayName}{(mine ? " · YOU" : string.Empty)}{role}";
            })
            .ToArray();
    }

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
                AudioButton.Content = "🔊 Computer sound";
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
            AudioButton.Content = "🔊 Computer sound";
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
        VirtualDisplayButton.Content = screen2Active ? "▣ Screen 2 · ACTIVE" : "＋ Screen 2";
        CloseVirtualDisplayButton.Visibility = screen2Active ? Visibility.Visible : Visibility.Collapsed;
        DisplayStatusText.Text = screen2Active
            ? "Screen 1 physical · Screen 2 virtual"
            : "Screen 1 · physical display";
        SessionStatusText.Text = screen2Active ? "● SCREEN 1 + 2 ACTIVE" : "● SCREEN 1 ACTIVE";
    }
}
