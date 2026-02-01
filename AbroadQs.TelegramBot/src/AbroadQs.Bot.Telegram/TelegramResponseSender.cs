using AbroadQs.Bot.Contracts;
using Microsoft.Extensions.Logging;
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
    private readonly ILogger<TelegramResponseSender> _logger;
    private readonly IProcessingContext? _processingContext;

    public TelegramResponseSender(ITelegramBotClient client, ILogger<TelegramResponseSender> logger, IProcessingContext? processingContext = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _processingContext = processingContext;
    }

    public async Task SendTextMessageAsync(long chatId, string text, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Sending message to chat {ChatId}", chatId);
            var result = await _client.SendMessage(
                new ChatId(chatId),
                text,
                parseMode: ParseMode.Html,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            
            if (result != null)
            {
                _logger.LogInformation("Message sent successfully to chat {ChatId}, messageId: {MessageId}", chatId, result.MessageId);
                if (_processingContext != null)
                    _processingContext.ResponseSent = true;
            }
            else
            {
                _logger.LogWarning("Message not sent to chat {ChatId} - bot client returned null (token not set?)", chatId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send message to chat {ChatId}", chatId);
            throw;
        }
    }

    public async Task SendTextMessageAsync(long chatId, string text, bool disableWebPagePreview, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Sending message to chat {ChatId} with link preview disabled: {Disabled}", chatId, disableWebPagePreview);
            var result = await _client.SendMessage(
                new ChatId(chatId),
                text,
                parseMode: ParseMode.Html,
                linkPreviewOptions: new LinkPreviewOptions { IsDisabled = disableWebPagePreview },
                cancellationToken: cancellationToken).ConfigureAwait(false);
            
            if (result != null)
            {
                _logger.LogInformation("Message sent successfully to chat {ChatId}, messageId: {MessageId}", chatId, result.MessageId);
                if (_processingContext != null)
                    _processingContext.ResponseSent = true;
            }
            else
            {
                _logger.LogWarning("Message not sent to chat {ChatId} - bot client returned null (token not set?)", chatId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send message to chat {ChatId}", chatId);
            throw;
        }
    }
}
