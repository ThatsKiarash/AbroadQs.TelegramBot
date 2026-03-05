namespace AbroadQs.Bot.Data;

public sealed class RemoteServerEntity
{
    public int Id { get; set; }
    public long OwnerTelegramUserId { get; set; }
    public string Name { get; set; } = "";
    public string Host { get; set; } = "";
    public int Port { get; set; } = 22;
    public string Username { get; set; } = "";
    public string AuthType { get; set; } = "password";

    // Encrypted secret payload (password or private key).
    public string EncryptedSecret { get; set; } = "";
    public string SecretNonce { get; set; } = "";
    public string SecretTag { get; set; } = "";

    public string? Tags { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset? LastConnectedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
}
