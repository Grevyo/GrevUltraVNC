using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using GrevUltraVNC.Contracts;
using GrevUltraVNC.Models;

namespace GrevUltraVNC.Services;

public sealed record GrevAgentProbeResult(
    GrevAgentState State,
    AgentStatusResponse? Status = null,
    string? Message = null,
    string? ConnectId = null,
    int ProtocolVersion = 0);

public sealed class GrevAgentClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
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
            Timeout = Timeout.InfiniteTimeSpan
        };
    }

    public async Task<GrevAgentProbeResult> ProbeAsync(Machine machine, CancellationToken cancellationToken = default)
    {
        using var timeout = CreateTimeout(cancellationToken, TimeSpan.FromSeconds(3));
        var token = timeout.Token;

        try
        {
            var probeAddress = machine.ActiveAddress;
            if (string.IsNullOrWhiteSpace(probeAddress))
                return new GrevAgentProbeResult(GrevAgentState.NotDetected, Message: "No LAN or Grev Connect route is currently resolved for this machine.");

            var root = $"http://{probeAddress}:{machine.AgentPort}";
            using var ping = await _httpClient.GetAsync(root + AgentProtocol.PingPath, token);
            if (!ping.IsSuccessStatusCode)
                return new GrevAgentProbeResult(GrevAgentState.NotDetected, Message: $"Agent ping returned HTTP {(int)ping.StatusCode}.");

            var pingBody = await ping.Content.ReadFromJsonAsync<AgentPingResponse>(cancellationToken: token);
            if (pingBody is null || !string.Equals(pingBody.Product, "GrevUltraVNC Agent", StringComparison.OrdinalIgnoreCase))
                return new GrevAgentProbeResult(GrevAgentState.NotDetected, Message: "The service on the agent port is not GrevUltraVNC Agent.");

            if (!string.IsNullOrWhiteSpace(machine.ConnectId) &&
                !string.IsNullOrWhiteSpace(pingBody.ConnectId) &&
                !GrevConnectId.Equals(machine.ConnectId, pingBody.ConnectId))
            {
                return new GrevAgentProbeResult(
                    GrevAgentState.NotDetected,
                    Message: $"This route belongs to {pingBody.ConnectId}, not {machine.ConnectId}.",
                    ConnectId: pingBody.ConnectId,
                    ProtocolVersion: pingBody.ProtocolVersion);
            }

            if (string.IsNullOrWhiteSpace(machine.ConnectId) && !string.IsNullOrWhiteSpace(pingBody.ConnectId))
            {
                if (string.IsNullOrWhiteSpace(machine.ResolvedAddress))
                {
                    machine.ResolvedAddress = probeAddress;
                    machine.ResolvedRoute = string.Equals(probeAddress, machine.IpAddress, StringComparison.OrdinalIgnoreCase)
                        ? "LAN"
                        : "Grev Connect";
                }
                machine.ConnectId = pingBody.ConnectId;
            }

            if (!_credentials.TryRead(machine.Id, out var sharedKey))
                return new GrevAgentProbeResult(
                    GrevAgentState.ReadyToPair,
                    Message: "Grev Agent detected. Add its pairing key in Edit Machine.",
                    ConnectId: pingBody.ConnectId,
                    ProtocolVersion: pingBody.ProtocolVersion);

            var status = await GetAuthenticatedAsync<AgentStatusResponse>(machine, sharedKey, AgentProtocol.StatusPath, token);
            var message = pingBody.ProtocolVersion < AgentProtocol.ProtocolVersion
                ? $"Grev Agent is connected but should be updated (protocol {pingBody.ProtocolVersion} → {AgentProtocol.ProtocolVersion})."
                : null;

            return status is null
                ? new GrevAgentProbeResult(GrevAgentState.Error, Message: "Grev Agent returned an empty status response.", ConnectId: pingBody.ConnectId, ProtocolVersion: pingBody.ProtocolVersion)
                : new GrevAgentProbeResult(GrevAgentState.Connected, status, message, pingBody.ConnectId, pingBody.ProtocolVersion);
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

    public Task<AgentActionResponse> EndProcessAsync(Machine machine, int processId, CancellationToken cancellationToken = default) =>
        PostRequiredAuthenticatedAsync<AgentProcessActionRequest, AgentActionResponse>(
            machine,
            AgentProtocol.ProcessActionPath,
            new AgentProcessActionRequest(processId, "terminate"),
            cancellationToken);

    public Task<AgentActionResponse> ControlServiceAsync(Machine machine, string serviceName, string action, CancellationToken cancellationToken = default) =>
        PostRequiredAuthenticatedAsync<AgentServiceActionRequest, AgentActionResponse>(
            machine,
            AgentProtocol.ServiceActionPath,
            new AgentServiceActionRequest(serviceName, action),
            cancellationToken);

    public Task<AgentActionResponse> RunQuickActionAsync(Machine machine, string action, CancellationToken cancellationToken = default) =>
        PostRequiredAuthenticatedAsync<AgentQuickActionRequest, AgentActionResponse>(
            machine,
            AgentProtocol.QuickActionPath,
            new AgentQuickActionRequest(action),
            cancellationToken);

    public Task<AgentIdentityResponse> SetConnectIdAsync(Machine machine, string connectId, CancellationToken cancellationToken = default) =>
        PostRequiredAuthenticatedAsync<AgentIdentityRequest, AgentIdentityResponse>(
            machine,
            AgentProtocol.IdentityPath,
            new AgentIdentityRequest(connectId),
            cancellationToken);

    public Task<AgentCommandResponse> RunCommandAsync(
        Machine machine,
        string shell,
        string command,
        int timeoutSeconds = 30,
        CancellationToken cancellationToken = default) =>
        PostEncryptedAsync<AgentCommandRequest, AgentCommandResponse>(
            machine,
            AgentProtocol.CommandPath,
            new AgentCommandRequest(shell, command, timeoutSeconds),
            TimeSpan.FromSeconds(45),
            "encrypted terminal",
            cancellationToken);

    public Task<AgentFileResponse> RunFileRequestAsync(
        Machine machine,
        AgentFileRequest fileRequest,
        CancellationToken cancellationToken = default) =>
        PostEncryptedAsync<AgentFileRequest, AgentFileResponse>(
            machine,
            AgentProtocol.FilesPath,
            fileRequest,
            TimeSpan.FromMinutes(10),
            "native file management",
            cancellationToken);

    public Task<AgentCollaborationResponse> RunCollaborationAsync(
        Machine machine,
        AgentCollaborationRequest collaborationRequest,
        CancellationToken cancellationToken = default) =>
        PostEncryptedAsync<AgentCollaborationRequest, AgentCollaborationResponse>(
            machine,
            AgentProtocol.CollaborationPath,
            collaborationRequest,
            TimeSpan.FromSeconds(8),
            "Grev Collaboration",
            cancellationToken);

    public Task<AgentAudioResponse> RunAudioAsync(
        Machine machine,
        AgentAudioRequest audioRequest,
        CancellationToken cancellationToken = default) =>
        PostEncryptedAsync<AgentAudioRequest, AgentAudioResponse>(
            machine,
            AgentProtocol.AudioPath,
            audioRequest,
            TimeSpan.FromSeconds(8),
            "remote computer audio",
            cancellationToken);

    private async Task<TResponse> PostEncryptedAsync<TRequest, TResponse>(
        Machine machine,
        string path,
        TRequest payload,
        TimeSpan timeoutDuration,
        string featureName,
        CancellationToken cancellationToken)
    {
        if (!_credentials.TryRead(machine.Id, out var sharedKey))
            throw new InvalidOperationException("This machine has no saved Grev Agent pairing key. Open Edit Machine and pair the Agent first.");

        var encrypted = AgentPayloadCrypto.Encrypt(sharedKey, payload);
        var body = JsonSerializer.SerializeToUtf8Bytes(encrypted, JsonOptions);

        using var request = CreateAuthenticatedRequest(
            HttpMethod.Post,
            Root(machine) + path,
            sharedKey,
            path,
            body);
        using var timeout = CreateTimeout(cancellationToken, timeoutDuration);
        using var response = await _httpClient.SendAsync(request, timeout.Token);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new AgentAuthenticationException();

        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new InvalidOperationException($"This Grev Agent is too old for {featureName}. Use Update Agent on the target PC.");

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Grev Agent returned HTTP {(int)response.StatusCode} for {path}.");

        var responseEnvelope = await response.Content.ReadFromJsonAsync<AgentEncryptedEnvelope>(cancellationToken: timeout.Token)
                               ?? throw new InvalidOperationException($"Grev Agent returned an empty encrypted {featureName} response.");

        return AgentPayloadCrypto.Decrypt<TResponse>(sharedKey, responseEnvelope);
    }

    private async Task<T> GetRequiredAuthenticatedAsync<T>(Machine machine, string path, CancellationToken cancellationToken)
    {
        if (!_credentials.TryRead(machine.Id, out var sharedKey))
            throw new InvalidOperationException("This machine has no saved Grev Agent pairing key. Open Edit Machine and pair the Agent first.");

        using var timeout = CreateTimeout(cancellationToken, TimeSpan.FromSeconds(10));
        var value = await GetAuthenticatedAsync<T>(machine, sharedKey, path, timeout.Token);
        return value ?? throw new InvalidOperationException("Grev Agent returned an empty response.");
    }

    private async Task<T> PostRequiredAuthenticatedAsync<TRequest, T>(Machine machine, string path, TRequest payload, CancellationToken cancellationToken)
    {
        if (!_credentials.TryRead(machine.Id, out var sharedKey))
            throw new InvalidOperationException("This machine has no saved Grev Agent pairing key. Open Edit Machine and pair the Agent first.");

        var body = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        using var request = CreateAuthenticatedRequest(HttpMethod.Post, Root(machine) + path, sharedKey, path, body);
        using var timeout = CreateTimeout(cancellationToken, TimeSpan.FromSeconds(45));
        using var response = await _httpClient.SendAsync(request, timeout.Token);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new AgentAuthenticationException();

        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new InvalidOperationException("This Grev Agent is too old for this remote action. Use Update Agent on the target PC.");

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Grev Agent returned HTTP {(int)response.StatusCode} for {path}.");

        var value = await response.Content.ReadFromJsonAsync<T>(cancellationToken: timeout.Token);
        return value ?? throw new InvalidOperationException("Grev Agent returned an empty action response.");
    }

    private async Task<T?> GetAuthenticatedAsync<T>(Machine machine, string sharedKey, string path, CancellationToken cancellationToken)
    {
        using var request = CreateAuthenticatedRequest(HttpMethod.Get, Root(machine) + path, sharedKey, path, ReadOnlySpan<byte>.Empty);
        using var response = await _httpClient.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new AgentAuthenticationException();

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Grev Agent returned HTTP {(int)response.StatusCode} for {path}.");

        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken);
    }

    private static string Root(Machine machine)
    {
        if (string.IsNullOrWhiteSpace(machine.ActiveAddress))
            throw new InvalidOperationException("No network route is currently resolved for this machine.");
        return $"http://{machine.ActiveAddress}:{machine.AgentPort}";
    }

    private static CancellationTokenSource CreateTimeout(CancellationToken cancellationToken, TimeSpan timeout)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        source.CancelAfter(timeout);
        return source;
    }

    private static HttpRequestMessage CreateAuthenticatedRequest(HttpMethod method, string url, string sharedKey, string path, ReadOnlySpan<byte> body)
    {
        var bodyBytes = body.ToArray();
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var signature = AgentProtocol.CreateSignature(sharedKey, timestamp, nonce, method.Method, path, bodyBytes);

        var request = new HttpRequestMessage(method, url);
        request.Headers.TryAddWithoutValidation(AgentProtocol.TimestampHeader, timestamp.ToString(System.Globalization.CultureInfo.InvariantCulture));
        request.Headers.TryAddWithoutValidation(AgentProtocol.NonceHeader, nonce);
        request.Headers.TryAddWithoutValidation(AgentProtocol.SignatureHeader, signature);

        if (bodyBytes.Length > 0)
        {
            request.Content = new ByteArrayContent(bodyBytes);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }

        return request;
    }

    public void Dispose() => _httpClient.Dispose();

    private sealed class AgentAuthenticationException : Exception
    {
    }
}
