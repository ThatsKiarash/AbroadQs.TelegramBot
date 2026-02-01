using AbroadQs.Bot.Contracts;

namespace AbroadQs.Bot.Host.Webhook.Services;

public sealed class NoOpUserLastCommandStore : IUserLastCommandStore
{
    public Task SetLastCommandAsync(long userId, string command, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<string?> GetLastCommandAsync(long userId, CancellationToken cancellationToken = default)
        => Task.FromResult<string?>(null);
}
