namespace AbroadQs.Bot.Contracts;

/// <summary>
/// Handles a bot update. Implement in modules; order and routing are decided by the application layer.
/// </summary>
public interface IUpdateHandler
{
    /// <summary>
    /// Command this handler is responsible for (e.g. "start", "help"). Null = handle all or by other criteria.
    /// </summary>
    string? Command { get; }

    /// <summary>
    /// Whether this handler can handle the given context (e.g. by command, message type).
    /// </summary>
    bool CanHandle(BotUpdateContext context);

    /// <summary>
    /// Handle the update. Return true if handled and pipeline should stop; false to try next handler.
    /// </summary>
    Task<bool> HandleAsync(BotUpdateContext context, CancellationToken cancellationToken);
}
