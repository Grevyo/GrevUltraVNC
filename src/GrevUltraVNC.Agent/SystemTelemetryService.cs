using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using GrevUltraVNC.Contracts;
using Microsoft.Win32;

namespace GrevUltraVNC.Agent;

public sealed class SystemTelemetryService : BackgroundService
{
    private static readonly TimeSpan SnapshotLifetime = TimeSpan.FromSeconds(3);

    private readonly AgentConfiguration _configuration;
    private readonly SemaphoreSlim _snapshotGate = new(1, 1);
    private readonly string _agentVersion;
    private readonly string _osDescription;
    private readonly string _cpuName;
    private double _cpuUsagePercent;
    private AgentStatusResponse? _latestSnapshot;
    private DateTimeOffset _latestSnapshotAtUtc = DateTimeOffset.MinValue;

    public SystemTelemetryService(AgentConfiguration configuration)
    {
        _configuration = configuration;
        _agentVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "dev";
        _osDescription = RuntimeInformation.OSDescription;
        _cpuName = GetCpuName();
    }

    public async Task<AgentStatusResponse> CaptureAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = _latestSnapshot;
        if (snapshot is not null && now - _latestSnapshotAtUtc < SnapshotLifetime)
            return snapshot;

        await _snapshotGate.WaitAsync(cancellationToken);
        try
        {
            now = DateTimeOffset.UtcNow;
            snapshot = _latestSnapshot;
            if (snapshot is not null && now - _latestSnapshotAtUtc < SnapshotLifetime)
                return snapshot;

            snapshot = await CaptureFreshAsync(cancellationToken);
            _latestSnapshot = snapshot;
            _latestSnapshotAtUtc = DateTimeOffset.UtcNow;
            return snapshot;
        }
        finally
        {
            _snapshotGate.Release();
        }
    }

    private async Task<AgentStatusResponse> CaptureFreshAsync(CancellationToken cancellationToken)
    {
        var memory = GetMemoryStatus();
        var vncListening = await IsPortListeningAsync(_configuration.UltraVncPort, cancellationToken);

        var disks = new List<AgentDiskStatus>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady || drive.DriveType != DriveType.Fixed) continue;
                disks.Add(new AgentDiskStatus(
                    drive.Name,
                    drive.VolumeLabel,
                    drive.TotalSize,
                    drive.AvailableFreeSpace));
            }
            catch
            {
                // A drive can disappear between enumeration and property access.
            }
        }

        return new AgentStatusResponse(
            Environment.MachineName,
            _agentVersion,
            _osDescription,
            _cpuName,
            Math.Round(_cpuUsagePercent, 1),
            checked((long)memory.ullTotalPhys),
            checked((long)memory.ullAvailPhys),
            Environment.TickCount64 / 1000,
            GetInteractiveUser(),
            GetServiceStatus("uvnc_service"),
            vncListening,
            _configuration.UltraVncPort,
            disks,
            DateTimeOffset.UtcNow);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!TryGetSystemTimes(out var previous))
            return;

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (!TryGetSystemTimes(out var current))
                continue;

            var idle = current.Idle - previous.Idle;
            var kernel = current.Kernel - previous.Kernel;
            var user = current.User - previous.User;
            var total = kernel + user;

            if (total > 0)
            {
                var busy = Math.Max(0, total - idle);
                _cpuUsagePercent = Math.Clamp(busy * 100.0 / total, 0, 100);
            }

            previous = current;
        }
    }

    public override void Dispose()
    {
        _snapshotGate.Dispose();
        base.Dispose();
    }

    private static MemoryStatusEx GetMemoryStatus()
    {
        var status = new MemoryStatusEx
        {
            dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>()
        };

        return GlobalMemoryStatusEx(ref status) ? status : default;
    }

    private static string GetCpuName()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            return key?.GetValue("ProcessorNameString")?.ToString()?.Trim() ?? "Unknown CPU";
        }
        catch
        {
            return "Unknown CPU";
        }
    }

    private static string GetServiceStatus(string serviceName)
    {
        try
        {
            using var service = new ServiceController(serviceName);
            return service.Status.ToString();
        }
        catch (InvalidOperationException)
        {
            return "NotInstalled";
        }
        catch
        {
            return "Unknown";
        }
    }

    private static async Task<bool> IsPortListeningAsync(int port, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(600));

        try
        {
            using var client = new TcpClient(AddressFamily.InterNetwork);
            await client.ConnectAsync(IPAddress.Loopback, port, timeout.Token);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? GetInteractiveUser()
    {
        var sessionId = WTSGetActiveConsoleSessionId();
        if (sessionId == uint.MaxValue) return null;

        var user = QueryWtsString(sessionId, WtsInfoClass.WTSUserName);
        if (string.IsNullOrWhiteSpace(user)) return null;

        var domain = QueryWtsString(sessionId, WtsInfoClass.WTSDomainName);
        return string.IsNullOrWhiteSpace(domain) ? user : $"{domain}\\{user}";
    }

    private static string? QueryWtsString(uint sessionId, WtsInfoClass infoClass)
    {
        if (!WTSQuerySessionInformation(IntPtr.Zero, sessionId, infoClass, out var buffer, out _))
            return null;

        try
        {
            return Marshal.PtrToStringUni(buffer);
        }
        finally
        {
            WTSFreeMemory(buffer);
        }
    }

    private static bool TryGetSystemTimes(out CpuTimes times)
    {
        times = default;
        if (!GetSystemTimes(out var idle, out var kernel, out var user))
            return false;

        times = new CpuTimes(ToUInt64(idle), ToUInt64(kernel), ToUInt64(user));
        return true;
    }

    private static ulong ToUInt64(FileTime time) =>
        ((ulong)time.dwHighDateTime << 32) | time.dwLowDateTime;

    private readonly record struct CpuTimes(ulong Idle, ulong Kernel, ulong User);

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint dwLowDateTime;
        public uint dwHighDateTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MemoryStatusEx
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    private enum WtsInfoClass
    {
        WTSUserName = 5,
        WTSDomainName = 7
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out FileTime idleTime, out FileTime kernelTime, out FileTime userTime);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);

    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("Wtsapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool WTSQuerySessionInformation(
        IntPtr hServer,
        uint sessionId,
        WtsInfoClass wtsInfoClass,
        out IntPtr ppBuffer,
        out uint pBytesReturned);

    [DllImport("Wtsapi32.dll")]
    private static extern void WTSFreeMemory(IntPtr pointer);
}
