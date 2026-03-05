using System.Security.Cryptography;
using System.Text;

namespace AbroadQs.Bot.Host.Webhook.Services;

public sealed class SecretCryptoService
{
    private readonly byte[] _key;

    public SecretCryptoService(IConfiguration config)
    {
        var raw = config["Security:SecretMasterKey"]
            ?? Environment.GetEnvironmentVariable("ABROADQS_SECRET_MASTER_KEY")
            ?? "abroadqs-dev-master-key-change-me";
        _key = DeriveKey(raw);
    }

    public (string CipherText, string Nonce, string Tag) Encrypt(string plainText)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var cipher = new byte[plainBytes.Length];
        var tag = new byte[16];

        using var aes = new AesGcm(_key, 16);
        aes.Encrypt(nonce, plainBytes, cipher, tag);
        return (
            Convert.ToBase64String(cipher),
            Convert.ToBase64String(nonce),
            Convert.ToBase64String(tag));
    }

    public string Decrypt(string cipherText, string nonce, string tag)
    {
        var cipher = Convert.FromBase64String(cipherText);
        var nonceBytes = Convert.FromBase64String(nonce);
        var tagBytes = Convert.FromBase64String(tag);
        var plain = new byte[cipher.Length];

        using var aes = new AesGcm(_key, 16);
        aes.Decrypt(nonceBytes, cipher, tagBytes, plain);
        return Encoding.UTF8.GetString(plain);
    }

    private static byte[] DeriveKey(string raw)
    {
        if (raw.StartsWith("base64:", StringComparison.OrdinalIgnoreCase))
        {
            var b64 = raw["base64:".Length..];
            var decoded = Convert.FromBase64String(b64);
            if (decoded.Length >= 32) return decoded[..32];
        }
        using var sha = SHA256.Create();
        return sha.ComputeHash(Encoding.UTF8.GetBytes(raw));
    }
}
