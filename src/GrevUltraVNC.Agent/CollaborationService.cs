using GrevUltraVNC.Contracts;

namespace GrevUltraVNC.Agent;

public sealed class CollaborationService
{
    private static readonly TimeSpan PresenceTimeout = TimeSpan.FromSeconds(6);
    private const int MaxWhiteboardEvents = 400;
    private const int MaxReturnedEvents = 120;
    private const int MaxStrokePoints = 2048;

    private readonly object _gate = new();
    private readonly Dictionary<string, PresenceState> _participants = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<AgentWhiteboardEvent> _whiteboardEvents = [];
    private long _nextEventId;
    private string? _controlOwnerId;

    public AgentCollaborationResponse Execute(AgentCollaborationRequest request)
    {
        lock (_gate)
        {
            CleanupStaleParticipants();

            var controllerId = NormalizeControllerId(request.ControllerId);
            if (controllerId is null)
                return Snapshot(false, "Controller identity is invalid.", request.LastEventId);

            var displayName = NormalizeDisplayName(request.DisplayName);
            if (displayName is null)
                return Snapshot(false, "Grev Name must be 1-40 characters.", request.LastEventId);

            var operation = request.Operation?.Trim().ToLowerInvariant() ?? string.Empty;
            switch (operation)
            {
                case "heartbeat":
                case "snapshot":
                case "cursor":
                    TouchParticipant(controllerId, displayName, request);
                    return Snapshot(true, "Collaboration connected.", request.LastEventId);

                case "take-control":
                    TouchParticipant(controllerId, displayName, request);
                    _controlOwnerId = controllerId;
                    return Snapshot(true, $"{displayName} has control.", request.LastEventId);

                case "release-control":
                    TouchParticipant(controllerId, displayName, request);
                    if (string.Equals(_controlOwnerId, controllerId, StringComparison.OrdinalIgnoreCase))
                        _controlOwnerId = null;
                    return Snapshot(true, "Remote control released.", request.LastEventId);

                case "leave":
                    _participants.Remove(controllerId);
                    if (string.Equals(_controlOwnerId, controllerId, StringComparison.OrdinalIgnoreCase))
                        _controlOwnerId = null;
                    return Snapshot(true, $"{displayName} left the session.", request.LastEventId);

                case "publish":
                    TouchParticipant(controllerId, displayName, request);
                    if (request.WhiteboardEvent is null)
                        return Snapshot(false, "No whiteboard event was supplied.", request.LastEventId);

                    var published = NormalizeWhiteboardEvent(request.WhiteboardEvent, controllerId, displayName);
                    if (published is null)
                        return Snapshot(false, "Whiteboard event was invalid.", request.LastEventId);

                    if (string.Equals(published.Kind, "clear", StringComparison.OrdinalIgnoreCase))
                        _whiteboardEvents.Clear();

                    _whiteboardEvents.Add(published);
                    if (_whiteboardEvents.Count > MaxWhiteboardEvents)
                        _whiteboardEvents.RemoveRange(0, _whiteboardEvents.Count - MaxWhiteboardEvents);

                    return Snapshot(true, "Whiteboard updated.", request.LastEventId);

                default:
                    return Snapshot(false, "Unknown collaboration operation.", request.LastEventId);
            }
        }
    }

    private void TouchParticipant(string controllerId, string displayName, AgentCollaborationRequest request)
    {
        var now = DateTimeOffset.UtcNow;
        var preferredColor = CollaborationColors.Normalize(request.PreferredColor);
        var cursorStyle = NormalizeCursorStyle(request.PreferredCursorStyle);

        if (!_participants.TryGetValue(controllerId, out var participant))
        {
            participant = new PresenceState
            {
                ControllerId = controllerId,
                DisplayName = displayName,
                PreferredColor = preferredColor,
                Color = CollaborationColors.PickAvailable(
                    preferredColor,
                    _participants.Values.Select(item => item.Color)),
                CursorStyle = cursorStyle,
                ConnectedAtUtc = now,
                LastSeenUtc = now
            };
            _participants[controllerId] = participant;
        }
        else if (!string.Equals(participant.PreferredColor, preferredColor, StringComparison.OrdinalIgnoreCase))
        {
            participant.PreferredColor = preferredColor;
            participant.Color = CollaborationColors.PickAvailable(
                preferredColor,
                _participants
                    .Where(pair => !string.Equals(pair.Key, controllerId, StringComparison.OrdinalIgnoreCase))
                    .Select(pair => pair.Value.Color));
        }

        participant.DisplayName = displayName;
        participant.LastSeenUtc = now;
        participant.CursorStyle = cursorStyle;
        participant.CursorVisible = request.CursorVisible;
        participant.CursorSurface = NormalizeCursorSurface(request.CursorSurface);
        participant.CursorX = request.CursorX is null ? null : Math.Clamp(request.CursorX.Value, 0, 1);
        participant.CursorY = request.CursorY is null ? null : Math.Clamp(request.CursorY.Value, 0, 1);

        if (!participant.CursorVisible || participant.CursorX is null || participant.CursorY is null)
        {
            participant.CursorVisible = false;
            participant.CursorX = null;
            participant.CursorY = null;
        }
    }

