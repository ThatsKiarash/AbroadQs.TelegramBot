using AbroadQs.Bot.Host.Webhook.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace AbroadQs.Bot.Host.Webhook.Tests;

public sealed class SecretCryptoServiceTests
{
    [Fact]
    public void Encrypt_Decrypt_RoundTrip_Works()
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:SecretMasterKey"] = "unit-test-master-key"
            })
            .Build();

        var svc = new SecretCryptoService(cfg);
        var payload = "P@ssw0rd-!@#-سری";

        var encrypted = svc.Encrypt(payload);
        var decrypted = svc.Decrypt(encrypted.CipherText, encrypted.Nonce, encrypted.Tag);

        Assert.Equal(payload, decrypted);
    }
}
