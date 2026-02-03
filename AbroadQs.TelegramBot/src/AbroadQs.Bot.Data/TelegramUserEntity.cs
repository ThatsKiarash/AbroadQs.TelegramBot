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
    public DateTimeOffset FirstSeenAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
}
