namespace AbroadQs.Bot.Contracts;

/// <summary>
/// Key-value settings (e.g. Telegram.BotToken) persisted in database.
/// </summary>
public interface ISettingsRepository
{
    Task<string?> GetValueAsync(string key, CancellationToken cancellationToken = default);
    Task SetValueAsync(string key, string value, CancellationToken cancellationToken = default);
}
