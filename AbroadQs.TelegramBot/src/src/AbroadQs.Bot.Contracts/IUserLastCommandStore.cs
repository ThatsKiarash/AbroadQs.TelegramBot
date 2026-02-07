namespace AbroadQs.Bot.Contracts;

/// <summary>
/// Stores the last command used by a user (e.g. for Redis cache).
/// Implement in the host; use no-op when Redis is not configured.
/// </summary>
public interface IUserLastCommandStore
{
    Task SetLastCommandAsync(long userId, string command, CancellationToken cancellationToken = default);
    Task<string?> GetLastCommandAsync(long userId, CancellationToken cancellationToken = default);
}
