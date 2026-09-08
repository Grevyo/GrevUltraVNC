using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using GrevUltraVNC.Contracts;
using GrevUltraVNC.Services;

namespace GrevUltraVNC.Models;

public enum MachineStatus
{
    Checking,
    Online,
    VncUnavailable,
    Offline
}

public sealed class Machine : INotifyPropertyChanged
{
    private MachineStatus _status = MachineStatus.Checking;
    private long? _latencyMs;
    private bool _vncAvailable;
    private bool _isFavorite;
    private DateTime? _lastCheckedAt;
    private GrevAgentState _agentState = GrevAgentState.Unknown;
    private AgentStatusResponse? _agentStatus;
    private string? _agentMessage;
    private string _connectId = string.Empty;
    private string? _resolvedAddress;
    private string _resolvedRoute = string.Empty;

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New PC";
    public string IpAddress { get; set; } = "192.168.1.1";
    public string MacAddress { get; set; } = string.Empty;
    public int VncPort { get; set; } = 5900;
    public int AgentPort { get; set; } = AgentProtocol.DefaultPort;
    public string Group { get; set; } = "My PCs";
    public string Notes { get; set; } = string.Empty;

    public string ConnectId
    {
        get => _connectId;
        set => SetField(ref _connectId, value?.Trim() ?? string.Empty);
    }

    [JsonIgnore]
    public string? ResolvedAddress
    {
        get => _resolvedAddress;
        set => SetField(ref _resolvedAddress, value);
    }

    [JsonIgnore]
    public string ResolvedRoute
    {
        get => _resolvedRoute;
        set => SetField(ref _resolvedRoute, value ?? string.Empty);
    }

