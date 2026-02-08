namespace AbroadQs.Bot.Contracts;

/// <summary>
/// Persists Telegram user information (e.g. in SQL Server).
/// Implement in the Data project; host registers it.
/// </summary>
public interface ITelegramUserRepository
{
    Task SaveOrUpdateAsync(long telegramUserId, string? username, string? firstName, string? lastName, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TelegramUserDto>> ListAllAsync(CancellationToken cancellationToken = default);
    Task<TelegramUserDto?> GetByTelegramUserIdAsync(long telegramUserId, CancellationToken cancellationToken = default);
    Task UpdateProfileAsync(long telegramUserId, string? firstName, string? lastName, string? preferredLanguage, CancellationToken cancellationToken = default);
    /// <summary>Mark user as registered (sets IsRegistered=true and RegisteredAt=now).</summary>
    Task MarkAsRegisteredAsync(long telegramUserId, CancellationToken cancellationToken = default);
    /// <summary>Toggle or set the clean-chat mode for a user.</summary>
    Task SetCleanChatModeAsync(long telegramUserId, bool enabled, CancellationToken cancellationToken = default);
}

public sealed record TelegramUserDto(
    long TelegramUserId,
    string? Username,
    string? FirstName,
    string? LastName,
    string? PreferredLanguage,
    bool IsRegistered,
    bool CleanChatMode,
    DateTimeOffset? RegisteredAt,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt);
