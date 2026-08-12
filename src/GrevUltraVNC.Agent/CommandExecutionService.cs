using System.Diagnostics;
using System.Text;
using GrevUltraVNC.Contracts;

namespace GrevUltraVNC.Agent;

public sealed class CommandExecutionService
{
    private const int MaxCommandCharacters = 8192;
    private const int MaxOutputCharacters = 200_000;

    public async Task<AgentCommandResponse> ExecuteAsync(
        AgentCommandRequest request,
        CancellationToken cancellationToken = default)
    {
        var command = request.Command?.Trim();
        if (string.IsNullOrWhiteSpace(command))
            return Failed("No command was supplied.");

        if (command.Length > MaxCommandCharacters)
            return Failed($"Command is too long. Maximum length is {MaxCommandCharacters} characters.");

        var shell = request.Shell?.Trim().ToLowerInvariant();
        var startInfo = new ProcessStartInfo
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System)
        };

        switch (shell)
        {
            case "powershell":
                startInfo.FileName = "powershell.exe";
                startInfo.ArgumentList.Add("-NoLogo");
                startInfo.ArgumentList.Add("-NoProfile");
                startInfo.ArgumentList.Add("-NonInteractive");
                startInfo.ArgumentList.Add("-Command");
                startInfo.ArgumentList.Add(command);
                break;

            case "cmd":
                startInfo.FileName = "cmd.exe";
                startInfo.ArgumentList.Add("/d");
                startInfo.ArgumentList.Add("/s");
                startInfo.ArgumentList.Add("/c");
                startInfo.ArgumentList.Add(command);
                break;

            default:
                return Failed("Unsupported shell. Choose PowerShell or CMD.");
        }

        var timeoutSeconds = Math.Clamp(request.TimeoutSeconds, 1, 30);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var process = new Process { StartInfo = startInfo };
            if (!process.Start())
                return Failed("Windows did not start the command process.");

            var stdoutTask = ReadCappedAsync(process.StandardOutput, MaxOutputCharacters);
            var stderrTask = ReadCappedAsync(process.StandardError, MaxOutputCharacters);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

            var timedOut = false;
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                timedOut = true;
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Best effort. The request still returns as timed out.
                }
            }

            if (cancellationToken.IsCancellationRequested)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                cancellationToken.ThrowIfCancellationRequested();
            }

            var output = await stdoutTask;
            var error = await stderrTask;
            stopwatch.Stop();

            var exitCode = process.HasExited ? process.ExitCode : -1;
            if (timedOut)
            {
                error = string.IsNullOrWhiteSpace(error)
                    ? $"Command exceeded the {timeoutSeconds}-second Grev Agent timeout."
                    : error + Environment.NewLine + $"Command exceeded the {timeoutSeconds}-second Grev Agent timeout.";
            }

            return new AgentCommandResponse(
                !timedOut && exitCode == 0,
                exitCode,
                output,
                error,
                timedOut,
                stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new AgentCommandResponse(false, -1, string.Empty, ex.Message, false, stopwatch.ElapsedMilliseconds);
        }
    }

    private static async Task<string> ReadCappedAsync(StreamReader reader, int maxCharacters)
    {
        var result = new StringBuilder(Math.Min(maxCharacters, 8192));
        var buffer = new char[4096];
        var truncated = false;

        while (true)
        {
            var read = await reader.ReadAsync(buffer, 0, buffer.Length);
            if (read <= 0) break;

            var remaining = maxCharacters - result.Length;
            if (remaining > 0)
                result.Append(buffer, 0, Math.Min(read, remaining));

            if (read > remaining)
                truncated = true;
        }

        if (truncated)
            result.AppendLine().Append("[output truncated by Grev Agent]");

        return result.ToString();
    }

    private static AgentCommandResponse Failed(string message) =>
        new(false, -1, string.Empty, message, false, 0);
}
