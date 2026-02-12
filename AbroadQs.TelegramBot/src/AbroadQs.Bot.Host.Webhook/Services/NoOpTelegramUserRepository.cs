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

    public Task SetCleanChatModeAsync(long telegramUserId, bool enabled, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task SetPhoneNumberAsync(long telegramUserId, string phoneNumber, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task SetPhoneVerifiedAsync(long telegramUserId, string method, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task SetVerifiedAsync(long telegramUserId, string? photoFileId, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task SetEmailAsync(long telegramUserId, string email, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task SetEmailVerifiedAsync(long telegramUserId, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task SetCountryAsync(long telegramUserId, string country, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task SetKycStatusAsync(long telegramUserId, string status, string? rejectionData = null, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task SetBioAsync(long telegramUserId, string? bio, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task SetGitHubUrlAsync(long telegramUserId, string? url, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task SetLinkedInUrlAsync(long telegramUserId, string? url, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task SetInstagramUrlAsync(long telegramUserId, string? url, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
