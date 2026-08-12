using System.ComponentModel;
using System.Diagnostics;
using System.ServiceProcess;
using GrevUltraVNC.Contracts;
using Microsoft.Win32;

namespace GrevUltraVNC.Agent;

public sealed class SystemInventoryService
{
    private static readonly HashSet<string> ProtectedProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "System",
        "Registry",
        "smss",
        "csrss",
        "wininit",
        "services",
        "lsass",
        "winlogon",
        "GrevUltraVNC.Agent"
    };

    private static readonly TimeSpan ServiceTimeout = TimeSpan.FromSeconds(20);

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

    public AgentActionResponse ControlProcess(AgentProcessActionRequest request)
    {
        if (!string.Equals(request.Action, "terminate", StringComparison.OrdinalIgnoreCase))
            return new AgentActionResponse(false, $"Unsupported process action: {request.Action}");

        if (request.ProcessId <= 4)
            return new AgentActionResponse(false, "That Windows process is protected and cannot be ended through Grev Agent.");

        try
        {
            using var process = Process.GetProcessById(request.ProcessId);
            var processName = process.ProcessName;

            if (ProtectedProcessNames.Contains(processName) || process.Id == Environment.ProcessId)
                return new AgentActionResponse(false, $"{processName} is protected and cannot be ended through Grev Agent.");

            process.Kill(entireProcessTree: true);
            process.WaitForExit(5000);
            return new AgentActionResponse(true, $"Ended {processName} (PID {request.ProcessId}).");
        }
        catch (ArgumentException)
        {
            return new AgentActionResponse(true, $"PID {request.ProcessId} is no longer running.");
        }
        catch (InvalidOperationException)
        {
            return new AgentActionResponse(true, $"PID {request.ProcessId} is no longer running.");
        }
        catch (Win32Exception ex)
        {
            return new AgentActionResponse(false, $"Windows refused to end PID {request.ProcessId}: {ex.Message}");
        }
        catch (Exception ex)
        {
            return new AgentActionResponse(false, $"Could not end PID {request.ProcessId}: {ex.Message}");
        }
    }

    public AgentActionResponse ControlService(AgentServiceActionRequest request)
    {
        var serviceName = request.ServiceName?.Trim();
        if (string.IsNullOrWhiteSpace(serviceName))
            return new AgentActionResponse(false, "No Windows service was selected.");

        var action = request.Action?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(action))
            return new AgentActionResponse(false, "No Windows service action was supplied.");

        if (string.Equals(serviceName, "GrevUltraVNCAgent", StringComparison.OrdinalIgnoreCase))
            return new AgentActionResponse(false, "Grev Agent cannot stop or restart its own Windows service remotely.");

        try
        {
            using var service = new ServiceController(serviceName);
            _ = service.DisplayName; // Forces Windows to resolve the service now.

            return action switch
            {
                "start" => StartService(service),
                "stop" => StopService(service),
                "restart" => RestartService(service),
                _ => new AgentActionResponse(false, $"Unsupported service action: {request.Action}")
            };
        }
        catch (InvalidOperationException ex)
        {
            return new AgentActionResponse(false, $"Could not access service {serviceName}: {ex.Message}");
        }
        catch (Win32Exception ex)
        {
            return new AgentActionResponse(false, $"Windows refused the service action: {ex.Message}");
        }
        catch (System.ServiceProcess.TimeoutException ex)
        {
            return new AgentActionResponse(false, $"Windows service action timed out: {ex.Message}");
        }
        catch (Exception ex)
        {
            return new AgentActionResponse(false, $"Service action failed: {ex.Message}");
        }
    }

    private static AgentActionResponse StartService(ServiceController service)
    {
        service.Refresh();

        if (service.Status == ServiceControllerStatus.Running)
            return new AgentActionResponse(true, $"{service.DisplayName} is already running.");

        if (service.Status == ServiceControllerStatus.StopPending)
        {
            service.WaitForStatus(ServiceControllerStatus.Stopped, ServiceTimeout);
            service.Refresh();
        }

        if (service.Status == ServiceControllerStatus.PausePending)
        {
            service.WaitForStatus(ServiceControllerStatus.Paused, ServiceTimeout);
            service.Refresh();
        }

        if (service.Status == ServiceControllerStatus.Paused)
        {
            service.Continue();
            service.WaitForStatus(ServiceControllerStatus.Running, ServiceTimeout);
            return new AgentActionResponse(true, $"Resumed {service.DisplayName}.");
        }

        if (service.Status == ServiceControllerStatus.StartPending)
        {
            service.WaitForStatus(ServiceControllerStatus.Running, ServiceTimeout);
            return new AgentActionResponse(true, $"{service.DisplayName} is running.");
        }

        service.Start();
        service.WaitForStatus(ServiceControllerStatus.Running, ServiceTimeout);
        return new AgentActionResponse(true, $"Started {service.DisplayName}.");
    }

    private static AgentActionResponse StopService(ServiceController service)
    {
        service.Refresh();

        if (service.Status == ServiceControllerStatus.Stopped)
            return new AgentActionResponse(true, $"{service.DisplayName} is already stopped.");

        if (service.Status == ServiceControllerStatus.StopPending)
        {
            service.WaitForStatus(ServiceControllerStatus.Stopped, ServiceTimeout);
            return new AgentActionResponse(true, $"{service.DisplayName} is stopped.");
        }

        if (service.Status == ServiceControllerStatus.StartPending)
        {
            service.WaitForStatus(ServiceControllerStatus.Running, ServiceTimeout);
            service.Refresh();
        }

        if (!service.CanStop)
            return new AgentActionResponse(false, $"Windows reports that {service.DisplayName} cannot be stopped.");

        service.Stop();
        service.WaitForStatus(ServiceControllerStatus.Stopped, ServiceTimeout);
        return new AgentActionResponse(true, $"Stopped {service.DisplayName}.");
    }

    private static AgentActionResponse RestartService(ServiceController service)
    {
        var stopped = StopService(service);
        if (!stopped.Success)
            return stopped;

        service.Refresh();
        var started = StartService(service);
        return started.Success
            ? new AgentActionResponse(true, $"Restarted {service.DisplayName}.")
            : started;
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
