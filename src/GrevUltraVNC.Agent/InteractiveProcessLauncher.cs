using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace GrevUltraVNC.Agent;

public sealed class InteractiveProcessLauncher
{
    private const uint MaximumAllowed = 0x02000000;
    private const uint CreateUnicodeEnvironment = 0x00000400;
    private const uint CreateNoWindow = 0x08000000;
    private const uint StillActive = 259;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly string BridgeRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "GrevUltraVNC",
        "Screen2Bridge");

    public async Task<DisplaySessionBridgeResult> AttachDisplayAsync(
        int width,
        int height,
        CancellationToken cancellationToken)
    {
        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
            throw new InvalidOperationException("Grev Agent could not resolve its installed executable path for the interactive Screen 2 helper.");

        Directory.CreateDirectory(BridgeRoot);
        var resultPath = Path.Combine(BridgeRoot, $"screen2-{Guid.NewGuid():N}.json");
        File.WriteAllText(resultPath, string.Empty);

        IntPtr userToken = IntPtr.Zero;
        IntPtr primaryToken = IntPtr.Zero;
        IntPtr environment = IntPtr.Zero;
        ProcessInformation processInfo = default;

        try
        {
            var sessionId = WTSGetActiveConsoleSessionId();
            if (sessionId == uint.MaxValue)
                throw new InvalidOperationException("No interactive Windows console session is active on the remote machine.");

            if (!WTSQueryUserToken(sessionId, out userToken))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Grev Agent could not obtain the active Windows user's session token.");

            using (var identity = new WindowsIdentity(userToken))
            {
                var sid = identity.User?.Value;
                if (string.IsNullOrWhiteSpace(sid))
                    throw new InvalidOperationException("Grev Agent could not resolve the active Windows user's SID.");

                GrantAccess(BridgeRoot, sid, "(RX)");
                GrantAccess(resultPath, sid, "(M)");
            }

            if (!DuplicateTokenEx(
                    userToken,
                    MaximumAllowed,
                    IntPtr.Zero,
                    SecurityImpersonationLevel.SecurityImpersonation,
                    TokenType.TokenPrimary,
                    out primaryToken))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Grev Agent could not create an interactive primary token for Screen 2.");
            }

            if (!CreateEnvironmentBlock(out environment, primaryToken, false))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Grev Agent could not create the interactive Windows environment for Screen 2.");

            var startupInfo = new StartupInfo
            {
                cb = Marshal.SizeOf<StartupInfo>(),
                lpDesktop = @"winsta0\default"
            };

            var arguments = $"--display-session-helper \"{resultPath}\" {width} {height}";
            var commandLine = new StringBuilder($"\"{executablePath}\" {arguments}");

            if (!CreateProcessAsUser(
                    primaryToken,
                    executablePath,
                    commandLine,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    false,
                    CreateUnicodeEnvironment | CreateNoWindow,
                    environment,
                    Path.GetDirectoryName(executablePath),
                    ref startupInfo,
                    out processInfo))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows refused to launch the Grev Screen 2 helper in the active user session.");
            }

            var deadline = DateTime.UtcNow.AddSeconds(40);
            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!GetExitCodeProcess(processInfo.hProcess, out var exitCode))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Grev Agent could not read the Screen 2 helper process state.");

                if (exitCode != StillActive)
                    break;

                await Task.Delay(150, cancellationToken);
            }

            if (!GetExitCodeProcess(processInfo.hProcess, out var finalExitCode))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Grev Agent could not read the final Screen 2 helper exit code.");

            if (finalExitCode == StillActive)
            {
                TerminateProcess(processInfo.hProcess, 124);
                throw new TimeoutException("The interactive Screen 2 helper did not finish within 40 seconds.");
            }

            var json = File.ReadAllText(resultPath).Trim();
            if (string.IsNullOrWhiteSpace(json))
                throw new InvalidOperationException($"The interactive Screen 2 helper exited with code {finalExitCode} without returning display information.");

            var result = JsonSerializer.Deserialize<DisplaySessionBridgeResult>(json, JsonOptions)
                ?? throw new InvalidOperationException("The interactive Screen 2 helper returned an empty result.");

            return result;
        }
        finally
        {
            if (processInfo.hThread != IntPtr.Zero) CloseHandle(processInfo.hThread);
            if (processInfo.hProcess != IntPtr.Zero) CloseHandle(processInfo.hProcess);
            if (environment != IntPtr.Zero) DestroyEnvironmentBlock(environment);
            if (primaryToken != IntPtr.Zero) CloseHandle(primaryToken);
            if (userToken != IntPtr.Zero) CloseHandle(userToken);

            try { File.Delete(resultPath); } catch { }
        }
    }

    private static void GrantAccess(string path, string sid, string rights)
    {
        var icacls = Path.Combine(Environment.SystemDirectory, "icacls.exe");
        var startInfo = new ProcessStartInfo
        {
            FileName = icacls,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add(path);
        startInfo.ArgumentList.Add("/grant");
        startInfo.ArgumentList.Add($"*{sid}:{rights}");

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Grev Agent could not start icacls for the Screen 2 bridge.");
        process.WaitForExit(10000);
        if (!process.HasExited)
        {
            try { process.Kill(true); } catch { }
            throw new TimeoutException("Timed out securing the Screen 2 bridge file.");
        }

        if (process.ExitCode != 0)
        {
            var error = process.StandardError.ReadToEnd().Trim();
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error)
                ? $"icacls could not grant the active Windows user access to {path}."
                : error);
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
        StringBuilder commandLine,
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
    private static extern bool GetExitCodeProcess(IntPtr processHandle, out uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateProcess(IntPtr processHandle, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}