using AbroadQs.Bot.Contracts;

namespace AbroadQs.Bot.Host.Webhook.Services;

public sealed class NoOpRemoteServerRepository : IRemoteServerRepository
{
    public Task<RemoteServerDto> AddAsync(RemoteServerCreateDto dto, CancellationToken ct = default)
        => Task.FromException<RemoteServerDto>(new InvalidOperationException("SQL Server is not configured."));

    public Task<RemoteServerDto?> GetByIdAsync(int serverId, CancellationToken ct = default)
        => Task.FromResult<RemoteServerDto?>(null);

    public Task<IReadOnlyList<RemoteServerDto>> ListByOwnerAsync(long ownerTelegramUserId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<RemoteServerDto>>([]);

    public Task<IReadOnlyList<RemoteServerDto>> ListAllAsync(long? ownerTelegramUserId = null, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<RemoteServerDto>>([]);

    public Task<bool> DeleteAsync(int serverId, long ownerTelegramUserId, CancellationToken ct = default)
        => Task.FromResult(false);

    public Task TouchLastConnectedAsync(int serverId, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<long> AddAuditAsync(RemoteServerAuditCreateDto dto, CancellationToken ct = default)
        => Task.FromResult(0L);

    public Task<IReadOnlyList<RemoteServerAuditDto>> ListAuditsAsync(long? actorTelegramUserId = null, int? serverId = null, int take = 100, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<RemoteServerAuditDto>>([]);

    public Task<long> CreateInstallerJobAsync(RemoteInstallerJobCreateDto dto, CancellationToken ct = default)
        => Task.FromResult(0L);

    public Task<bool> UpdateInstallerJobAsync(RemoteInstallerJobUpdateDto dto, CancellationToken ct = default)
        => Task.FromResult(false);

    public Task<RemoteInstallerJobDto?> GetInstallerJobAsync(long jobId, CancellationToken ct = default)
        => Task.FromResult<RemoteInstallerJobDto?>(null);

    public Task<IReadOnlyList<RemoteInstallerJobDto>> ListInstallerJobsAsync(long? actorTelegramUserId = null, int? serverId = null, int take = 100, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<RemoteInstallerJobDto>>([]);
}
