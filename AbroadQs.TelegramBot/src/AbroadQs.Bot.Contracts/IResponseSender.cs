namespace AbroadQs.Bot.Contracts;

/// <summary>
/// One inline button (callback or URL). Use in rows for inline keyboard.
/// </summary>
public sealed record InlineButton(string Text, string? CallbackData = null, string? Url = null);

/// <summary>
/// Abstraction for sending responses back to the user. Keeps handlers independent of Telegram client.
/// </summary>
public interface IResponseSender
{
    Task SendTextMessageAsync(long chatId, string text, CancellationToken cancellationToken = default);
    Task SendTextMessageAsync(long chatId, string text, bool disableWebPagePreview, CancellationToken cancellationToken = default);

    /// <summary>
    /// Send a message with an inline keyboard (e.g. glass-style buttons). Each inner list is one row.
    /// </summary>
    Task SendTextMessageWithInlineKeyboardAsync(long chatId, string text, IReadOnlyList<IReadOnlyList<InlineButton>> inlineKeyboard, CancellationToken cancellationToken = default);

    /// <summary>
    /// Edit an existing message's text and inline keyboard. Use when handling a callback to update the same message.
    /// </summary>
    Task EditMessageTextWithInlineKeyboardAsync(long chatId, int messageId, string text, IReadOnlyList<IReadOnlyList<InlineButton>> inlineKeyboard, CancellationToken cancellationToken = default);

    /// <summary>
    /// Answer a callback query so Telegram removes the loading state. Call when handling a callback.
    /// </summary>
    Task AnswerCallbackQueryAsync(string callbackQueryId, string? message = null, CancellationToken cancellationToken = default);
}
