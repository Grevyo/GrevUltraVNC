using System.Diagnostics;

namespace GrevUltraVNC.Services;

public sealed class PowerService
{
    public Task<(bool Success, string Message)> RestartAsync(string target) => RunAsync(target, restart: true);
    public Task<(bool Success, string Message)> ShutdownAsync(string target) => RunAsync(target, restart: false);

    private static async Task<(bool Success, string Message)> RunAsync(string target, bool restart)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "shutdown.exe",
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("/m");
        psi.ArgumentList.Add($"\\\\{target}");
        psi.ArgumentList.Add(restart ? "/r" : "/s");
        psi.ArgumentList.Add("/t");
        psi.ArgumentList.Add("0");
        psi.ArgumentList.Add("/f");

        using var process = Process.Start(psi);
        if (process is null) return (false, "Windows could not start shutdown.exe.");

        var errorTask = process.StandardError.ReadToEndAsync();
        var outputTask = process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        var error = await errorTask;
        var output = await outputTask;

        return process.ExitCode == 0
            ? (true, restart ? "Restart command sent." : "Shutdown command sent.")
            : (false, string.IsNullOrWhiteSpace(error) ? output.Trim() : error.Trim());
    }
}
