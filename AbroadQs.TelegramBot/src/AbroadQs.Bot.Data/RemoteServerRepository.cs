using AbroadQs.Bot.Contracts;
using Microsoft.EntityFrameworkCore;

namespace AbroadQs.Bot.Data;

public sealed class RemoteServerRepository : IRemoteServerRepository
{
    private readonly ApplicationDbContext _db;

    public RemoteServerRepository(ApplicationDbContext db) => _db = db;

    public async Task<RemoteServerDto> AddAsync(RemoteServerCreateDto dto, CancellationToken ct = default)
    {
        var entity = new RemoteServerEntity
        {
            OwnerTelegramUserId = dto.OwnerTelegramUserId,
            Name = dto.Name,
            Host = dto.Host,
            Port = dto.Port,
            Username = dto.Username,
            AuthType = dto.AuthType,
            EncryptedSecret = dto.EncryptedSecret,
            SecretNonce = dto.SecretNonce,
            SecretTag = dto.SecretTag,
            Tags = dto.Tags,
            Description = dto.Description,
            CreatedAt = DateTimeOffset.UtcNow,
            IsActive = true
        };

        _db.Set<RemoteServerEntity>().Add(entity);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToDto(entity);
    }

    public async Task<RemoteServerDto?> GetByIdAsync(int serverId, CancellationToken ct = default)
    {
        var entity = await _db.Set<RemoteServerEntity>()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == serverId, ct)
            .ConfigureAwait(false);
        return entity is null ? null : ToDto(entity);
    }

    public async Task<IReadOnlyList<RemoteServerDto>> ListByOwnerAsync(long ownerTelegramUserId, CancellationToken ct = default)
    {
        var list = await _db.Set<RemoteServerEntity>()
            .AsNoTracking()
            .Where(x => x.OwnerTelegramUserId == ownerTelegramUserId)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return list.Select(ToDto).ToList();
    }

    public async Task<IReadOnlyList<RemoteServerDto>> ListAllAsync(long? ownerTelegramUserId = null, CancellationToken ct = default)
    {
        var q = _db.Set<RemoteServerEntity>().AsNoTracking().AsQueryable();
        if (ownerTelegramUserId.HasValue)
            q = q.Where(x => x.OwnerTelegramUserId == ownerTelegramUserId.Value);
        var list = await q.OrderByDescending(x => x.CreatedAt).ToListAsync(ct).ConfigureAwait(false);
        return list.Select(ToDto).ToList();
    }

    public async Task<bool> DeleteAsync(int serverId, long ownerTelegramUserId, CancellationToken ct = default)
    {
        var entity = await _db.Set<RemoteServerEntity>()
            .FirstOrDefaultAsync(x => x.Id == serverId && x.OwnerTelegramUserId == ownerTelegramUserId, ct)
            .ConfigureAwait(false);
        if (entity is null) return false;
        _db.Set<RemoteServerEntity>().Remove(entity);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }

    public async Task TouchLastConnectedAsync(int serverId, CancellationToken ct = default)
    {
        var entity = await _db.Set<RemoteServerEntity>()
            .FirstOrDefaultAsync(x => x.Id == serverId, ct)
            .ConfigureAwait(false);
        if (entity is null) return;
        entity.LastConnectedAt = DateTimeOffset.UtcNow;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<long> AddAuditAsync(RemoteServerAuditCreateDto dto, CancellationToken ct = default)
    {
        var entity = new RemoteServerAuditEntity
        {
            ServerId = dto.ServerId,
            ActorTelegramUserId = dto.ActorTelegramUserId,
            ActionType = dto.ActionType,
            CommandText = dto.CommandText,
            Success = dto.Success,
            ExitCode = dto.ExitCode,
            DurationMs = dto.DurationMs,
            OutputPreview = dto.OutputPreview,
            ErrorMessage = dto.ErrorMessage,
            MetadataJson = dto.MetadataJson,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _db.Set<RemoteServerAuditEntity>().Add(entity);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return entity.Id;
    }

    public async Task<IReadOnlyList<RemoteServerAuditDto>> ListAuditsAsync(
        long? actorTelegramUserId = null,
        int? serverId = null,
        int take = 100,
        CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 500);
        var q = _db.Set<RemoteServerAuditEntity>().AsNoTracking().AsQueryable();
        if (actorTelegramUserId.HasValue)
            q = q.Where(x => x.ActorTelegramUserId == actorTelegramUserId.Value);
        if (serverId.HasValue)
            q = q.Where(x => x.ServerId == serverId.Value);
        var list = await q.OrderByDescending(x => x.CreatedAt).Take(take).ToListAsync(ct).ConfigureAwait(false);
        return list.Select(ToDto).ToList();
    }

    public async Task<long> CreateInstallerJobAsync(RemoteInstallerJobCreateDto dto, CancellationToken ct = default)
    {
        var entity = new RemoteInstallerJobEntity
        {
            ServerId = dto.ServerId,
            ActorTelegramUserId = dto.ActorTelegramUserId,
            JobType = dto.JobType,
            Status = "queued",
            RequestJson = dto.RequestJson,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _db.Set<RemoteInstallerJobEntity>().Add(entity);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return entity.Id;
    }

    public async Task<bool> UpdateInstallerJobAsync(RemoteInstallerJobUpdateDto dto, CancellationToken ct = default)
    {
        var entity = await _db.Set<RemoteInstallerJobEntity>().FirstOrDefaultAsync(x => x.Id == dto.Id, ct).ConfigureAwait(false);
        if (entity is null) return false;
        entity.Status = dto.Status;
        entity.LogText = dto.LogText ?? entity.LogText;
        entity.ResultJson = dto.ResultJson ?? entity.ResultJson;
        entity.StartedAt = dto.StartedAt ?? entity.StartedAt;
        entity.FinishedAt = dto.FinishedAt ?? entity.FinishedAt;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }

    public async Task<RemoteInstallerJobDto?> GetInstallerJobAsync(long jobId, CancellationToken ct = default)
    {
        var entity = await _db.Set<RemoteInstallerJobEntity>()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == jobId, ct)
            .ConfigureAwait(false);
        return entity is null ? null : ToDto(entity);
    }

    public async Task<IReadOnlyList<RemoteInstallerJobDto>> ListInstallerJobsAsync(
        long? actorTelegramUserId = null,
        int? serverId = null,
        int take = 100,
        CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 500);
        var q = _db.Set<RemoteInstallerJobEntity>().AsNoTracking().AsQueryable();
        if (actorTelegramUserId.HasValue)
            q = q.Where(x => x.ActorTelegramUserId == actorTelegramUserId.Value);
        if (serverId.HasValue)
            q = q.Where(x => x.ServerId == serverId.Value);
        var list = await q.OrderByDescending(x => x.CreatedAt).Take(take).ToListAsync(ct).ConfigureAwait(false);
        return list.Select(ToDto).ToList();
    }

    private static RemoteServerDto ToDto(RemoteServerEntity x) =>
        new(
            x.Id,
            x.OwnerTelegramUserId,
            x.Name,
            x.Host,
            x.Port,
            x.Username,
            x.AuthType,
            x.EncryptedSecret,
            x.SecretNonce,
            x.SecretTag,
            x.Tags,
            x.Description,
            x.IsActive,
            x.LastConnectedAt,
            x.CreatedAt,
            x.UpdatedAt);

    private static RemoteServerAuditDto ToDto(RemoteServerAuditEntity x) =>
        new(
            x.Id,
            x.ServerId,
            x.ActorTelegramUserId,
            x.ActionType,
            x.CommandText,
            x.Success,
            x.ExitCode,
            x.DurationMs,
            x.OutputPreview,
            x.ErrorMessage,
            x.MetadataJson,
            x.CreatedAt);

    private static RemoteInstallerJobDto ToDto(RemoteInstallerJobEntity x) =>
        new(
            x.Id,
            x.ServerId,
            x.ActorTelegramUserId,
            x.JobType,
            x.Status,
            x.RequestJson,
            x.LogText,
            x.ResultJson,
            x.CreatedAt,
            x.StartedAt,
            x.FinishedAt);
}
