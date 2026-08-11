using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using GrevUltraVNC.Contracts;
using GrevUltraVNC.Models;

namespace GrevUltraVNC.Services;

public sealed record GrevAgentProbeResult(
    GrevAgentState State,
    AgentStatusResponse? Status = null,
    string? Message = null);

public sealed class GrevAgentClient : IDisposable
{
    private readonly AgentCredentialService _credentials = new();
    private readonly HttpClient _httpClient;

    public GrevAgentClient()
    {
        var handler = new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromMilliseconds(900),
            UseProxy = false
        };

        _httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(2)
        };
    }

    public async Task<GrevAgentProbeResult> ProbeAsync(Machine machine, CancellationToken cancellationToken = default)
    {
        var root = $"http://{machine.IpAddress}:{machine.AgentPort}";

        try
        {
            using var ping = await _httpClient.GetAsync(root + AgentProtocol.PingPath, cancellationToken);
            if (!ping.IsSuccessStatusCode)
                return new GrevAgentProbeResult(GrevAgentState.NotDetected, Message: $"Agent ping returned HTTP {(int)ping.StatusCode}.");

            var pingBody = await ping.Content.ReadFromJsonAsync<AgentPingResponse>(cancellationToken: cancellationToken);
            if (pingBody is null || !string.Equals(pingBody.Product, "GrevUltraVNC Agent", StringComparison.OrdinalIgnoreCase))
                return new GrevAgentProbeResult(GrevAgentState.NotDetected, Message: "The service on the agent port is not GrevUltraVNC Agent.");

            if (!_credentials.TryRead(machine.Id, out var sharedKey))
                return new GrevAgentProbeResult(GrevAgentState.ReadyToPair, Message: "Grev Agent detected. Add its pairing key in Edit Machine.");

            using var request = CreateAuthenticatedGet(root + AgentProtocol.StatusPath, sharedKey, AgentProtocol.StatusPath);
            using var response = await _httpClient.SendAsync(request, cancellationToken);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
                return new GrevAgentProbeResult(GrevAgentState.AuthenticationFailed, Message: "The saved Grev Agent pairing key was rejected.");

            if (!response.IsSuccessStatusCode)
                return new GrevAgentProbeResult(GrevAgentState.Error, Message: $"Agent status returned HTTP {(int)response.StatusCode}.");

            var status = await response.Content.ReadFromJsonAsync<AgentStatusResponse>(cancellationToken: cancellationToken);
            return status is null
                ? new GrevAgentProbeResult(GrevAgentState.Error, Message: "Grev Agent returned an empty status response.")
                : new GrevAgentProbeResult(GrevAgentState.Connected, status);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new GrevAgentProbeResult(GrevAgentState.NotDetected, Message: "Grev Agent did not respond in time.");
        }
        catch (HttpRequestException ex)
        {
            return new GrevAgentProbeResult(GrevAgentState.NotDetected, Message: ex.Message);
        }
        catch (Exception ex)
        {
            return new GrevAgentProbeResult(GrevAgentState.Error, Message: ex.Message);
        }
    }

    private static HttpRequestMessage CreateAuthenticatedGet(string url, string sharedKey, string path)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var signature = AgentProtocol.CreateSignature(sharedKey, timestamp, nonce, HttpMethod.Get.Method, path, ReadOnlySpan<byte>.Empty);

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation(AgentProtocol.TimestampHeader, timestamp.ToString(System.Globalization.CultureInfo.InvariantCulture));
        request.Headers.TryAddWithoutValidation(AgentProtocol.NonceHeader, nonce);
        request.Headers.TryAddWithoutValidation(AgentProtocol.SignatureHeader, signature);
        return request;
    }

    public void Dispose() => _httpClient.Dispose();
}
