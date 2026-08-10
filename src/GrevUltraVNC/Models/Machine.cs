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

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New PC";
    public string IpAddress { get; set; } = "192.168.1.1";
    public string MacAddress { get; set; } = string.Empty;
    public int VncPort { get; set; } = 5900;
    public string Group { get; set; } = "My PCs";
    public string Notes { get; set; } = string.Empty;

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

    public string StatusText => Status switch
    {
        MachineStatus.Checking => "● CHECKING",
        MachineStatus.Online => "● ONLINE",
        MachineStatus.VncUnavailable => "● ONLINE · VNC OFFLINE",
        _ => "● OFFLINE"
    };

    public string DetailText => Status switch
    {
        MachineStatus.Online when LatencyMs is not null => $"{LatencyMs} ms · VNC {VncPort}",
        MachineStatus.VncUnavailable when LatencyMs is not null => $"{LatencyMs} ms · VNC {VncPort} unavailable",
        MachineStatus.Offline => "No response",
        _ => $"VNC {VncPort}"
    };

    public Machine Clone() => new()
    {
        Id = Id,
        Name = Name,
        IpAddress = IpAddress,
        MacAddress = MacAddress,
        VncPort = VncPort,
        Group = Group,
        Notes = Notes,
        Status = Status,
        LatencyMs = LatencyMs,
        VncAvailable = VncAvailable
    };

    public void ApplyFrom(Machine other)
    {
        Name = other.Name;
        IpAddress = other.IpAddress;
        MacAddress = other.MacAddress;
        VncPort = other.VncPort;
        Group = other.Group;
        Notes = other.Notes;
        OnPropertyChanged(string.Empty);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        OnPropertyChanged(propertyName);
        if (propertyName is nameof(Status) or nameof(LatencyMs) or nameof(VncAvailable))
        {
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(DetailText));
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