    private void CleanupStaleParticipants()
    {
        var cutoff = DateTimeOffset.UtcNow - PresenceTimeout;
        foreach (var id in _participants
                     .Where(pair => pair.Value.LastSeenUtc < cutoff)
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            _participants.Remove(id);
            if (string.Equals(_controlOwnerId, id, StringComparison.OrdinalIgnoreCase))
                _controlOwnerId = null;
        }

        if (_controlOwnerId is not null && !_participants.ContainsKey(_controlOwnerId))
            _controlOwnerId = null;
    }

    private AgentCollaborationResponse Snapshot(bool success, string message, long lastEventId)
    {
        var participants = _participants.Values
            .OrderBy(item => item.ConnectedAtUtc)
            .Select(item => new AgentPresenceInfo(
                item.ControllerId,
                item.DisplayName,
                item.ConnectedAtUtc,
                item.LastSeenUtc,
                item.CursorX,
                item.CursorY,
                item.CursorVisible,
                item.CursorSurface,
                string.Equals(_controlOwnerId, item.ControllerId, StringComparison.OrdinalIgnoreCase),
                item.Color,
                item.CursorStyle))
            .ToArray();

        var events = _whiteboardEvents
            .Where(item => item.EventId > lastEventId)
            .TakeLast(MaxReturnedEvents)
            .ToArray();

        var latestEventId = _whiteboardEvents.Count == 0
            ? Math.Max(lastEventId, _nextEventId)
            : _whiteboardEvents[^1].EventId;

        string? ownerName = null;
        if (_controlOwnerId is not null && _participants.TryGetValue(_controlOwnerId, out var owner))
            ownerName = owner.DisplayName;

        return new AgentCollaborationResponse(
            success,
            message,
            participants,
            events,
            latestEventId,
            _controlOwnerId,
            ownerName);
    }

    private AgentWhiteboardEvent? NormalizeWhiteboardEvent(
        AgentWhiteboardEvent input,
        string controllerId,
        string displayName)
    {
        var kind = input.Kind?.Trim().ToLowerInvariant();
        if (kind is not ("stroke" or "clear")) return null;

        var points = kind == "clear"
            ? Array.Empty<AgentWhiteboardPoint>()
            : (input.Points ?? [])
                .Take(MaxStrokePoints)
                .Select(point => new AgentWhiteboardPoint(
                    Math.Clamp(point.X, 0, 1),
                    Math.Clamp(point.Y, 0, 1)))
                .ToArray();

        if (kind == "stroke" && points.Length < 2) return null;

        var color = NormalizeColor(input.Color);
        var thickness = Math.Clamp(input.Thickness, 1, 20);
        var strokeId = string.IsNullOrWhiteSpace(input.StrokeId)
            ? Guid.NewGuid().ToString("N")
            : input.StrokeId.Trim()[..Math.Min(64, input.StrokeId.Trim().Length)];

        return new AgentWhiteboardEvent(
            Interlocked.Increment(ref _nextEventId),
            controllerId,
            displayName,
            kind,
            strokeId,
            color,
            thickness,
            points,
            DateTimeOffset.UtcNow);
    }

    private static string NormalizeCursorSurface(string? value) =>
        string.Equals(value?.Trim(), "screen2", StringComparison.OrdinalIgnoreCase)
            ? "screen2"
            : "screen1";

    private static string NormalizeCursorStyle(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return normalized is "grev" or "arrow" or "crosshair" or "ring" or "diamond" or "pixel"
            ? normalized
            : "grev";
    }

    private static string? NormalizeControllerId(string? value)
    {
        var text = value?.Trim();
        if (string.IsNullOrWhiteSpace(text) || text.Length > 100) return null;
        return text.All(character => char.IsLetterOrDigit(character) || character is '-' or '_') ? text : null;
    }

    private static string? NormalizeDisplayName(string? value)
    {
        var text = value?.Trim();
        if (string.IsNullOrWhiteSpace(text) || text.Length > 40) return null;
        return text;
    }

    private static string NormalizeColor(string? value)
    {
        var text = value?.Trim() ?? string.Empty;
        if (text.Length == 7 && text[0] == '#' && text[1..].All(Uri.IsHexDigit))
            return text.ToUpperInvariant();
        return "#32CFF0";
    }

    private sealed class PresenceState
    {
        public required string ControllerId { get; init; }
        public required string DisplayName { get; set; }
        public required string PreferredColor { get; set; }
        public required string Color { get; set; }
        public required string CursorStyle { get; set; }
        public DateTimeOffset ConnectedAtUtc { get; init; }
        public DateTimeOffset LastSeenUtc { get; set; }
        public double? CursorX { get; set; }
        public double? CursorY { get; set; }
        public bool CursorVisible { get; set; }
        public string CursorSurface { get; set; } = "screen1";
    }
}
