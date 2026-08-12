using GrevUltraVNC.Contracts;

namespace GrevUltraVNC.Agent;

public sealed class CollaborationService
{
    private static readonly TimeSpan PresenceTimeout = TimeSpan.FromSeconds(8);
    private const int MaxWhiteboardEvents = 400;
    private const int MaxReturnedEvents = 120;
    private const int MaxStrokePoints = 2048;

    private readonly object _gate = new();
    private readonly Dictionary<string, PresenceState> _participants = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<AgentWhiteboardEvent> _whiteboardEvents = [];
    private long _nextEventId;

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
                    TouchParticipant(controllerId, displayName);
                    return Snapshot(true, "Collaboration connected.", request.LastEventId);

                case "leave":
                    _participants.Remove(controllerId);
                    return Snapshot(true, $"{displayName} left the session.", request.LastEventId);

                case "publish":
                    TouchParticipant(controllerId, displayName);
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

    private void TouchParticipant(string controllerId, string displayName)
    {
        var now = DateTimeOffset.UtcNow;
        if (_participants.TryGetValue(controllerId, out var existing))
        {
            existing.DisplayName = displayName;
            existing.LastSeenUtc = now;
            return;
        }

        _participants[controllerId] = new PresenceState
        {
            ControllerId = controllerId,
            DisplayName = displayName,
            ConnectedAtUtc = now,
            LastSeenUtc = now
        };
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
        }
    }

    private AgentCollaborationResponse Snapshot(bool success, string message, long lastEventId)
    {
        var participants = _participants.Values
            .OrderBy(item => item.ConnectedAtUtc)
            .Select(item => new AgentPresenceInfo(
                item.ControllerId,
                item.DisplayName,
                item.ConnectedAtUtc,
                item.LastSeenUtc))
            .ToArray();

        var events = _whiteboardEvents
            .Where(item => item.EventId > lastEventId)
            .TakeLast(MaxReturnedEvents)
            .ToArray();

        var latestEventId = _whiteboardEvents.Count == 0
            ? Math.Max(lastEventId, _nextEventId)
            : _whiteboardEvents[^1].EventId;

        return new AgentCollaborationResponse(success, message, participants, events, latestEventId);
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
        public DateTimeOffset ConnectedAtUtc { get; init; }
        public DateTimeOffset LastSeenUtc { get; set; }
    }
}
