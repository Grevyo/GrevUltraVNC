namespace GrevUltraVNC.Contracts;

public sealed record AgentPingResponse(
    string Product,
    string AgentVersion,
    string MachineName,
    bool AuthenticationRequired,
    int ProtocolVersion);

public sealed record AgentDiskStatus(
    string Name,
    string Label,
    long TotalBytes,
    long FreeBytes);

public sealed record AgentStatusResponse(
    string MachineName,
    string AgentVersion,
    string OsDescription,
    string CpuName,
    double CpuUsagePercent,
    long TotalMemoryBytes,
    long AvailableMemoryBytes,
    long UptimeSeconds,
    string? InteractiveUser,
    string UltraVncServiceStatus,
    bool UltraVncPortListening,
    int UltraVncPort,
    IReadOnlyList<AgentDiskStatus> Disks,
    DateTimeOffset CapturedAtUtc);
