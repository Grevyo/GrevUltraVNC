using System.Text;
using GrevUltraVNC.Models;

namespace GrevUltraVNC.Services;

public sealed class AgentUpdateService
{
    private const string UpdaterScriptPath = @"C:\Windows\Temp\Update-GrevAgent.ps1";
    private const string UpdateStatusPath = @"C:\Windows\Temp\GrevUltraVNC-Agent-Update.status";
    private const string UpdateLogPath = @"C:\Windows\Temp\GrevUltraVNC-Agent-Update.log";
    private const string UpdaterUrl = "https://raw.githubusercontent.com/Grevyo/GrevUltraVNC/main/scripts/update-agent-from-github.ps1";

    private readonly GrevAgentClient _agent;

    public AgentUpdateService(GrevAgentClient agent)
    {
        _agent = agent;
    }

    public async Task<GrevAgentProbeResult> UpdateFromGitHubAsync(
        Machine machine,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        progress?.Report("Checking Grev Agent…");
        var initialProbe = await _agent.ProbeAsync(machine, cancellationToken);
        if (initialProbe.State != GrevAgentState.Connected)
            throw new InvalidOperationException(initialProbe.Message ?? "Grev Agent must be connected before it can update itself.");

        progress?.Report("Starting Agent update from GitHub…");

        var childCommand =
            $"$ErrorActionPreference='Stop'; Start-Sleep -Seconds 3; try {{ & '{UpdaterScriptPath}' *>&1 | Out-File -LiteralPath '{UpdateLogPath}' -Encoding UTF8; ('SUCCESS|' + (Get-Date).ToString('O')) | Set-Content -LiteralPath '{UpdateStatusPath}' -Encoding UTF8 }} catch {{ ('FAILED|' + $_.Exception.Message) | Set-Content -LiteralPath '{UpdateStatusPath}' -Encoding UTF8 }}";
        var encodedChildCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(childCommand));

        var bootstrapCommand =
            "$ErrorActionPreference='Stop'; " +
            $"$u='{UpdaterUrl}'; " +
            $"$p='{UpdaterScriptPath}'; " +
            $"$s='{UpdateStatusPath}'; " +
            $"$l='{UpdateLogPath}'; " +
            "Remove-Item -LiteralPath $s -Force -ErrorAction SilentlyContinue; " +
            "Remove-Item -LiteralPath $l -Force -ErrorAction SilentlyContinue; " +
            "Invoke-WebRequest -UseBasicParsing -Uri $u -OutFile $p; " +
            $"Start-Process -FilePath 'powershell.exe' -WindowStyle Hidden -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-EncodedCommand','{encodedChildCommand}')";

        var launch = await _agent.RunCommandAsync(machine, "powershell", bootstrapCommand, 30, cancellationToken);
        if (!launch.Success)
        {
            var detail = string.IsNullOrWhiteSpace(launch.StandardError)
                ? $"Updater bootstrap exited with code {launch.ExitCode}."
                : launch.StandardError.Trim();
            throw new InvalidOperationException(detail);
        }

        progress?.Report("Agent updater launched · waiting for service restart…");

        var deadline = DateTimeOffset.UtcNow.AddMinutes(2);
        var lastMessage = "Waiting for Grev Agent to return…";

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);

            var probe = await _agent.ProbeAsync(machine, cancellationToken);
            if (probe.State != GrevAgentState.Connected)
            {
                lastMessage = "Agent is restarting…";
                progress?.Report(lastMessage);
                continue;
            }

            progress?.Report("Agent is back · verifying update…");

            try
            {
                var statusCommand = $"if (Test-Path -LiteralPath '{UpdateStatusPath}') {{ Get-Content -LiteralPath '{UpdateStatusPath}' -Raw }} else {{ 'PENDING' }}";
                var status = await _agent.RunCommandAsync(machine, "powershell", statusCommand, 8, cancellationToken);
                var marker = (status.StandardOutput ?? string.Empty).Trim().TrimStart('\uFEFF');

                if (marker.StartsWith("SUCCESS|", StringComparison.OrdinalIgnoreCase))
                {
                    progress?.Report("Grev Agent updated successfully.");
                    return probe;
                }

                if (marker.StartsWith("FAILED|", StringComparison.OrdinalIgnoreCase))
                {
                    var detail = marker["FAILED|".Length..].Trim();
                    throw new InvalidOperationException(string.IsNullOrWhiteSpace(detail)
                        ? "Grev Agent update failed."
                        : detail);
                }

                lastMessage = "Agent is responding · installer still finishing…";
                progress?.Report(lastMessage);
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch
            {
                lastMessage = "Agent is restarting…";
                progress?.Report(lastMessage);
            }
        }

        throw new TimeoutException($"Timed out waiting for the Grev Agent update to finish. {lastMessage}");
    }
}
