namespace AbroadQs.Bot.Contracts;

/// <summary>
/// Stores temporary conversation state per user (e.g. "awaiting_profile_name").
/// Used for multi-step flows like profile completion.
/// </summary>
public interface IUserConversationStateStore
{
    Task SetStateAsync(long userId, string state, CancellationToken cancellationToken = default);
    Task<string?> GetStateAsync(long userId, CancellationToken cancellationToken = default);
    Task ClearStateAsync(long userId, CancellationToken cancellationToken = default);

    /// <summary>Track which reply keyboard stage the user is currently viewing.</summary>
    Task SetReplyStageAsync(long userId, string stageKey, CancellationToken cancellationToken = default);
    Task<string?> GetReplyStageAsync(long userId, CancellationToken cancellationToken = default);

    /// <summary>Track message IDs during multi-step flows (KYC etc.) for bulk cleanup.</summary>
    Task AddFlowMessageIdAsync(long userId, int messageId, CancellationToken cancellationToken = default);
    Task<List<int>> GetAndClearFlowMessageIdsAsync(long userId, CancellationToken cancellationToken = default);
}
