using AbroadQs.Bot.Contracts;
using AbroadQs.Bot.Data;
using Microsoft.Extensions.DependencyInjection;
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
    private readonly IServiceScopeFactory? _scopeFactory;

    public TelegramResponseSender(ITelegramBotClient client, ILogger<TelegramResponseSender> logger, IProcessingContext? processingContext = null, IServiceScopeFactory? scopeFactory = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _processingContext = processingContext;
        _scopeFactory = scopeFactory;
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
                
                // Save outgoing message and update user message state
                await SaveOutgoingMessageAsync(result, cancellationToken).ConfigureAwait(false);
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
                
                // Save outgoing message and update user message state
                await SaveOutgoingMessageAsync(result, cancellationToken).ConfigureAwait(false);
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

    private async Task SaveOutgoingMessageAsync(Message message, CancellationToken cancellationToken)
    {
        if (_scopeFactory == null) return;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var messageRepo = scope.ServiceProvider.GetService<IMessageRepository>();
            var stateRepo = scope.ServiceProvider.GetService<IUserMessageStateRepository>();
            
            if (messageRepo != null)
            {
                var messageInfo = MessageRepository.ToOutgoingMessageInfo(message);
                var messageId = await messageRepo.SaveOutgoingMessageAsync(messageInfo, cancellationToken).ConfigureAwait(false);
                
                // Update user message state (use ChatId as TelegramUserId for private chats)
                if (stateRepo != null)
                {
                    var userId = message.Chat.Type == ChatType.Private ? message.Chat.Id : (message.From?.Id ?? message.Chat.Id);
                    await stateRepo.SetLastBotMessageAsync(userId, messageId, message.MessageId, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save outgoing message {MessageId}", message.MessageId);
        }
    }
}
