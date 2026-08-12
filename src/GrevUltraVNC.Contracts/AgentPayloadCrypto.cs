using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GrevUltraVNC.Contracts;

public sealed record AgentEncryptedEnvelope(
    string Nonce,
    string Ciphertext,
    string Tag);

public static class AgentPayloadCrypto
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly byte[] Context = Encoding.UTF8.GetBytes("GrevUltraVNC-Agent-Payload-Encryption-v1");

    public static AgentEncryptedEnvelope Encrypt<T>(string sharedKey, T payload)
    {
        var key = DeriveEncryptionKey(sharedKey);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];

        try
        {
            using var aes = new AesGcm(key, tag.Length);
            aes.Encrypt(nonce, plaintext, ciphertext, tag);

            return new AgentEncryptedEnvelope(
                Convert.ToBase64String(nonce),
                Convert.ToBase64String(ciphertext),
                Convert.ToBase64String(tag));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public static T Decrypt<T>(string sharedKey, AgentEncryptedEnvelope envelope)
    {
        var key = DeriveEncryptionKey(sharedKey);
        var nonce = Convert.FromBase64String(envelope.Nonce);
        var ciphertext = Convert.FromBase64String(envelope.Ciphertext);
        var tag = Convert.FromBase64String(envelope.Tag);
        var plaintext = new byte[ciphertext.Length];

        try
        {
            if (nonce.Length != 12 || tag.Length != 16)
                throw new CryptographicException("Grev Agent encrypted payload has an invalid nonce or authentication tag.");

            using var aes = new AesGcm(key, tag.Length);
            aes.Decrypt(nonce, ciphertext, tag, plaintext);

            return JsonSerializer.Deserialize<T>(plaintext, JsonOptions)
                   ?? throw new CryptographicException("Grev Agent encrypted payload was empty.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private static byte[] DeriveEncryptionKey(string sharedKey)
    {
        var rawKey = Convert.FromBase64String(sharedKey.Trim());
        try
        {
            using var hmac = new HMACSHA256(rawKey);
            return hmac.ComputeHash(Context);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(rawKey);
        }
    }
}
