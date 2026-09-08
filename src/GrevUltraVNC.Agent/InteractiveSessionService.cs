using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using GrevUltraVNC.Contracts;

namespace GrevUltraVNC.Agent;

public sealed class InteractiveSessionService
{
    private const uint TokenAdjustPrivileges = 0x0020;
    private const uint TokenQuery = 0x0008;
    private const uint SePrivilegeEnabled = 0x00000002;
    private const int ErrorNotAllAssigned = 1300;
    private const string ShutdownPrivilege = "SeShutdownPrivilege";

    public AgentActionResponse RunQuickAction(AgentQuickActionRequest request)
    {
        var action = request.Action?.Trim().ToLowerInvariant();
        return action switch
        {
            "restart-explorer" => RestartExplorer(),
            "lock" => LockWorkstation(),
            "sign-out" or "logoff" => SignOutInteractiveUser(),
            "sleep" => ScheduleSuspend(hibernate: false),
            "hibernate" => ScheduleSuspend(hibernate: true),
            _ => new AgentActionResponse(false, $"Unsupported quick action: {request.Action}")
        };
    }

    private static AgentActionResponse RestartExplorer()
    {
        var sessionId = InteractiveSessionLauncher.GetActiveConsoleSessionId();
        if (sessionId == InteractiveSessionLauncher.NoActiveSession)
            return new AgentActionResponse(false, "No interactive Windows session is currently active.");

        try
        {
            foreach (var process in Process.GetProcessesByName("explorer"))
            {
                using (process)
                {
                    try
                    {
                        if ((uint)process.SessionId != sessionId) continue;
                        process.Kill(entireProcessTree: true);
                        process.WaitForExit(3000);
                    }
                    catch
                    {
                        // Continue so we can still attempt to relaunch the shell.
                    }
                }
            }

            var explorerPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "explorer.exe");

            if (!File.Exists(explorerPath))
                return new AgentActionResponse(false, $"Windows Explorer was not found at {explorerPath}.");

            InteractiveSessionLauncher.Launch(sessionId, explorerPath, arguments: null, detached: false, what: "Windows Explorer");
            return new AgentActionResponse(true, "Restarted Windows Explorer in the active user session.");
        }
        catch (Exception ex)
        {
            return new AgentActionResponse(false, $"Could not restart Windows Explorer: {ex.Message}");
        }
    }

    private static AgentActionResponse LockWorkstation()
    {
        var sessionId = InteractiveSessionLauncher.GetActiveConsoleSessionId();
        if (sessionId == InteractiveSessionLauncher.NoActiveSession)
            return new AgentActionResponse(false, "No interactive Windows session is currently active.");

        try
        {
            var rundll32 = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "rundll32.exe");

            if (!File.Exists(rundll32))
                return new AgentActionResponse(false, "Windows rundll32.exe could not be found.");

            // LockWorkStation must run on the interactive desktop, so launch the
            // request with the active console user's token instead of from Session 0.
            InteractiveSessionLauncher.Launch(sessionId, rundll32, "user32.dll,LockWorkStation", detached: false, what: "the workstation lock request");
            return new AgentActionResponse(true, "Lock request sent to the active Windows session.");
        }
        catch (Exception ex)
        {
            return new AgentActionResponse(false, $"Could not lock the workstation: {ex.Message}");
        }
    }

    private static AgentActionResponse SignOutInteractiveUser()
    {
        var sessionId = InteractiveSessionLauncher.GetActiveConsoleSessionId();
        if (sessionId == InteractiveSessionLauncher.NoActiveSession)
            return new AgentActionResponse(false, "No interactive Windows session is currently active.");

        try
        {
            if (!WTSLogoffSession(IntPtr.Zero, sessionId, false))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows refused to sign out the active session.");

            return new AgentActionResponse(true, "Sign-out request sent to the active Windows session.");
        }
        catch (Exception ex)
        {
            return new AgentActionResponse(false, $"Could not sign out the active user: {ex.Message}");
        }
    }

    private static AgentActionResponse ScheduleSuspend(bool hibernate)
    {
        var actionName = hibernate ? "hibernate" : "sleep";

        try
        {
            // Validate the privilege before acknowledging the request. The actual
            // power transition happens shortly afterwards so ASP.NET can flush the
            // authenticated success response back to GrevUltraVNC first.
            EnableShutdownPrivilege();

            _ = Task.Run(async () =>
            {
                await Task.Delay(1500);
                try
                {
                    EnableShutdownPrivilege();
                    SetSuspendState(hibernate, false, false);
                }
                catch
                {
                    // The request has already been acknowledged to the controller.
                    // A future Agent event log will surface delayed power failures.
                }
            });

            return new AgentActionResponse(true, hibernate
                ? "Hibernate scheduled. The machine will hibernate momentarily."
                : "Sleep scheduled. The machine will sleep momentarily.");
        }
        catch (Exception ex)
        {
            return new AgentActionResponse(false, $"Could not schedule {actionName}: {ex.Message}");
        }
    }

    private static void EnableShutdownPrivilege()
    {
        using var process = Process.GetCurrentProcess();
        if (!OpenProcessToken(process.Handle, TokenAdjustPrivileges | TokenQuery, out var token))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not open the Grev Agent process token.");

        try
        {
            if (!LookupPrivilegeValue(null, ShutdownPrivilege, out var luid))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not resolve the Windows shutdown privilege.");

            var privileges = new TokenPrivileges
            {
                PrivilegeCount = 1,
                Privileges = new LuidAndAttributes
                {
                    Luid = luid,
                    Attributes = SePrivilegeEnabled
                }
            };

            if (!AdjustTokenPrivileges(token, false, ref privileges, 0, IntPtr.Zero, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not enable the Windows shutdown privilege.");

            var error = Marshal.GetLastWin32Error();
            if (error == ErrorNotAllAssigned)
                throw new Win32Exception(error, "The Grev Agent account does not have the Windows shutdown privilege.");
        }
        finally
        {
            InteractiveSessionLauncher.CloseHandle(token);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LuidAndAttributes
    {
        public Luid Luid;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenPrivileges
    {
        public uint PrivilegeCount;
        public LuidAndAttributes Privileges;
    }

    [DllImport("Wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSLogoffSession(IntPtr serverHandle, uint sessionId, bool wait);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LookupPrivilegeValue(string? systemName, string name, out Luid luid);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AdjustTokenPrivileges(
        IntPtr tokenHandle,
        bool disableAllPrivileges,
        ref TokenPrivileges newState,
        uint bufferLength,
        IntPtr previousState,
        IntPtr returnLength);

    [DllImport("powrprof.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);
}
