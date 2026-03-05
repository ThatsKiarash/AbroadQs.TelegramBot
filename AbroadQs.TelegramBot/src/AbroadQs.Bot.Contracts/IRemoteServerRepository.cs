namespace AbroadQs.Bot.Contracts;

public interface IRemoteServerRepository
{
    Task<RemoteServerDto> AddAsync(RemoteServerCreateDto dto, CancellationToken ct = default);
    Task<RemoteServerDto?> GetByIdAsync(int serverId, CancellationToken ct = default);
    Task<IReadOnlyList<RemoteServerDto>> ListByOwnerAsync(long ownerTelegramUserId, CancellationToken ct = default);
    Task<IReadOnlyList<RemoteServerDto>> ListAllAsync(long? ownerTelegramUserId = null, CancellationToken ct = default);
    Task<bool> DeleteAsync(int serverId, long ownerTelegramUserId, CancellationToken ct = default);
    Task TouchLastConnectedAsync(int serverId, CancellationToken ct = default);

    Task<long> AddAuditAsync(RemoteServerAuditCreateDto dto, CancellationToken ct = default);
    Task<IReadOnlyList<RemoteServerAuditDto>> ListAuditsAsync(
        long? actorTelegramUserId = null,
        int? serverId = null,
        int take = 100,
        CancellationToken ct = default);

    Task<long> CreateInstallerJobAsync(RemoteInstallerJobCreateDto dto, CancellationToken ct = default);
    Task<bool> UpdateInstallerJobAsync(RemoteInstallerJobUpdateDto dto, CancellationToken ct = default);
    Task<RemoteInstallerJobDto?> GetInstallerJobAsync(long jobId, CancellationToken ct = default);
    Task<IReadOnlyList<RemoteInstallerJobDto>> ListInstallerJobsAsync(
        long? actorTelegramUserId = null,
        int? serverId = null,
        int take = 100,
        CancellationToken ct = default);
}

public sealed record RemoteServerCreateDto(
    long OwnerTelegramUserId,
    string Name,
    string Host,
    int Port,
    string Username,
    string AuthType,
    string EncryptedSecret,
    string SecretNonce,
    string SecretTag,
    string? Tags,
    string? Description);

public sealed record RemoteServerDto(
    int Id,
    long OwnerTelegramUserId,
    string Name,
    string Host,
    int Port,
    string Username,
    string AuthType,
    string EncryptedSecret,
    string SecretNonce,
    string SecretTag,
    string? Tags,
    string? Description,
    bool IsActive,
    DateTimeOffset? LastConnectedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

public sealed record RemoteServerAuditCreateDto(
    int ServerId,
    long ActorTelegramUserId,
    string ActionType,
    string? CommandText,
    bool Success,
    int? ExitCode,
    long? DurationMs,
    string? OutputPreview,
    string? ErrorMessage,
    string? MetadataJson);

public sealed record RemoteServerAuditDto(
    long Id,
    int ServerId,
    long ActorTelegramUserId,
    string ActionType,
    string? CommandText,
    bool Success,
    int? ExitCode,
    long? DurationMs,
    string? OutputPreview,
    string? ErrorMessage,
    string? MetadataJson,
    DateTimeOffset CreatedAt);

public sealed record RemoteInstallerJobCreateDto(
    int ServerId,
    long ActorTelegramUserId,
    string JobType,
    string? RequestJson);

public sealed record RemoteInstallerJobUpdateDto(
    long Id,
    string Status,
    string? LogText,
    string? ResultJson,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt);

public sealed record RemoteInstallerJobDto(
    long Id,
    int ServerId,
    long ActorTelegramUserId,
    string JobType,
    string Status,
    string? RequestJson,
    string? LogText,
    string? ResultJson,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt);
