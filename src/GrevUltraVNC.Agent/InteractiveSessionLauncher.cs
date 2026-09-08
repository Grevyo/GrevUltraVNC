using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace GrevUltraVNC.Agent;

/// <summary>
/// Starts a process on the interactive desktop from the Agent's Session 0 service.
///
/// Two callers need this — the quick actions (Explorer restart, workstation lock) and the
/// Screen 2 UltraVNC server — and each used to carry its own copy of the whole
/// WTSQueryUserToken / DuplicateTokenEx / CreateEnvironmentBlock / CreateProcessAsUser
/// sequence, plus the structs and P/Invoke declarations behind it. That is now here once.
/// </summary>
internal static class InteractiveSessionLauncher
{
    /// <summary>Returned by <see cref="GetActiveConsoleSessionId"/> when nobody is signed in.</summary>
    public const uint NoActiveSession = uint.MaxValue;

    private const uint MaximumAllowed = 0x02000000;
    private const uint CreateUnicodeEnvironment = 0x00000400;
    private const uint DetachedProcess = 0x00000008;

    /// <summary>The physical console session, or <see cref="NoActiveSession"/>.</summary>
    public static uint GetActiveConsoleSessionId() => WTSGetActiveConsoleSessionId();

    /// <summary>
    /// Launches <paramref name="executablePath"/> as the user signed in to
    /// <paramref name="sessionId"/> and returns the new process id.
    /// </summary>
    /// <param name="arguments">Command-line arguments, or null to launch with none.</param>
    /// <param name="detached">
    /// True for a long-lived process that must outlive the launch (the Screen 2 server);
    /// false for a fire-and-forget shell action.
    /// </param>
    /// <param name="what">
    /// What is being launched, in lower case, so failures read as full sentences —
    /// e.g. "the Screen 2 UltraVNC server".
    /// </param>
    public static int Launch(
        uint sessionId,
        string executablePath,
        string? arguments,
        bool detached,
        string what)
    {
        var userToken = IntPtr.Zero;
        var primaryToken = IntPtr.Zero;
        var environment = IntPtr.Zero;
        ProcessInformation processInfo = default;

        try
        {
            if (!WTSQueryUserToken(sessionId, out userToken))
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"Grev Agent could not obtain the interactive user's Windows token for {what}.");

            if (!DuplicateTokenEx(
                    userToken,
                    MaximumAllowed,
                    IntPtr.Zero,
                    SecurityImpersonationLevel.SecurityImpersonation,
                    TokenType.TokenPrimary,
                    out primaryToken))
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"Grev Agent could not create an interactive primary token for {what}.");

            if (!CreateEnvironmentBlock(out environment, primaryToken, false))
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"Grev Agent could not create the interactive user's environment for {what}.");

            var startupInfo = new StartupInfo
            {
                cb = Marshal.SizeOf<StartupInfo>(),
                lpDesktop = @"winsta0\default"
            };

            // Passing a command line at all is only needed when there are arguments; with
            // none, CreateProcessAsUser is happy with the application name alone.
            StringBuilder? commandLine = null;
            if (!string.IsNullOrWhiteSpace(arguments))
                commandLine = new StringBuilder($"\"{executablePath}\" {arguments}");

            var creationFlags = CreateUnicodeEnvironment;
            if (detached) creationFlags |= DetachedProcess;

            if (!CreateProcessAsUser(
                    primaryToken,
                    executablePath,
                    commandLine,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    false,
                    creationFlags,
                    environment,
                    Path.GetDirectoryName(executablePath),
                    ref startupInfo,
                    out processInfo))
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"Windows refused to launch {what} in the interactive session.");

            return checked((int)processInfo.dwProcessId);
        }
        finally
        {
            // The launched process owns its own lifetime; the Agent only owns these handles.
            if (processInfo.hThread != IntPtr.Zero) CloseHandle(processInfo.hThread);
            if (processInfo.hProcess != IntPtr.Zero) CloseHandle(processInfo.hProcess);
            if (environment != IntPtr.Zero) DestroyEnvironmentBlock(environment);
            if (primaryToken != IntPtr.Zero) CloseHandle(primaryToken);
            if (userToken != IntPtr.Zero) CloseHandle(userToken);
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
        StringBuilder? commandLine,
        IntPtr processAttributes,
        IntPtr threadAttributes,
        bool inheritHandles,
        uint creationFlags,
        IntPtr environment,
        string? currentDirectory,
        ref StartupInfo startupInfo,
        out ProcessInformation processInformation);

    /// <summary>Also used by callers that hold their own Windows handles.</summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(IntPtr handle);
}
