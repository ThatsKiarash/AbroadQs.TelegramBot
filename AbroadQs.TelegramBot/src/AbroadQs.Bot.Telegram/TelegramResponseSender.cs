using AbroadQs.Bot.Contracts;
using AbroadQs.Bot.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

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

    public async Task SendTextMessageWithInlineKeyboardAsync(long chatId, string text, IReadOnlyList<IReadOnlyList<InlineButton>> inlineKeyboard, CancellationToken cancellationToken = default)
    {
        try
        {
            var rows = inlineKeyboard.Select(row => row.Select(b =>
            {
                if (!string.IsNullOrEmpty(b.Url))
                    return InlineKeyboardButton.WithUrl(b.Text, b.Url);
                return InlineKeyboardButton.WithCallbackData(b.Text, b.CallbackData ?? "");
            }).ToList()).ToList();
            var markup = new InlineKeyboardMarkup(rows);

            var result = await _client.SendMessage(
                new ChatId(chatId),
                text,
                parseMode: ParseMode.Html,
                replyMarkup: markup,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (result != null)
            {
                _logger.LogInformation("Message with inline keyboard sent to chat {ChatId}, messageId: {MessageId}", chatId, result.MessageId);
                if (_processingContext != null)
                    _processingContext.ResponseSent = true;
                await SaveOutgoingMessageAsync(result, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                _logger.LogWarning("Message with keyboard not sent to chat {ChatId} - bot client returned null", chatId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send message with keyboard to chat {ChatId}", chatId);
            throw;
        }
    }

    public async Task SendTextMessageWithReplyKeyboardAsync(long chatId, string text, IReadOnlyList<IReadOnlyList<string>> keyboard, CancellationToken cancellationToken = default)
    {
        try
        {
            var rows = keyboard.Select(row => new KeyboardButton[row.Count]).ToList();
            for (int r = 0; r < keyboard.Count; r++)
                for (int c = 0; c < keyboard[r].Count; c++)
                    rows[r][c] = new KeyboardButton(keyboard[r][c]);

            var markup = new ReplyKeyboardMarkup(rows)
            {
                ResizeKeyboard = true,
                IsPersistent = true,
                OneTimeKeyboard = false,
                InputFieldPlaceholder = "یک گزینه را انتخاب کنید..."
            };

            var result = await _client.SendMessage(
                new ChatId(chatId),
                text,
                parseMode: ParseMode.Html,
                replyMarkup: markup,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (result != null)
            {
                _logger.LogInformation("Message with reply keyboard sent to chat {ChatId}, messageId: {MessageId}", chatId, result.MessageId);
                if (_processingContext != null)
                    _processingContext.ResponseSent = true;
                await SaveOutgoingMessageAsync(result, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                _logger.LogWarning("Message with reply keyboard not sent to chat {ChatId} - bot client returned null", chatId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send message with reply keyboard to chat {ChatId}", chatId);
            throw;
        }
    }

    public async Task EditMessageTextWithInlineKeyboardAsync(long chatId, int messageId, string text, IReadOnlyList<IReadOnlyList<InlineButton>> inlineKeyboard, CancellationToken cancellationToken = default)
    {
        try
        {
            var rows = inlineKeyboard.Select(row => row.Select(b =>
            {
                if (!string.IsNullOrEmpty(b.Url))
                    return InlineKeyboardButton.WithUrl(b.Text, b.Url);
                return InlineKeyboardButton.WithCallbackData(b.Text, b.CallbackData ?? "");
            }).ToList()).ToList();
            var markup = new InlineKeyboardMarkup(rows);

            await _client.EditMessageText(
                new ChatId(chatId),
                messageId,
                text,
                parseMode: ParseMode.Html,
                replyMarkup: markup,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Message edited in chat {ChatId}, messageId {MessageId}", chatId, messageId);
            if (_processingContext != null)
                _processingContext.ResponseSent = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to edit message {MessageId} in chat {ChatId}", messageId, chatId);
            throw;
        }
    }

    public async Task EditMessageTextAsync(long chatId, int messageId, string text, CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.EditMessageText(
                new ChatId(chatId),
                messageId,
                text,
                parseMode: ParseMode.Html,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Message text edited in chat {ChatId}, messageId {MessageId}", chatId, messageId);
            if (_processingContext != null)
                _processingContext.ResponseSent = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to edit message text {MessageId} in chat {ChatId}", messageId, chatId);
            throw;
        }
    }

    public async Task UpdateReplyKeyboardSilentAsync(long chatId, IReadOnlyList<IReadOnlyList<string>> keyboard, CancellationToken cancellationToken = default)
    {
        try
        {
            var rows = keyboard.Select(row => new KeyboardButton[row.Count]).ToList();
            for (int r = 0; r < keyboard.Count; r++)
                for (int c = 0; c < keyboard[r].Count; c++)
                    rows[r][c] = new KeyboardButton(keyboard[r][c]);

            var markup = new ReplyKeyboardMarkup(rows)
            {
                ResizeKeyboard = true,
                IsPersistent = true,
                OneTimeKeyboard = false,
                InputFieldPlaceholder = "یک گزینه را انتخاب کنید..."
            };

            // Send phantom message to set the new keyboard
            var result = await _client.SendMessage(
                new ChatId(chatId),
                "\u200B", // zero-width space
                replyMarkup: markup,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            // Immediately delete the phantom — keyboard persists on the client
            if (result != null)
            {
                await _client.DeleteMessage(new ChatId(chatId), result.MessageId, cancellationToken: cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Reply keyboard silently updated in chat {ChatId}", chatId);
            }
            // Do NOT call SaveOutgoingMessageAsync — this is a phantom message
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to silently update reply keyboard in chat {ChatId}", chatId);
            // Swallow — keyboard update is best-effort
        }
    }

    public async Task DeleteMessageAsync(long chatId, int messageId, CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.DeleteMessage(new ChatId(chatId), messageId, cancellationToken: cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Message {MessageId} deleted from chat {ChatId}", messageId, chatId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete message {MessageId} in chat {ChatId} — swallowed to keep bot alive", messageId, chatId);
        }
    }

    public async Task AnswerCallbackQueryAsync(string callbackQueryId, string? message = null, CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.AnswerCallbackQuery(callbackQueryId, message, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to answer callback query {CallbackQueryId}", callbackQueryId);
        }
    }

    public async Task SendContactRequestAsync(long chatId, string text, string buttonLabel, string? cancelLabel = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var contactButton = KeyboardButton.WithRequestContact(buttonLabel);
            var rows = new List<KeyboardButton[]> { new[] { contactButton } };
            if (!string.IsNullOrEmpty(cancelLabel))
                rows.Add(new[] { new KeyboardButton(cancelLabel) });

            var markup = new ReplyKeyboardMarkup(rows)
            {
                ResizeKeyboard = true,
                IsPersistent = true,
                OneTimeKeyboard = false,
                InputFieldPlaceholder = "یک گزینه را انتخاب کنید..."
            };
            var result = await _client.SendMessage(
                new ChatId(chatId), text, parseMode: ParseMode.Html, replyMarkup: markup, cancellationToken: cancellationToken).ConfigureAwait(false);

            if (result != null)
            {
                if (_processingContext != null) _processingContext.ResponseSent = true;
                await SaveOutgoingMessageAsync(result, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send contact request to chat {ChatId}", chatId);
            throw;
        }
    }

    public async Task RemoveReplyKeyboardAsync(long chatId, string text, CancellationToken cancellationToken = default)
    {
        try
        {
            var markup = new ReplyKeyboardRemove();
            var result = await _client.SendMessage(
                new ChatId(chatId), text, parseMode: ParseMode.Html, replyMarkup: markup, cancellationToken: cancellationToken).ConfigureAwait(false);

            if (result != null)
            {
                if (_processingContext != null) _processingContext.ResponseSent = true;
                await SaveOutgoingMessageAsync(result, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove reply keyboard in chat {ChatId}", chatId);
            throw;
        }
    }

    public async Task RemoveReplyKeyboardSilentAsync(long chatId, CancellationToken cancellationToken = default)
    {
        try
        {
            var markup = new ReplyKeyboardRemove();
            // Send a zero-width space phantom message with ReplyKeyboardRemove, then immediately delete it
            var result = await _client.SendMessage(
                new ChatId(chatId), "\u200B", replyMarkup: markup, cancellationToken: cancellationToken).ConfigureAwait(false);

            if (result != null)
            {
                // Immediately delete the phantom — user should never see it
                try
                {
                    await _client.DeleteMessage(new ChatId(chatId), result.MessageId, cancellationToken: cancellationToken).ConfigureAwait(false);
                }
                catch { /* swallow delete failure */ }
            }
            // Do NOT save as outgoing message — this is a phantom
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to silently remove reply keyboard in chat {ChatId}", chatId);
            // Swallow — non-critical
        }
    }

    public async Task SendPhotoAsync(long chatId, string photoPath, string? caption = null, CancellationToken cancellationToken = default)
    {
        try
        {
            InputFile inputFile;
            if (photoPath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || photoPath.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                inputFile = InputFile.FromUri(photoPath);
            }
            else
            {
                var stream = System.IO.File.OpenRead(photoPath);
                inputFile = InputFile.FromStream(stream, Path.GetFileName(photoPath));
            }

            var result = await _client.SendPhoto(
                new ChatId(chatId), inputFile, caption: caption, parseMode: ParseMode.Html,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (result != null)
            {
                if (_processingContext != null) _processingContext.ResponseSent = true;
                await SaveOutgoingMessageAsync(result, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send photo to chat {ChatId}", chatId);
            throw;
        }
    }

    public async Task SendPhotoWithInlineKeyboardAsync(long chatId, string photoUrl, string? caption, IReadOnlyList<IReadOnlyList<InlineButton>>? inlineKeyboard = null, CancellationToken cancellationToken = default)
    {
        try
        {
            InputFile inputFile;
            if (photoUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || photoUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                inputFile = InputFile.FromUri(photoUrl);
            else
            {
                var stream = System.IO.File.OpenRead(photoUrl);
                inputFile = InputFile.FromStream(stream, Path.GetFileName(photoUrl));
            }

            InlineKeyboardMarkup? markup = null;
            if (inlineKeyboard != null)
            {
                var rows = inlineKeyboard.Select(row => row.Select(b =>
                {
                    if (!string.IsNullOrEmpty(b.Url))
                        return InlineKeyboardButton.WithUrl(b.Text, b.Url);
                    return InlineKeyboardButton.WithCallbackData(b.Text, b.CallbackData ?? "");
                }).ToList()).ToList();
                markup = new InlineKeyboardMarkup(rows);
            }

            var result = await _client.SendPhoto(
                new ChatId(chatId), inputFile, caption: caption, parseMode: ParseMode.Html,
                replyMarkup: markup, cancellationToken: cancellationToken).ConfigureAwait(false);

            if (result != null)
            {
                if (_processingContext != null) _processingContext.ResponseSent = true;
                await SaveOutgoingMessageAsync(result, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send photo with keyboard to chat {ChatId}", chatId);
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
