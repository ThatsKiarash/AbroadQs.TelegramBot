namespace AbroadQs.Bot.Contracts;

/// <summary>
/// Persists Telegram user information (e.g. in SQL Server).
/// Implement in the Data project; host registers it.
/// </summary>
public interface ITelegramUserRepository
{
    Task SaveOrUpdateAsync(long telegramUserId, string? username, string? firstName, string? lastName, CancellationToken cancellationToken = default);
}
