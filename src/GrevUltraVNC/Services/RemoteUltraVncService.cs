using System.Diagnostics;

namespace GrevUltraVNC.Services;

public sealed record RemoteServiceResult(bool Success, string Message);

public sealed class RemoteUltraVncService
{
    private const string ServiceName = "uvnc_service";

    public async Task<RemoteServiceResult> StartAsync(string host)
    {
        var result = await RunScAsync(host, "start", ServiceName);

        if (result.ExitCode == 0 || result.Text.Contains("1056", StringComparison.OrdinalIgnoreCase))
            return new RemoteServiceResult(true, "UltraVNC service is running.");

        return new RemoteServiceResult(false, FriendlyError(result.Text));
    }

    public async Task<RemoteServiceResult> StopAsync(string host)
    {
        var result = await RunScAsync(host, "stop", ServiceName);

        if (result.ExitCode == 0 || result.Text.Contains("1062", StringComparison.OrdinalIgnoreCase))
            return new RemoteServiceResult(true, "UltraVNC service is stopped.");

        return new RemoteServiceResult(false, FriendlyError(result.Text));
    }

    public async Task<RemoteServiceResult> RestartAsync(string host)
    {
        var stop = await StopAsync(host);
        if (!stop.Success) return stop;

        await Task.Delay(700);
        var start = await StartAsync(host);
        return start.Success
            ? new RemoteServiceResult(true, "UltraVNC service restarted.")
            : start;
    }

    public async Task<RemoteServiceResult> EnableAutoStartAndStartAsync(string host)
    {
        var config = await RunScAsync(host, "config", ServiceName, "start=", "auto");
        if (config.ExitCode != 0)
            return new RemoteServiceResult(false, FriendlyError(config.Text));

        var start = await StartAsync(host);
        if (!start.Success)
            return start;

        return new RemoteServiceResult(true, "UltraVNC is set to start automatically with Windows and is running now.");
    }

    public async Task<RemoteServiceResult> QueryAsync(string host)
    {
        var result = await RunScAsync(host, "query", ServiceName);
        if (result.ExitCode != 0)
            return new RemoteServiceResult(false, FriendlyError(result.Text));

        if (result.Text.Contains("RUNNING", StringComparison.OrdinalIgnoreCase))
            return new RemoteServiceResult(true, "UltraVNC service state: RUNNING");
        if (result.Text.Contains("STOPPED", StringComparison.OrdinalIgnoreCase))
            return new RemoteServiceResult(true, "UltraVNC service state: STOPPED");

        return new RemoteServiceResult(true, result.Text);
    }

    private static async Task<ScResult> RunScAsync(string host, params string[] arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "sc.exe",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        psi.ArgumentList.Add($"\\\\{host}");
        foreach (var argument in arguments)
            psi.ArgumentList.Add(argument);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Could not start Windows Service Controller (sc.exe).");

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        var output = await outputTask;
        var error = await errorTask;
        return new ScResult(process.ExitCode, $"{output}\n{error}".Trim());
    }

    private static string FriendlyError(string text)
    {
        if (text.Contains("1060", StringComparison.OrdinalIgnoreCase))
            return "UltraVNC Server is not installed as the 'uvnc_service' service on the target machine.";

        if (text.Contains("1722", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("RPC server is unavailable", StringComparison.OrdinalIgnoreCase))
            return "The target PC is online, but Windows Remote Service Management is not reachable. Enable the Remote Service Management firewall rules on the target PC.";

        if (text.Contains("FAILED 5", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("Access is denied", StringComparison.OrdinalIgnoreCase))
            return "Access was denied. Run GrevUltraVNC with an administrator account that also has administrative rights on the target PC.";

        return string.IsNullOrWhiteSpace(text)
            ? "Windows could not control the UltraVNC service on the target PC."
            : text;
    }

    private sealed record ScResult(int ExitCode, string Text);
}
