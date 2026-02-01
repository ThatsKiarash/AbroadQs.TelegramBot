using AbroadQs.Bot.Contracts;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace AbroadQs.Bot.Telegram;

/// <summary>
/// Sends responses via Telegram Bot API. Reusable in any project that needs to reply to users.
/// </summary>
public sealed class TelegramResponseSender : IResponseSender
{
    private readonly ITelegramBotClient _client;

    public TelegramResponseSender(ITelegramBotClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public async Task SendTextMessageAsync(long chatId, string text, CancellationToken cancellationToken = default)
    {
        await _client.SendMessage(
            new ChatId(chatId),
            text,
            parseMode: ParseMode.Html,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task SendTextMessageAsync(long chatId, string text, bool disableWebPagePreview, CancellationToken cancellationToken = default)
    {
        await _client.SendMessage(
            new ChatId(chatId),
            text,
            parseMode: ParseMode.Html,
            linkPreviewOptions: new LinkPreviewOptions { IsDisabled = disableWebPagePreview },
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}
