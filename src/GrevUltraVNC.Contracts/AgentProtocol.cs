using System.Security.Cryptography;
using System.Text;

namespace GrevUltraVNC.Contracts;

public static class AgentProtocol
{
    public const int ProtocolVersion = 13;
    public const int DefaultPort = 47820;
    public const string PingPath = "/api/v1/ping";
    public const string StatusPath = "/api/v1/status";
    public const string ProcessesPath = "/api/v1/processes";
    public const string ServicesPath = "/api/v1/services";
    public const string ProcessActionPath = "/api/v1/process/action";
    public const string ServiceActionPath = "/api/v1/service/action";
    public const string QuickActionPath = "/api/v1/quick-action";
    public const string CommandPath = "/api/v1/command";
    public const string FilesPath = "/api/v1/files";
    public const string IdentityPath = "/api/v1/identity";
    public const string CollaborationPath = "/api/v1/collaboration";
    public const string AudioPath = "/api/v1/audio";
    public const string DisplayPath = "/api/v1/display";
    public const string TimestampHeader = "X-Grev-Timestamp";
    public const string NonceHeader = "X-Grev-Nonce";
    public const string SignatureHeader = "X-Grev-Signature";
    public static readonly TimeSpan AllowedClockSkew = TimeSpan.FromSeconds(90);

    public static string CreateSharedKey() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    public static bool IsValidSharedKey(string? sharedKey)
    {
        if (string.IsNullOrWhiteSpace(sharedKey)) return false;

        try
        {
            return Convert.FromBase64String(sharedKey.Trim()).Length >= 32;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static string CreateSignature(
        string sharedKey,
        long unixTimestamp,
        string nonce,
        string method,
        string path,
        ReadOnlySpan<byte> body)
    {
        var key = Convert.FromBase64String(sharedKey.Trim());
        try
        {
            var bodyHash = SHA256.HashData(body);
            var canonical = string.Join("\n",
                unixTimestamp.ToString(System.Globalization.CultureInfo.InvariantCulture),
                nonce,
                method.ToUpperInvariant(),
                path,
                Convert.ToHexString(bodyHash));

            using var hmac = new HMACSHA256(key);
            return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(canonical)));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    public static bool FixedTimeSignatureEquals(string expected, string actual)
    {
        try
        {
            var expectedBytes = Convert.FromHexString(expected);
            var actualBytes = Convert.FromHexString(actual);
            return expectedBytes.Length == actualBytes.Length &&
                   CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
