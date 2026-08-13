using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
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

        var serverPath = FindServerPath()
            ?? throw new InvalidOperationException("Grev could not find winvnc.exe on the target PC.");
        var sourceConfig = FindConfig(serverPath)
            ?? throw new InvalidOperationException("Grev found UltraVNC Server but could not find ultravnc.ini.");

        var directory = Path.Combine(AgentConfiguration.DataDirectory, "Screen2Server");
        Directory.CreateDirectory(directory);
        _configPath = Path.Combine(directory, "ultravnc-screen2.ini");
        File.Copy(sourceConfig, _configPath, true);

        SetIniValue(_configPath, "admin", "SocketConnect", "1");
        SetIniValue(_configPath, "admin", "AutoPortSelect", "0");
        SetIniValue(_configPath, "admin", "PortNumber", Port.ToString());
        SetIniValue(_configPath, "admin", "HTTPConnect", "0");
        SetIniValue(_configPath, "admin", "primary", "0");
        SetIniValue(_configPath, "admin", "secondary", "1");

        var startInfo = new ProcessStartInfo
        {
            FileName = serverPath,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-config");
        startInfo.ArgumentList.Add(_configPath);
        startInfo.ArgumentList.Add("-multi");
        startInfo.ArgumentList.Add("-run");

        _process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Grev could not start the Screen 2 UltraVNC server.");

        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await IsListeningAsync(Port, cancellationToken))
                return Port;
            if (_process.HasExited)
                throw new InvalidOperationException("The Screen 2 UltraVNC server exited before its VNC port became available.");
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

    private static string? FindServerPath()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{UltraVncServiceName}");
            var imagePath = key?.GetValue("ImagePath")?.ToString();
            var parsed = ExtractExecutablePath(imagePath);
            if (!string.IsNullOrWhiteSpace(parsed) && File.Exists(parsed))
                return parsed;
        }
        catch { }

        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "uvnc bvba", "UltraVNC", "winvnc.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "UltraVNC", "winvnc.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "uvnc bvba", "UltraVNC", "winvnc.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "UltraVNC", "winvnc.exe")
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

    private static string? FindConfig(string serverPath)
    {
        var serverDirectory = Path.GetDirectoryName(serverPath) ?? string.Empty;
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var candidates = new[]
        {
            Path.Combine(serverDirectory, "ultravnc.ini"),
            Path.Combine(programData, "UltraVNC", "ultravnc.ini"),
            Path.Combine(programData, "uvnc bvba", "UltraVNC", "ultravnc.ini")
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static void SetIniValue(string path, string section, string key, string value)
    {
        var lines = File.ReadAllLines(path).ToList();
        var sectionHeader = $"[{section}]";
        var sectionIndex = lines.FindIndex(line => string.Equals(line.Trim(), sectionHeader, StringComparison.OrdinalIgnoreCase));
        if (sectionIndex < 0)
        {
            if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[^1])) lines.Add(string.Empty);
            lines.Add(sectionHeader);
            lines.Add($"{key}={value}");
            File.WriteAllLines(path, lines);
            return;
        }

        var end = lines.Count;
        for (var i = sectionIndex + 1; i < lines.Count; i++)
        {
            if (lines[i].TrimStart().StartsWith('['))
            {
                end = i;
                break;
            }
        }

        for (var i = sectionIndex + 1; i < end; i++)
        {
            var equals = lines[i].IndexOf('=');
            if (equals <= 0) continue;
            if (!string.Equals(lines[i][..equals].Trim(), key, StringComparison.OrdinalIgnoreCase)) continue;
            lines[i] = $"{key}={value}";
            File.WriteAllLines(path, lines);
            return;
        }

        lines.Insert(end, $"{key}={value}");
        File.WriteAllLines(path, lines);
    }

    private static async Task<bool> IsListeningAsync(int port, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(500));
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

    public void Dispose() => Stop();
}
