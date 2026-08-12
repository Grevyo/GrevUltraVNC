using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using GrevUltraVNC.Contracts;

namespace GrevUltraVNC.Agent;

public sealed class InteractiveSessionService
{
    private const uint MaximumAllowed = 0x02000000;
    private const uint CreateUnicodeEnvironment = 0x00000400;
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
        var sessionId = WTSGetActiveConsoleSessionId();
        if (sessionId == uint.MaxValue)
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

            LaunchInInteractiveSession(sessionId, explorerPath);
            return new AgentActionResponse(true, "Restarted Windows Explorer in the active user session.");
        }
        catch (Exception ex)
        {
            return new AgentActionResponse(false, $"Could not restart Windows Explorer: {ex.Message}");
        }
    }

    private static AgentActionResponse LockWorkstation()
    {
        var sessionId = WTSGetActiveConsoleSessionId();
        if (sessionId == uint.MaxValue)
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
            LaunchInInteractiveSession(sessionId, rundll32, "user32.dll,LockWorkStation");
            return new AgentActionResponse(true, "Lock request sent to the active Windows session.");
        }
        catch (Exception ex)
        {
            return new AgentActionResponse(false, $"Could not lock the workstation: {ex.Message}");
        }
    }

    private static AgentActionResponse SignOutInteractiveUser()
    {
        var sessionId = WTSGetActiveConsoleSessionId();
        if (sessionId == uint.MaxValue)
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
            CloseHandle(token);
        }
    }

    private static void LaunchInInteractiveSession(uint sessionId, string executablePath, string? arguments = null)
    {
        if (!WTSQueryUserToken(sessionId, out var userToken))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not get the interactive user's Windows token.");

        try
        {
            if (!DuplicateTokenEx(
                    userToken,
                    MaximumAllowed,
                    IntPtr.Zero,
                    SecurityImpersonationLevel.SecurityImpersonation,
                    TokenType.TokenPrimary,
                    out var primaryToken))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not create an interactive primary token.");

            try
            {
                IntPtr environment = IntPtr.Zero;
                try
                {
                    if (!CreateEnvironmentBlock(out environment, primaryToken, false))
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not create the interactive user's environment.");

                    var startupInfo = new StartupInfo
                    {
                        cb = Marshal.SizeOf<StartupInfo>(),
                        lpDesktop = @"winsta0\default"
                    };

                    StringBuilder? commandLine = null;
                    if (!string.IsNullOrWhiteSpace(arguments))
                        commandLine = new StringBuilder($"\"{executablePath}\" {arguments}");

                    if (!CreateProcessAsUser(
                            primaryToken,
                            executablePath,
                            commandLine,
                            IntPtr.Zero,
                            IntPtr.Zero,
                            false,
                            CreateUnicodeEnvironment,
                            environment,
                            Path.GetDirectoryName(executablePath),
                            ref startupInfo,
                            out var processInfo))
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows refused to launch the action in the interactive session.");

                    try
                    {
                        // Creation succeeded; the interactive process owns its lifetime.
                    }
                    finally
                    {
                        if (processInfo.hThread != IntPtr.Zero) CloseHandle(processInfo.hThread);
                        if (processInfo.hProcess != IntPtr.Zero) CloseHandle(processInfo.hProcess);
                    }
                }
                finally
                {
                    if (environment != IntPtr.Zero)
                        DestroyEnvironmentBlock(environment);
                }
            }
            finally
            {
                CloseHandle(primaryToken);
            }
        }
        finally
        {
            CloseHandle(userToken);
        }
    }

    private enum SecurityImpersonationLevel
    {
        SecurityAnonymous,
        SecurityIdentification,
        SecurityImpersonation,
        SecurityDelegation
    }

    private enum TokenType
    {
        TokenPrimary = 1,
        TokenImpersonation
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

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public uint dwProcessId;
        public uint dwThreadId;
    }

    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("Wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSQueryUserToken(uint sessionId, out IntPtr token);

    [DllImport("Wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSLogoffSession(IntPtr serverHandle, uint sessionId, bool wait);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DuplicateTokenEx(
        IntPtr existingToken,
        uint desiredAccess,
        IntPtr tokenAttributes,
        SecurityImpersonationLevel impersonationLevel,
        TokenType tokenType,
        out IntPtr newToken);

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

    [DllImport("userenv.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateEnvironmentBlock(out IntPtr environment, IntPtr token, bool inherit);

    [DllImport("userenv.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyEnvironmentBlock(IntPtr environment);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessAsUser(
        IntPtr token,
        string? applicationName,
        StringBuilder? commandLine,
        IntPtr processAttributes,
        IntPtr threadAttributes,
        bool inheritHandles,
        uint creationFlags,
        IntPtr environment,
        string? currentDirectory,
        ref StartupInfo startupInfo,
        out ProcessInformation processInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
