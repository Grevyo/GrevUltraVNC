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
            Timeout = TimeSpan.FromSeconds(3)
        };
    }

    public async Task<GrevAgentProbeResult> ProbeAsync(Machine machine, CancellationToken cancellationToken = default)
    {
        var root = Root(machine);

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

            var status = await GetAuthenticatedAsync<AgentStatusResponse>(machine, sharedKey, AgentProtocol.StatusPath, cancellationToken);
            return status is null
                ? new GrevAgentProbeResult(GrevAgentState.Error, Message: "Grev Agent returned an empty status response.")
                : new GrevAgentProbeResult(GrevAgentState.Connected, status);
        }
        catch (AgentAuthenticationException)
        {
            return new GrevAgentProbeResult(GrevAgentState.AuthenticationFailed, Message: "The saved Grev Agent pairing key was rejected.");
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

    public Task<AgentStatusResponse> GetStatusAsync(Machine machine, CancellationToken cancellationToken = default) =>
        GetRequiredAuthenticatedAsync<AgentStatusResponse>(machine, AgentProtocol.StatusPath, cancellationToken);

    public async Task<IReadOnlyList<AgentProcessInfo>> GetProcessesAsync(Machine machine, CancellationToken cancellationToken = default) =>
        await GetRequiredAuthenticatedAsync<AgentProcessInfo[]>(machine, AgentProtocol.ProcessesPath, cancellationToken);

    public async Task<IReadOnlyList<AgentServiceInfo>> GetServicesAsync(Machine machine, CancellationToken cancellationToken = default) =>
        await GetRequiredAuthenticatedAsync<AgentServiceInfo[]>(machine, AgentProtocol.ServicesPath, cancellationToken);

    private async Task<T> GetRequiredAuthenticatedAsync<T>(Machine machine, string path, CancellationToken cancellationToken)
    {
        if (!_credentials.TryRead(machine.Id, out var sharedKey))
            throw new InvalidOperationException("This machine has no saved Grev Agent pairing key. Open Edit Machine and pair the Agent first.");

        var value = await GetAuthenticatedAsync<T>(machine, sharedKey, path, cancellationToken);
        return value ?? throw new InvalidOperationException("Grev Agent returned an empty response.");
    }

    private async Task<T?> GetAuthenticatedAsync<T>(Machine machine, string sharedKey, string path, CancellationToken cancellationToken)
    {
        using var request = CreateAuthenticatedGet(Root(machine) + path, sharedKey, path);
        using var response = await _httpClient.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new AgentAuthenticationException();

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Grev Agent returned HTTP {(int)response.StatusCode} for {path}.");

        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken);
    }

    private static string Root(Machine machine) => $"http://{machine.IpAddress}:{machine.AgentPort}";

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

    private sealed class AgentAuthenticationException : Exception
    {
    }
}
