namespace AbroadQs.Bot.Contracts;

/// <summary>
/// Context for a single bot update (message, callback, etc.).
/// Keeps Contracts free from Telegram.Bot types so other projects can reuse.
/// </summary>
public sealed class BotUpdateContext
{
    public long UpdateId { get; init; }
    public long ChatId { get; init; }
    public long? UserId { get; init; }
    public string? MessageText { get; init; }
    public string? Username { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }

    /// <summary>
    /// First word of the message (e.g. /start, /help). Null if not a command.
    /// </summary>
    public string? Command => ParseCommand(MessageText);

    /// <summary>
    /// Arguments after the command (e.g. /start arg1 arg2).
    /// </summary>
    public string? CommandArguments => ParseCommandArguments(MessageText);

    /// <summary>
    /// Telegram message ID of the user's incoming message. Used for cleanup (deleting user msgs to keep chat clean).
    /// </summary>
    public int? IncomingMessageId { get; init; }

    /// <summary>
    /// True when the update is from an inline button press (callback_query).
    /// </summary>
    public bool IsCallbackQuery { get; init; }

    /// <summary>
    /// Telegram message id of the message with the inline keyboard (for editing). Only set when IsCallbackQuery.
    /// </summary>
    public int? CallbackMessageId { get; init; }

    /// <summary>
    /// Callback query id to pass to AnswerCallbackQueryAsync. Only set when IsCallbackQuery.
    /// </summary>
    public string? CallbackQueryId { get; init; }

    /// <summary>
    /// Optional: raw update object from Telegram.Bot for advanced handlers that need it.
    /// Set by the host; handlers can cast to Telegram.Bot.Types.Update if they reference Telegram.Bot.
    /// </summary>
    public object? RawUpdate { get; init; }

    private static string? ParseCommand(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || !text.StartsWith('/'))
            return null;
        var parts = text.TrimStart().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? parts[0].TrimStart('/') : null;
    }

    private static string? ParseCommandArguments(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || !text.StartsWith('/'))
            return null;
        var firstSpace = text.IndexOf(' ');
        return firstSpace < 0 ? null : text[(firstSpace + 1)..].Trim();
    }
}
