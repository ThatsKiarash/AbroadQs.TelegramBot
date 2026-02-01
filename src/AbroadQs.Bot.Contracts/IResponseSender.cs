namespace AbroadQs.Bot.Contracts;

/// <summary>
/// Abstraction for sending responses back to the user. Keeps handlers independent of Telegram client.
/// </summary>
public interface IResponseSender
{
    Task SendTextMessageAsync(long chatId, string text, CancellationToken cancellationToken = default);
    Task SendTextMessageAsync(long chatId, string text, bool disableWebPagePreview, CancellationToken cancellationToken = default);
}
