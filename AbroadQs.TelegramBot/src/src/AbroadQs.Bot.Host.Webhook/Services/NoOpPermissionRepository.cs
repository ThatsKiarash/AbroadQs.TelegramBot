using AbroadQs.Bot.Contracts;

namespace AbroadQs.Bot.Host.Webhook.Services;

/// <summary>
/// No-op implementation when SQL Server is not configured.
/// </summary>
public sealed class NoOpPermissionRepository : IPermissionRepository
{
    public Task<IReadOnlyList<PermissionDto>> ListAllAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<PermissionDto>>(Array.Empty<PermissionDto>());

    public Task<PermissionDto> CreateAsync(PermissionCreateDto dto, CancellationToken cancellationToken = default)
        => Task.FromResult(new PermissionDto(0, dto.PermissionKey, dto.NameFa, dto.NameEn, dto.Description));

    public Task<bool> DeleteAsync(string permissionKey, CancellationToken cancellationToken = default)
        => Task.FromResult(false);

    public Task<IReadOnlyList<string>> GetUserPermissionsAsync(long telegramUserId, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

    public Task<bool> UserHasPermissionAsync(long telegramUserId, string permissionKey, CancellationToken cancellationToken = default)
        => Task.FromResult(true); // Allow all when no DB

    public Task GrantPermissionAsync(long telegramUserId, string permissionKey, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task RevokePermissionAsync(long telegramUserId, string permissionKey, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<IReadOnlyList<UserPermissionDto>> GetAllUserPermissionsAsync(long telegramUserId, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<UserPermissionDto>>(Array.Empty<UserPermissionDto>());
}
