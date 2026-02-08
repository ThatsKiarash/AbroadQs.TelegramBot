namespace AbroadQs.Bot.Data;

public sealed class TelegramUserEntity
{
    public int Id { get; set; }
    public long TelegramUserId { get; set; }
    public string? Username { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    /// <summary>User preferred language code (e.g. "fa", "en").</summary>
    public string? PreferredLanguage { get; set; }
    /// <summary>Whether the user has completed the registration flow.</summary>
    public bool IsRegistered { get; set; }
    /// <summary>When the user completed registration.</summary>
    public DateTimeOffset? RegisteredAt { get; set; }
    /// <summary>Whether the bot should keep the chat clean (delete old messages during navigation). Default: true.</summary>
    public bool CleanChatMode { get; set; } = true;
    public DateTimeOffset FirstSeenAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
}
