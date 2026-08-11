using System.Collections.Concurrent;
using GrevUltraVNC.Contracts;

namespace GrevUltraVNC.Agent;

public sealed class AgentRequestAuthenticator
{
    private const int MaxAuthenticatedBodyBytes = 1024 * 1024;
    private readonly AgentConfiguration _configuration;
    private readonly ConcurrentDictionary<string, long> _usedNonces = new(StringComparer.Ordinal);

    public AgentRequestAuthenticator(AgentConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task<bool> IsAuthorizedAsync(HttpRequest request, CancellationToken cancellationToken = default)
    {
        if (!request.Headers.TryGetValue(AgentProtocol.TimestampHeader, out var timestampValues) ||
            !long.TryParse(timestampValues.FirstOrDefault(), out var timestamp))
            return false;

        if (!request.Headers.TryGetValue(AgentProtocol.NonceHeader, out var nonceValues))
            return false;

        if (!request.Headers.TryGetValue(AgentProtocol.SignatureHeader, out var signatureValues))
            return false;

        var nonce = nonceValues.FirstOrDefault()?.Trim();
        var signature = signatureValues.FirstOrDefault()?.Trim();
        if (string.IsNullOrWhiteSpace(nonce) || nonce.Length is < 16 or > 128 || string.IsNullOrWhiteSpace(signature))
            return false;

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var allowedSeconds = (long)AgentProtocol.AllowedClockSkew.TotalSeconds;
        if (Math.Abs(now - timestamp) > allowedSeconds)
            return false;

        PruneNonces(now - allowedSeconds - 30);
        if (!_usedNonces.TryAdd(nonce, timestamp))
            return false;

        byte[] body;
        if (request.ContentLength is > MaxAuthenticatedBodyBytes)
            return false;

        request.EnableBuffering();
        using (var memory = new MemoryStream())
        {
            await request.Body.CopyToAsync(memory, cancellationToken);
            body = memory.ToArray();
            request.Body.Position = 0;
        }

        var path = request.Path.Value ?? "/";
        var expected = AgentProtocol.CreateSignature(
            _configuration.SharedKey,
            timestamp,
            nonce,
            request.Method,
            path,
            body);

        return AgentProtocol.FixedTimeSignatureEquals(expected, signature);
    }

    private void PruneNonces(long olderThan)
    {
        foreach (var item in _usedNonces)
        {
            if (item.Value < olderThan)
                _usedNonces.TryRemove(item.Key, out _);
        }
    }
}
