using AbroadQs.Bot.Contracts;

namespace AbroadQs.Bot.Host.Webhook.Services;

public sealed class NoOpUserConversationStateStore : IUserConversationStateStore
{
    public Task SetStateAsync(long userId, string state, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<string?> GetStateAsync(long userId, CancellationToken cancellationToken = default)
        => Task.FromResult<string?>(null);

    public Task ClearStateAsync(long userId, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task SetReplyStageAsync(long userId, string stageKey, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<string?> GetReplyStageAsync(long userId, CancellationToken cancellationToken = default)
        => Task.FromResult<string?>(null);

    public Task AddFlowMessageIdAsync(long userId, int messageId, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<List<int>> GetAndClearFlowMessageIdsAsync(long userId, CancellationToken cancellationToken = default)
        => Task.FromResult(new List<int>());
}
