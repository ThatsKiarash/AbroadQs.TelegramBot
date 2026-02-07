using AbroadQs.Bot.Contracts;
using Microsoft.EntityFrameworkCore;

namespace AbroadQs.Bot.Data;

public sealed class PermissionRepository : IPermissionRepository
{
    private readonly ApplicationDbContext _db;

    public PermissionRepository(ApplicationDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    // --- Permissions ---

    public async Task<IReadOnlyList<PermissionDto>> ListAllAsync(CancellationToken cancellationToken = default)
    {
        return await _db.Permissions.AsNoTracking()
            .OrderBy(x => x.PermissionKey)
            .Select(x => new PermissionDto(x.Id, x.PermissionKey, x.NameFa, x.NameEn, x.Description))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<PermissionDto> CreateAsync(PermissionCreateDto dto, CancellationToken cancellationToken = default)
    {
        var entity = new PermissionEntity
        {
            PermissionKey = dto.PermissionKey,
            NameFa = dto.NameFa,
            NameEn = dto.NameEn,
            Description = dto.Description
        };
        _db.Permissions.Add(entity);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new PermissionDto(entity.Id, entity.PermissionKey, entity.NameFa, entity.NameEn, entity.Description);
    }

    public async Task<bool> DeleteAsync(string permissionKey, CancellationToken cancellationToken = default)
    {
        var entity = await _db.Permissions.FirstOrDefaultAsync(x => x.PermissionKey == permissionKey, cancellationToken).ConfigureAwait(false);
        if (entity == null) return false;
        // Also remove all user assignments for this permission
        var assignments = await _db.UserPermissions.Where(x => x.PermissionKey == permissionKey).ToListAsync(cancellationToken).ConfigureAwait(false);
        _db.UserPermissions.RemoveRange(assignments);
        _db.Permissions.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    // --- User Permissions ---

    public async Task<IReadOnlyList<string>> GetUserPermissionsAsync(long telegramUserId, CancellationToken cancellationToken = default)
    {
        return await _db.UserPermissions.AsNoTracking()
            .Where(x => x.TelegramUserId == telegramUserId)
            .Select(x => x.PermissionKey)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> UserHasPermissionAsync(long telegramUserId, string permissionKey, CancellationToken cancellationToken = default)
    {
        return await _db.UserPermissions.AsNoTracking()
            .AnyAsync(x => x.TelegramUserId == telegramUserId && x.PermissionKey == permissionKey, cancellationToken).ConfigureAwait(false);
    }

    public async Task GrantPermissionAsync(long telegramUserId, string permissionKey, CancellationToken cancellationToken = default)
    {
        var exists = await _db.UserPermissions
            .AnyAsync(x => x.TelegramUserId == telegramUserId && x.PermissionKey == permissionKey, cancellationToken).ConfigureAwait(false);
        if (exists) return;
        _db.UserPermissions.Add(new UserPermissionEntity
        {
            TelegramUserId = telegramUserId,
            PermissionKey = permissionKey,
            GrantedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RevokePermissionAsync(long telegramUserId, string permissionKey, CancellationToken cancellationToken = default)
    {
        var entity = await _db.UserPermissions
            .FirstOrDefaultAsync(x => x.TelegramUserId == telegramUserId && x.PermissionKey == permissionKey, cancellationToken).ConfigureAwait(false);
        if (entity == null) return;
        _db.UserPermissions.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<UserPermissionDto>> GetAllUserPermissionsAsync(long telegramUserId, CancellationToken cancellationToken = default)
    {
        return await _db.UserPermissions.AsNoTracking()
            .Where(x => x.TelegramUserId == telegramUserId)
            .Select(x => new UserPermissionDto(x.TelegramUserId, x.PermissionKey, x.GrantedAt))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }
}
