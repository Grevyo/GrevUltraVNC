using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace GrevUltraVNC.Agent;

public sealed class SecondaryUltraVncServer : IDisposable
{
    private const string UltraVncServiceName = "uvnc_service";

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

        var controllerAddress = FindPrimaryVncControllerAddress();
        var serviceImagePath = ReadServiceImagePath();
        var serverPath = FindServerPath(serviceImagePath)
            ?? throw new InvalidOperationException("Grev could not find winvnc.exe on the target PC.");
        var sourceConfig = FindPrimaryConfig(serverPath, serviceImagePath)
            ?? throw new InvalidOperationException("Grev found UltraVNC Server but could not find the configuration used by the primary UltraVNC service.");

        var directory = Path.Combine(AgentConfiguration.DataDirectory, "Screen2Server");
        Directory.CreateDirectory(directory);
        _configPath = Path.Combine(directory, "ultravnc-screen2.ini");
        File.Copy(sourceConfig, _configPath, true);

        // Screen 2 is a short-lived independent server. It is only created while Screen 1 is
        // already connected. Restrict the temporary listener to the one remote IP that currently
        // owns the established primary UltraVNC connection, then disable password/MS-Logon auth
        // for this isolated instance. Every other host is rejected by UltraVNC's AuthHosts rules.
        SetIniValue(_configPath, "admin", "SocketConnect", "1");
        SetIniValue(_configPath, "admin", "AutoPortSelect", "0");
        SetIniValue(_configPath, "admin", "PortNumber", Port.ToString());
        SetIniValue(_configPath, "admin", "HTTPConnect", "0");
        SetIniValue(_configPath, "admin", "AuthRequired", "0");
        SetIniValue(_configPath, "admin", "MSLogonRequired", "0");
        SetIniValue(_configPath, "admin", "RequireMSLogonIII", "0");
        SetIniValue(_configPath, "admin", "NewMSLogon", "0");
        SetIniValue(_configPath, "admin", "UseDSMPlugin", "0");
        SetIniValue(_configPath, "admin", "QuerySetting", "2");
        SetIniValue(_configPath, "admin", "QueryIfNoLogon", "0");
        SetIniValue(_configPath, "admin", "AuthHosts", $"-:+{controllerAddress}:");

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

    private string FindPrimaryVncControllerAddress()
    {
        try
        {
            var addresses = IPGlobalProperties.GetIPGlobalProperties()
                .GetActiveTcpConnections()
                .Where(connection =>
                    connection.State == TcpState.Established &&
                    connection.LocalEndPoint.Port == _configuration.UltraVncPort)
                .Select(connection => NormalizeAddress(connection.RemoteEndPoint.Address))
                .Where(address => address is not null)
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return addresses.Count switch
            {
                1 => addresses[0],
                0 => throw new InvalidOperationException(
                    $"Screen 2 could not find an established Screen 1 UltraVNC connection on TCP {_configuration.UltraVncPort}. Open Screen 1 first, then create Screen 2."),
                _ => throw new InvalidOperationException(
                    "Screen 2 found more than one remote IP connected to the primary UltraVNC server, so Grev cannot safely decide which host should be allowed onto the temporary Screen 2 server.")
            };
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Grev could not identify the current Screen 1 controller address for Screen 2.", ex);
        }
    }

    private static string? NormalizeAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();
        return IPAddress.IsLoopback(address) ? null : address.ToString();
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
        var explicitConfig = ExtractConfigPath(serviceImagePath);
        if (!string.IsNullOrWhiteSpace(explicitConfig))
        {
            explicitConfig = Environment.ExpandEnvironmentVariables(explicitConfig);
            if (!Path.IsPathRooted(explicitConfig))
                explicitConfig = Path.GetFullPath(explicitConfig, Path.GetDirectoryName(serverPath) ?? Environment.CurrentDirectory);
            if (File.Exists(explicitConfig))
                return explicitConfig;
        }

        var serverDirectory = Path.GetDirectoryName(serverPath) ?? string.Empty;
        var portableMarker = Path.Combine(serverDirectory, "ultravnc.portable");
        if (File.Exists(portableMarker))
        {
            var portableConfig = Path.Combine(serverDirectory, "ultravnc.ini");
            if (File.Exists(portableConfig))
                return portableConfig;
        }

        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var candidates = new[]
        {
            Path.Combine(programData, "UltraVNC", "ultravnc.ini"),
            Path.Combine(programData, "uvnc bvba", "UltraVNC", "ultravnc.ini"),
            Path.Combine(serverDirectory, "ultravnc.ini")
        };
        return candidates.FirstOrDefault(File.Exists);
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
        var sessionId = InteractiveSessionLauncher.GetActiveConsoleSessionId();
        if (sessionId == InteractiveSessionLauncher.NoActiveSession)
            throw new InvalidOperationException("No interactive Windows console session is active on the target PC.");

        var processId = InteractiveSessionLauncher.Launch(
            sessionId,
            executablePath,
            arguments,
            detached: true,
            what: "the Screen 2 UltraVNC server");

        return Process.GetProcessById(processId);
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

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WritePrivateProfileString(
        string section,
        string key,
        string value,
        string filePath);
}