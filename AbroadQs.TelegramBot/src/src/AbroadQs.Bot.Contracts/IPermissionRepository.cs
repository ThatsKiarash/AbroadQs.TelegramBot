namespace AbroadQs.Bot.Contracts;

/// <summary>
/// Manages permissions and user-permission assignments.
/// </summary>
public interface IPermissionRepository
{
    // --- Permissions ---
    Task<IReadOnlyList<PermissionDto>> ListAllAsync(CancellationToken cancellationToken = default);
    Task<PermissionDto> CreateAsync(PermissionCreateDto dto, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(string permissionKey, CancellationToken cancellationToken = default);

    // --- User Permissions ---
    Task<IReadOnlyList<string>> GetUserPermissionsAsync(long telegramUserId, CancellationToken cancellationToken = default);
    Task<bool> UserHasPermissionAsync(long telegramUserId, string permissionKey, CancellationToken cancellationToken = default);
    Task GrantPermissionAsync(long telegramUserId, string permissionKey, CancellationToken cancellationToken = default);
    Task RevokePermissionAsync(long telegramUserId, string permissionKey, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UserPermissionDto>> GetAllUserPermissionsAsync(long telegramUserId, CancellationToken cancellationToken = default);
}

// --- Permission DTOs ---

public sealed record PermissionDto(
    int Id,
    string PermissionKey,
    string? NameFa,
    string? NameEn,
    string? Description);

public sealed record PermissionCreateDto(
    string PermissionKey,
    string? NameFa,
    string? NameEn,
    string? Description);

public sealed record UserPermissionDto(
    long TelegramUserId,
    string PermissionKey,
    DateTimeOffset GrantedAt);
