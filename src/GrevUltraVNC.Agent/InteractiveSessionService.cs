using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using GrevUltraVNC.Contracts;

namespace GrevUltraVNC.Agent;

public sealed class InteractiveSessionService
{
    private const uint MaximumAllowed = 0x02000000;
    private const uint CreateUnicodeEnvironment = 0x00000400;

    public AgentActionResponse RunQuickAction(AgentQuickActionRequest request)
    {
        var action = request.Action?.Trim().ToLowerInvariant();
        return action switch
        {
            "restart-explorer" => RestartExplorer(),
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

    private static void LaunchInInteractiveSession(uint sessionId, string executablePath)
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

                    if (!CreateProcessAsUser(
                            primaryToken,
                            executablePath,
                            null,
                            IntPtr.Zero,
                            IntPtr.Zero,
                            false,
                            CreateUnicodeEnvironment,
                            environment,
                            Path.GetDirectoryName(executablePath),
                            ref startupInfo,
                            out var processInfo))
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows refused to relaunch Explorer in the interactive session.");

                    try
                    {
                        // Creation succeeded; Explorer now owns its lifetime.
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

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DuplicateTokenEx(
        IntPtr existingToken,
        uint desiredAccess,
        IntPtr tokenAttributes,
        SecurityImpersonationLevel impersonationLevel,
        TokenType tokenType,
        out IntPtr newToken);

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
        string? commandLine,
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
