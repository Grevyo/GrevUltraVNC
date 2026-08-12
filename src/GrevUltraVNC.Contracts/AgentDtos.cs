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

public sealed record AgentProcessInfo(
    int Id,
    string Name,
    long WorkingSetBytes,
    long PrivateMemoryBytes,
    long CpuTimeMilliseconds,
    int SessionId,
    DateTimeOffset? StartedAtUtc);

public sealed record AgentServiceInfo(
    string ServiceName,
    string DisplayName,
    string Status,
    string StartMode,
    bool CanStop,
    bool CanPauseAndContinue);

public sealed record AgentProcessActionRequest(
    int ProcessId,
    string Action);

public sealed record AgentServiceActionRequest(
    string ServiceName,
    string Action);

public sealed record AgentQuickActionRequest(
    string Action);

public sealed record AgentCommandRequest(
    string Shell,
    string Command,
    int TimeoutSeconds = 30);

public sealed record AgentCommandResponse(
    bool Success,
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut,
    long DurationMilliseconds);

public sealed record AgentActionResponse(
    bool Success,
    string Message);
