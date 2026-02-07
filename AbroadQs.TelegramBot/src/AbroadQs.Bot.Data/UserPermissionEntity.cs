namespace AbroadQs.Bot.Data;

/// <summary>
/// Junction table: grants a permission to a specific user.
/// </summary>
public sealed class UserPermissionEntity
{
    public int Id { get; set; }
    /// <summary>Telegram user ID (FK to TelegramUsers.TelegramUserId).</summary>
    public long TelegramUserId { get; set; }
    /// <summary>Permission key (references PermissionEntity.PermissionKey).</summary>
    public string PermissionKey { get; set; } = "";
    /// <summary>When the permission was granted.</summary>
    public DateTimeOffset GrantedAt { get; set; }

    // Navigation
    public TelegramUserEntity? User { get; set; }
}
