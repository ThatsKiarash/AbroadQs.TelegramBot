namespace AbroadQs.Bot.Contracts;

public interface IRemoteServerRuntimeService
{
    Task<RuntimeActionResult> AddServerAsync(
        long actorTelegramUserId,
        string name,
        string host,
        int port,
        string username,
        string secret,
        string authType = "password",
        string? tags = null,
        string? description = null,
        CancellationToken ct = default);
    Task<RuntimeActionResult> ConnectAsync(long actorTelegramUserId, int serverId, CancellationToken ct = default);
    Task<RuntimeActionResult> ExecuteCommandAsync(long actorTelegramUserId, int serverId, string commandText, CancellationToken ct = default);
    Task<RuntimeActionResult> DisconnectAsync(long actorTelegramUserId, int serverId, CancellationToken ct = default);
    Task<RuntimeActionResult> InstallOpenClawAsync(long actorTelegramUserId, int serverId, CancellationToken ct = default);
    Task<RuntimeActionResult> InstallSlipnetAsync(long actorTelegramUserId, int serverId, CancellationToken ct = default);
    Task<RuntimeActionResult> InstallDnsttAsync(long actorTelegramUserId, int serverId, CancellationToken ct = default);
    Task<RemoteInstallerJobDto?> GetInstallerJobAsync(long actorTelegramUserId, long jobId, CancellationToken ct = default);
}

public sealed record RuntimeActionResult(
    bool Success,
    string Message,
    int? ExitCode = null,
    string? Output = null,
    long? JobId = null,
    long? DurationMs = null);