    [JsonIgnore]
    public string ActiveAddress
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(ResolvedAddress)) return ResolvedAddress!;
            return string.IsNullOrWhiteSpace(ConnectId) ? IpAddress : string.Empty;
        }
    }

    [JsonIgnore]
    public string ConnectDisplayText
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(ResolvedAddress))
            {
                var route = string.IsNullOrWhiteSpace(ResolvedRoute) ? "Grev Connect" : ResolvedRoute;
                return string.IsNullOrWhiteSpace(ConnectId)
                    ? $"{route} {ResolvedAddress}"
                    : $"{ConnectId} · {route} {ResolvedAddress}";
            }

            if (!string.IsNullOrWhiteSpace(ConnectId))
                return $"{ConnectId} · route unavailable";

            return string.IsNullOrWhiteSpace(IpAddress)
                ? "No route configured"
                : $"LAN {IpAddress}";
        }
    }

    public bool IsFavorite
    {
        get => _isFavorite;
        set => SetField(ref _isFavorite, value);
    }

    public MachineStatus Status
    {
        get => _status;
        set => SetField(ref _status, value);
    }

    public long? LatencyMs
    {
        get => _latencyMs;
        set => SetField(ref _latencyMs, value);
    }

    public bool VncAvailable
    {
        get => _vncAvailable;
        set => SetField(ref _vncAvailable, value);
    }

    public DateTime? LastCheckedAt
    {
        get => _lastCheckedAt;
        set => SetField(ref _lastCheckedAt, value);
    }

    [JsonIgnore]
    public GrevAgentState AgentState
    {
        get => _agentState;
        set => SetField(ref _agentState, value);
    }

    [JsonIgnore]
    public AgentStatusResponse? AgentStatus
    {
        get => _agentStatus;
        set => SetField(ref _agentStatus, value);
    }

    [JsonIgnore]
    public string? AgentMessage
    {
        get => _agentMessage;
        set => SetField(ref _agentMessage, value);
    }

    public string FavoriteGlyph => IsFavorite ? "★" : "☆";

    /// <summary>Notes, or null so WPF suppresses the tooltip entirely when there are none.</summary>
    [JsonIgnore]
    public string? NotesTooltip => string.IsNullOrWhiteSpace(Notes) ? null : Notes;

    /// <summary>Two-letter tile used in place of a per-machine icon.</summary>
    [JsonIgnore]
    public string Initials
    {
        get
        {
            var words = Name.Split([' ', '-', '_', '.'], StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) return "PC";
            if (words.Length == 1)
                return words[0].Length == 1
                    ? words[0].ToUpperInvariant()
                    : words[0][..2].ToUpperInvariant();
            return $"{char.ToUpperInvariant(words[0][0])}{char.ToUpperInvariant(words[1][0])}";
        }
    }

    public string StatusText => Status switch
    {
        MachineStatus.Checking => "● CHECKING",
        MachineStatus.Online => "● PC ONLINE",
        MachineStatus.VncUnavailable => "● PC ONLINE · VNC UNREACHABLE",
        _ => "● PC OFFLINE"
    };

    /// <summary>Short form for a status pill, where the surrounding chip carries the colour.</summary>
    public string StatusLabel => Status switch
    {
        MachineStatus.Checking => "CHECKING",
        MachineStatus.Online => "ONLINE",
        MachineStatus.VncUnavailable => "NO VNC",
        _ => "OFFLINE"
    };

    /// <summary>How this machine is currently reached: local network, Zima overlay, or nothing.</summary>
    public string RouteBadge
    {
        get
        {
            if (string.IsNullOrWhiteSpace(ResolvedAddress))
                return string.IsNullOrWhiteSpace(ConnectId) ? "NO ROUTE" : "SEARCHING";

            if (ResolvedRoute.Contains("Zima", StringComparison.OrdinalIgnoreCase))
                return "ZIMA";

            return ResolvedRoute.Contains("LAN", StringComparison.OrdinalIgnoreCase) ? "LAN" : "GREV CONNECT";
        }
    }

    /// <summary>The address the viewer will actually dial, or the reason there isn't one.</summary>
    public string RouteDetail => string.IsNullOrWhiteSpace(ActiveAddress)
        ? (string.IsNullOrWhiteSpace(ConnectId) ? "No address configured" : $"{ConnectId} not on any reachable network")
        : $"{ActiveAddress}:{VncPort}";

    public string PingText => LatencyMs is not null ? $"Ping {LatencyMs} ms" : "No ping reply";

    /// <summary>Bare latency for a dense chip.</summary>
    public string LatencyText => LatencyMs is not null ? $"{LatencyMs} ms" : "— ms";

    public string VncText => VncAvailable
        ? $"TCP {VncPort} reachable"
        : $"TCP {VncPort} unavailable";

    public string DetailText => Status switch
    {
        MachineStatus.Online => $"{PingText} · {VncText}",
        MachineStatus.VncUnavailable => $"{PingText} · {VncText}",
        MachineStatus.Offline => string.IsNullOrWhiteSpace(ConnectId) ? "No network response" : $"{ConnectId} not currently reachable",
        _ => string.IsNullOrWhiteSpace(ActiveAddress) ? "Resolving Grev Connect route…" : $"Checking {ActiveAddress}:{VncPort}"
    };

    public string LastCheckedText => LastCheckedAt is null
        ? "Not checked yet"
        : $"Checked {GrevFormat.Since(LastCheckedAt)}";

    public string AgentStatusText => AgentState switch
    {
        GrevAgentState.Unknown => "AGENT CHECKING",
        GrevAgentState.Connected => "● AGENT CONNECTED",
        GrevAgentState.ReadyToPair => "● AGENT READY TO PAIR",
        GrevAgentState.AuthenticationFailed => "● AGENT KEY REJECTED",
        GrevAgentState.Error => "● AGENT ERROR",
        _ => "AGENT NOT DETECTED"
    };

    public string AgentStatusLabel => AgentState switch
    {
        GrevAgentState.Unknown => "CHECKING",
        GrevAgentState.Connected => "AGENT",
        GrevAgentState.ReadyToPair => "PAIR ME",
        GrevAgentState.AuthenticationFailed => "KEY BAD",
        GrevAgentState.Error => "AGENT ERR",
        _ => "NO AGENT"
    };

    // ---- Live telemetry, surfaced so a dashboard card can draw meters directly ----

    /// <summary>True once the Agent is paired and has reported at least one status sample.</summary>
    [JsonIgnore]
    public bool HasTelemetry => AgentState == GrevAgentState.Connected && AgentStatus is not null;

    [JsonIgnore]
    public double CpuPercent => AgentStatus is null ? 0 : Math.Clamp(AgentStatus.CpuUsagePercent, 0, 100);

    [JsonIgnore]
    public double MemoryPercent => AgentStatus is null
        ? 0
        : GrevFormat.UsedPercent(AgentStatus.TotalMemoryBytes - AgentStatus.AvailableMemoryBytes, AgentStatus.TotalMemoryBytes);

    /// <summary>Fullest fixed disk, because that is the one that will cause trouble first.</summary>
    [JsonIgnore]
    public double DiskPercent
    {
        get
        {
            var disks = AgentStatus?.Disks;
            if (disks is null || disks.Count == 0) return 0;
            return disks.Max(disk => GrevFormat.UsedPercent(disk.TotalBytes - disk.FreeBytes, disk.TotalBytes));
        }
    }

    [JsonIgnore]
    public string CpuText => HasTelemetry ? GrevFormat.Percent(CpuPercent) : "—";

    [JsonIgnore]
    public string MemoryText => HasTelemetry
        ? $"{GrevFormat.ShortGigabytes(AgentStatus!.TotalMemoryBytes - AgentStatus.AvailableMemoryBytes)} / {GrevFormat.ShortGigabytes(AgentStatus.TotalMemoryBytes)}"
        : "—";

    [JsonIgnore]
    public string DiskText
    {
        get
        {
            var disks = AgentStatus?.Disks;
            if (!HasTelemetry || disks is null || disks.Count == 0) return "—";

            var fullest = disks
                .OrderByDescending(disk => GrevFormat.UsedPercent(disk.TotalBytes - disk.FreeBytes, disk.TotalBytes))
                .First();
            return $"{fullest.Name} {GrevFormat.Bytes(fullest.FreeBytes)} free";
        }
    }

    [JsonIgnore]
    public string UptimeText => HasTelemetry ? GrevFormat.Uptime(AgentStatus!.UptimeSeconds) : "—";

    [JsonIgnore]
    public string SignedInUserText => HasTelemetry
        ? (string.IsNullOrWhiteSpace(AgentStatus!.InteractiveUser) ? "Nobody signed in" : AgentStatus.InteractiveUser!)
        : "Unknown";

    [JsonIgnore]
    public string OsText => HasTelemetry ? AgentStatus!.OsDescription : string.Empty;

    /// <summary>Single sentence explaining what to do next when telemetry is not available.</summary>
    public string AgentSummaryText
    {
        get
        {
            if (HasTelemetry)
                return $"CPU {CpuText} · RAM {MemoryText} · Up {UptimeText}";

            return AgentState switch
            {
                GrevAgentState.ReadyToPair => "Agent found · paste pairing key in Edit",
                GrevAgentState.AuthenticationFailed => "Saved pairing key was rejected",
                GrevAgentState.Error => AgentMessage ?? "Agent returned an error",
                GrevAgentState.NotDetected => $"No response on agent TCP {AgentPort}",
                _ => $"Checking agent TCP {AgentPort}"
            };
        }
    }

    public Machine Clone() => new()
    {
        Id = Id,
        Name = Name,
        IpAddress = IpAddress,
        ConnectId = ConnectId,
        MacAddress = MacAddress,
        VncPort = VncPort,
        AgentPort = AgentPort,
        Group = Group,
        Notes = Notes,
        IsFavorite = IsFavorite,
        Status = Status,
        LatencyMs = LatencyMs,
        VncAvailable = VncAvailable,
        LastCheckedAt = LastCheckedAt,
        AgentState = AgentState,
        AgentStatus = AgentStatus,
        AgentMessage = AgentMessage,
        ResolvedAddress = ResolvedAddress,
        ResolvedRoute = ResolvedRoute
    };

    public void ApplyFrom(Machine other)
    {
        Name = other.Name;
        IpAddress = other.IpAddress;
        ConnectId = other.ConnectId;
        MacAddress = other.MacAddress;
        VncPort = other.VncPort;
        AgentPort = other.AgentPort;
        Group = other.Group;
        Notes = other.Notes;
        IsFavorite = other.IsFavorite;
        OnPropertyChanged(string.Empty);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        OnPropertyChanged(propertyName);

        if (propertyName is nameof(Status) or nameof(LatencyMs) or nameof(VncAvailable) or nameof(LastCheckedAt) or nameof(ResolvedAddress) or nameof(ResolvedRoute) or nameof(ConnectId))
        {
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(StatusLabel));
            OnPropertyChanged(nameof(PingText));
            OnPropertyChanged(nameof(LatencyText));
            OnPropertyChanged(nameof(VncText));
            OnPropertyChanged(nameof(DetailText));
            OnPropertyChanged(nameof(LastCheckedText));
            OnPropertyChanged(nameof(ActiveAddress));
            OnPropertyChanged(nameof(ConnectDisplayText));
            OnPropertyChanged(nameof(RouteBadge));
            OnPropertyChanged(nameof(RouteDetail));
        }

        if (propertyName is nameof(AgentState) or nameof(AgentStatus) or nameof(AgentMessage))
        {
            OnPropertyChanged(nameof(AgentStatusText));
            OnPropertyChanged(nameof(AgentStatusLabel));
            OnPropertyChanged(nameof(AgentSummaryText));
            OnPropertyChanged(nameof(HasTelemetry));
            OnPropertyChanged(nameof(CpuPercent));
            OnPropertyChanged(nameof(MemoryPercent));
            OnPropertyChanged(nameof(DiskPercent));
            OnPropertyChanged(nameof(CpuText));
            OnPropertyChanged(nameof(MemoryText));
            OnPropertyChanged(nameof(DiskText));
            OnPropertyChanged(nameof(UptimeText));
            OnPropertyChanged(nameof(SignedInUserText));
            OnPropertyChanged(nameof(OsText));
        }

        if (propertyName == nameof(IsFavorite))
            OnPropertyChanged(nameof(FavoriteGlyph));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
