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
}
