using AbroadQs.Bot.Contracts;

namespace AbroadQs.Bot.Host.Webhook.Services;

/// <summary>
/// No-op implementation when SQL Server is not configured. Prevents DI failure for SettingsMenuHandler/ProfileStateHandler.
/// </summary>
public sealed class NoOpTelegramUserRepository : ITelegramUserRepository
{
    public Task SaveOrUpdateAsync(long telegramUserId, string? username, string? firstName, string? lastName, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<IReadOnlyList<TelegramUserDto>> ListAllAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<TelegramUserDto>>(Array.Empty<TelegramUserDto>());

    public Task<TelegramUserDto?> GetByTelegramUserIdAsync(long telegramUserId, CancellationToken cancellationToken = default)
        => Task.FromResult<TelegramUserDto?>(null);

    public Task UpdateProfileAsync(long telegramUserId, string? firstName, string? lastName, string? preferredLanguage, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task MarkAsRegisteredAsync(long telegramUserId, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
