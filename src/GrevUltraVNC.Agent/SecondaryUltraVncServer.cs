using System.ComponentModel;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace GrevUltraVNC.Agent;

public sealed class SecondaryUltraVncServer : IDisposable
{
    private const string UltraVncServiceName = "uvnc_service";
    private const uint MaximumAllowed = 0x02000000;
    private const uint CreateUnicodeEnvironment = 0x00000400;
    private const uint DetachedProcess = 0x00000008;
    private const int UltraVncPasswordSize = 8;

    private readonly AgentConfiguration _configuration;
    private Process? _process;
    private string? _configPath;

    public SecondaryUltraVncServer(AgentConfiguration configuration)
    {
        _configuration = configuration;
    }

    public int Port => _configuration.UltraVncPort < 65535
        ? _configuration.UltraVncPort + 1
        : throw new InvalidOperationException("Screen 2 needs a second VNC port, but the primary VNC port is already 65535.");

    public async Task<int> StartAsync(CancellationToken cancellationToken)
    {
        Stop();

        var serviceImagePath = ReadServiceImagePath();
        var serverPath = FindServerPath(serviceImagePath)
            ?? throw new InvalidOperationException("Grev could not find winvnc.exe on the target PC.");
        var sourceConfig = FindPrimaryConfig(serverPath, serviceImagePath)
            ?? throw new InvalidOperationException("Grev found UltraVNC Server but could not find the configuration used by the primary UltraVNC service.");

        var directory = Path.Combine(AgentConfiguration.DataDirectory, "Screen2Server");
        Directory.CreateDirectory(directory);
        _configPath = Path.Combine(directory, "ultravnc-screen2.ini");
        File.Copy(sourceConfig, _configPath, true);

        if (!HasStoredVncPassword(_configPath))
        {
            throw new InvalidOperationException(
                $"Grev found the UltraVNC configuration at '{sourceConfig}', but it does not contain a valid stored VNC password. " +
                "The primary UltraVNC service may be using a different configuration file.");
        }

        // UltraVNC stores passwd/passwd2 with WritePrivateProfileStruct rather than as ordinary
        // text values. Never rewrite the whole INI with File.WriteAllLines: doing so can invalidate
        // those structured password entries. Change only the settings we own using the same Win32
        // profile API family UltraVNC itself uses.
        SetIniValue(_configPath, "admin", "SocketConnect", "1");
        SetIniValue(_configPath, "admin", "AutoPortSelect", "0");
        SetIniValue(_configPath, "admin", "PortNumber", Port.ToString());
        SetIniValue(_configPath, "admin", "HTTPConnect", "0");

        if (!HasStoredVncPassword(_configPath))
        {
            throw new InvalidOperationException(
                "Grev could not preserve UltraVNC's stored password while preparing the temporary Screen 2 server configuration.");
        }

        // This process is already launched inside the logged-in user's interactive desktop.
        // Use UltraVNC's normal app mode so Screen 2 is fully independent from the primary
        // UltraVNC service worker and its global service-session signalling.
        _process = LaunchInActiveSession(
            serverPath,
            $"-config \"{_configPath}\" -multi -run");

        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsListening(Port))
                return Port;
            if (_process.HasExited)
                throw new InvalidOperationException($"The Screen 2 UltraVNC server exited with code {_process.ExitCode} before TCP {Port} became available.");
            await Task.Delay(250, cancellationToken);
        }

        throw new TimeoutException($"The Screen 2 UltraVNC server did not begin listening on TCP {Port}.");
    }

    public void Stop()
    {
        if (_process is not null)
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(true);
                    _process.WaitForExit(5000);
                }
            }
            catch { }
            finally
            {
                _process.Dispose();
                _process = null;
            }
        }

        if (!string.IsNullOrWhiteSpace(_configPath))
        {
            try { File.Delete(_configPath); } catch { }
            _configPath = null;
        }
    }

    private static string? ReadServiceImagePath()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{UltraVncServiceName}");
            return key?.GetValue("ImagePath")?.ToString();
        }
        catch
        {
            return null;
        }
    }

    private static string? FindServerPath(string? serviceImagePath)
    {
        var parsed = ExtractExecutablePath(serviceImagePath);
        if (!string.IsNullOrWhiteSpace(parsed) && File.Exists(parsed))
            return parsed;

        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "uvnc bvba", "UltraVNC", "winvnc.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "UltraVNC", "winvnc.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "uvnc bvba", "UltraVNC", "winvnc.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "UltraVNC", "winvnc.exe")
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static string? FindPrimaryConfig(string serverPath, string? serviceImagePath)
    {
        var candidates = new List<string>();

        var explicitConfig = ExtractConfigPath(serviceImagePath);
        if (!string.IsNullOrWhiteSpace(explicitConfig))
        {
            explicitConfig = Environment.ExpandEnvironmentVariables(explicitConfig);
            if (!Path.IsPathRooted(explicitConfig))
                explicitConfig = Path.GetFullPath(explicitConfig, Path.GetDirectoryName(serverPath) ?? Environment.CurrentDirectory);
            candidates.Add(explicitConfig);
        }

        var serverDirectory = Path.GetDirectoryName(serverPath) ?? string.Empty;
        var portableMarker = Path.Combine(serverDirectory, "ultravnc.portable");
        if (File.Exists(portableMarker))
            candidates.Add(Path.Combine(serverDirectory, "ultravnc.ini"));

        // UltraVNC 1.8.x admin/service mode normally uses ProgramData. Include the known legacy
        // locations too, then prefer whichever existing config actually contains a VNC password.
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        candidates.Add(Path.Combine(programData, "UltraVNC", "ultravnc.ini"));
        candidates.Add(Path.Combine(programData, "uvnc bvba", "UltraVNC", "ultravnc.ini"));
        candidates.Add(Path.Combine(serverDirectory, "ultravnc.ini"));

        var existing = candidates
            .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return existing.FirstOrDefault(HasStoredVncPassword)
            ?? existing.FirstOrDefault();
    }

    private static bool HasStoredVncPassword(string path)
    {
        try
        {
            var password = new byte[UltraVncPasswordSize];
            return GetPrivateProfileStruct(
                       "UltraVNC",
                       "passwd",
                       password,
                       (uint)password.Length,
                       path) &&
                   password.Any(value => value != 0);
        }
        catch
        {
            return false;
        }
    }

    private static string? ExtractExecutablePath(string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath)) return null;
        var value = Environment.ExpandEnvironmentVariables(imagePath.Trim());
        if (value.StartsWith('"'))
        {
            var end = value.IndexOf('"', 1);
            return end > 1 ? value[1..end] : null;
        }

        var exe = value.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return exe >= 0 ? value[..(exe + 4)].Trim() : null;
    }

    private static string? ExtractConfigPath(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine)) return null;
        var index = commandLine.IndexOf("-config", StringComparison.OrdinalIgnoreCase);
        if (index < 0) return null;

        index += "-config".Length;
        while (index < commandLine.Length && char.IsWhiteSpace(commandLine[index])) index++;
        if (index >= commandLine.Length) return null;

        if (commandLine[index] == '"')
        {
            var end = commandLine.IndexOf('"', index + 1);
            return end > index + 1 ? commandLine[(index + 1)..end] : null;
        }

        var start = index;
        while (index < commandLine.Length && !char.IsWhiteSpace(commandLine[index])) index++;
        return index > start ? commandLine[start..index] : null;
    }

    private static Process LaunchInActiveSession(string executablePath, string arguments)
    {
        var sessionId = WTSGetActiveConsoleSessionId();
        if (sessionId == uint.MaxValue)
            throw new InvalidOperationException("No interactive Windows console session is active on the target PC.");

        IntPtr userToken = IntPtr.Zero;
        IntPtr primaryToken = IntPtr.Zero;
        IntPtr environment = IntPtr.Zero;
        ProcessInformation processInfo = default;

        try
        {
            if (!WTSQueryUserToken(sessionId, out userToken))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Grev Agent could not obtain the active Windows user's session token for Screen 2.");

            if (!DuplicateTokenEx(
                    userToken,
                    MaximumAllowed,
                    IntPtr.Zero,
                    SecurityImpersonationLevel.SecurityImpersonation,
                    TokenType.TokenPrimary,
                    out primaryToken))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Grev Agent could not create a primary token for the Screen 2 UltraVNC server.");

            if (!CreateEnvironmentBlock(out environment, primaryToken, false))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Grev Agent could not create the active user's environment for Screen 2.");

            var startupInfo = new StartupInfo
            {
                cb = Marshal.SizeOf<StartupInfo>(),
                lpDesktop = @"winsta0\default"
            };
            var commandLine = new StringBuilder($"\"{executablePath}\" {arguments}");

            if (!CreateProcessAsUser(
                    primaryToken,
                    executablePath,
                    commandLine,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    false,
                    CreateUnicodeEnvironment | DetachedProcess,
                    environment,
                    Path.GetDirectoryName(executablePath),
                    ref startupInfo,
                    out processInfo))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows refused to launch the Screen 2 UltraVNC server in the active user session.");

            return Process.GetProcessById(checked((int)processInfo.dwProcessId));
        }
        finally
        {
            if (processInfo.hThread != IntPtr.Zero) CloseHandle(processInfo.hThread);
            if (processInfo.hProcess != IntPtr.Zero) CloseHandle(processInfo.hProcess);
            if (environment != IntPtr.Zero) DestroyEnvironmentBlock(environment);
            if (primaryToken != IntPtr.Zero) CloseHandle(primaryToken);
            if (userToken != IntPtr.Zero) CloseHandle(userToken);
        }
    }

    private static void SetIniValue(string path, string section, string key, string value)
    {
        if (!WritePrivateProfileString(section, key, value, path))
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Grev could not set UltraVNC option {section}/{key} for Screen 2.");
    }

    private static bool IsListening(int port)
    {
        try
        {
            return IPGlobalProperties.GetIPGlobalProperties()
                .GetActiveTcpListeners()
                .Any(endpoint => endpoint.Port == port);
        }
        catch
        {
            return false;
        }
    }

    public void Dispose() => Stop();

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

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WritePrivateProfileString(
        string section,
        string key,
        string value,
        string filePath);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetPrivateProfileStruct(
        string section,
        string key,
        [Out] byte[] buffer,
        uint size,
        string filePath);

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
    private static extern bool CloseHandle(IntPtr handle);
}
