using System.ComponentModel;
using System.Runtime.CompilerServices;

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

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New PC";
    public string IpAddress { get; set; } = "192.168.1.1";
    public string MacAddress { get; set; } = string.Empty;
    public int VncPort { get; set; } = 5900;
    public string Group { get; set; } = "My PCs";
    public string Notes { get; set; } = string.Empty;

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

    public string FavoriteGlyph => IsFavorite ? "★" : "☆";

    public string StatusText => Status switch
    {
        MachineStatus.Checking => "● CHECKING",
        MachineStatus.Online => "● PC ONLINE",
        MachineStatus.VncUnavailable => "● PC ONLINE · VNC UNREACHABLE",
        _ => "● PC OFFLINE"
    };

    public string PingText => LatencyMs is not null ? $"Ping {LatencyMs} ms" : "No ping reply";

    public string VncText => VncAvailable
        ? $"TCP {VncPort} reachable"
        : $"TCP {VncPort} unavailable";

    public string DetailText => Status switch
    {
        MachineStatus.Online => $"{PingText} · {VncText}",
        MachineStatus.VncUnavailable => $"{PingText} · {VncText}",
        MachineStatus.Offline => "No network response",
        _ => $"Checking {IpAddress}:{VncPort}"
    };

    public string LastCheckedText => LastCheckedAt is null
        ? "Not checked yet"
        : $"Checked {LastCheckedAt:HH:mm:ss}";

    public Machine Clone() => new()
    {
        Id = Id,
        Name = Name,
        IpAddress = IpAddress,
        MacAddress = MacAddress,
        VncPort = VncPort,
        Group = Group,
        Notes = Notes,
        IsFavorite = IsFavorite,
        Status = Status,
        LatencyMs = LatencyMs,
        VncAvailable = VncAvailable,
        LastCheckedAt = LastCheckedAt
    };

    public void ApplyFrom(Machine other)
    {
        Name = other.Name;
        IpAddress = other.IpAddress;
        MacAddress = other.MacAddress;
        VncPort = other.VncPort;
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

        if (propertyName is nameof(Status) or nameof(LatencyMs) or nameof(VncAvailable) or nameof(LastCheckedAt))
        {
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(PingText));
            OnPropertyChanged(nameof(VncText));
            OnPropertyChanged(nameof(DetailText));
            OnPropertyChanged(nameof(LastCheckedText));
        }

        if (propertyName == nameof(IsFavorite))
            OnPropertyChanged(nameof(FavoriteGlyph));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
