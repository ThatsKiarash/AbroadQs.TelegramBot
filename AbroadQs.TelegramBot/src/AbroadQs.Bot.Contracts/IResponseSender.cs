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
    /// Send a message with a reply keyboard (persistent buttons at the bottom of the chat). Each inner list is one row of button labels.
    /// </summary>
    Task SendTextMessageWithReplyKeyboardAsync(long chatId, string text, IReadOnlyList<IReadOnlyList<string>> keyboard, CancellationToken cancellationToken = default);

    /// <summary>
    /// Edit an existing message's text and inline keyboard. Use when handling a callback to update the same message.
    /// </summary>
    Task EditMessageTextWithInlineKeyboardAsync(long chatId, int messageId, string text, IReadOnlyList<IReadOnlyList<InlineButton>> inlineKeyboard, CancellationToken cancellationToken = default);

    /// <summary>
    /// Edit an existing message's text (no keyboard change). Used for reply-keyboard messages where only text needs updating.
    /// </summary>
    Task EditMessageTextAsync(long chatId, int messageId, string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// Silently update the reply keyboard by sending a phantom message with the new keyboard and immediately deleting it.
    /// The keyboard persists after the phantom is deleted. Does NOT save the phantom as a bot message.
    /// </summary>
    Task UpdateReplyKeyboardSilentAsync(long chatId, IReadOnlyList<IReadOnlyList<string>> keyboard, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a message from a chat. Used e.g. to remove language-selection prompts after the user picks a language.
    /// </summary>
    Task DeleteMessageAsync(long chatId, int messageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Answer a callback query so Telegram removes the loading state. Call when handling a callback.
    /// </summary>
    Task AnswerCallbackQueryAsync(string callbackQueryId, string? message = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Send a message with a reply keyboard that has a "Share Contact" button.
    /// Used for phone number verification in KYC flow.
    /// </summary>
    Task SendContactRequestAsync(long chatId, string text, string buttonLabel, string? cancelLabel = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove the reply keyboard for a user. Used after contact sharing to clean up the UI.
    /// </summary>
    Task RemoveReplyKeyboardAsync(long chatId, string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// Silently remove the reply keyboard — sends a temporary message with ReplyKeyboardRemove and immediately deletes it.
    /// The user sees nothing. Does NOT save the phantom as a bot message.
    /// </summary>
    Task RemoveReplyKeyboardSilentAsync(long chatId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends an invisible placeholder message with ReplyKeyboardRemove and returns the message ID.
    /// The caller should EDIT this message with the actual content + inline keyboard.
    /// This avoids dual keyboards (reply-kb + inline appearing simultaneously).
    /// </summary>
    Task<int?> SendLoadingWithRemoveReplyKbAsync(long chatId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Send a photo with an optional caption. Used to show sample verification images etc.
    /// </summary>
    Task SendPhotoAsync(long chatId, string photoPath, string? caption = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Send a photo from a URL with an optional caption and inline keyboard.
    /// </summary>
    Task SendPhotoWithInlineKeyboardAsync(long chatId, string photoUrl, string? caption, IReadOnlyList<IReadOnlyList<InlineButton>>? inlineKeyboard = null, CancellationToken cancellationToken = default);
}
