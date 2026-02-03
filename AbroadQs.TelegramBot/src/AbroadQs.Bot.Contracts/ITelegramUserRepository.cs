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
}

public sealed record TelegramUserDto(
    long TelegramUserId,
    string? Username,
    string? FirstName,
    string? LastName,
    string? PreferredLanguage,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt);
