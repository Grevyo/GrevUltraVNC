using System.Diagnostics;
using System.ServiceProcess;
using GrevUltraVNC.Contracts;
using Microsoft.Win32;

namespace GrevUltraVNC.Agent;

public sealed class SystemInventoryService
{
    public IReadOnlyList<AgentProcessInfo> GetProcesses()
    {
        var results = new List<AgentProcessInfo>();

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    DateTimeOffset? startedAt = null;
                    try
                    {
                        startedAt = process.StartTime.ToUniversalTime();
                    }
                    catch
                    {
                        // Protected/system processes may not expose their start time.
                    }

                    long cpuMs = 0;
                    try
                    {
                        cpuMs = checked((long)process.TotalProcessorTime.TotalMilliseconds);
                    }
                    catch
                    {
                        // Protected/system processes may not expose CPU time.
                    }

                    long workingSet = 0;
                    long privateMemory = 0;
                    int sessionId = -1;

                    try { workingSet = process.WorkingSet64; } catch { }
                    try { privateMemory = process.PrivateMemorySize64; } catch { }
                    try { sessionId = process.SessionId; } catch { }

                    results.Add(new AgentProcessInfo(
                        process.Id,
                        process.ProcessName,
                        workingSet,
                        privateMemory,
                        cpuMs,
                        sessionId,
                        startedAt));
                }
                catch
                {
                    // Processes can exit while being enumerated; skip those entries.
                }
            }
        }

        return results
            .OrderByDescending(x => x.WorkingSetBytes)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<AgentServiceInfo> GetServices()
    {
        var results = new List<AgentServiceInfo>();

        foreach (var service in ServiceController.GetServices())
        {
            using (service)
            {
                try
                {
                    results.Add(new AgentServiceInfo(
                        service.ServiceName,
                        service.DisplayName,
                        service.Status.ToString(),
                        GetStartMode(service.ServiceName),
                        service.CanStop,
                        service.CanPauseAndContinue));
                }
                catch
                {
                    // A service can disappear between enumeration and inspection.
                }
            }
        }

        return results
            .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.ServiceName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string GetStartMode(string serviceName)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{serviceName}");
            if (key?.GetValue("Start") is not int value)
                return "Unknown";

            return value switch
            {
                0 => "Boot",
                1 => "System",
                2 => "Automatic",
                3 => "Manual",
                4 => "Disabled",
                _ => "Unknown"
            };
        }
        catch
        {
            return "Unknown";
        }
    }
}
