using AbroadQs.Bot.Contracts;
using AbroadQs.Bot.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;

namespace AbroadQs.Bot.Application;

/// <summary>
/// Dispatches incoming Telegram updates to registered handlers in order.
/// Handlers are ordered by registration; first handler that CanHandle and returns true wins.
/// </summary>
public sealed class UpdateDispatcher
{
    private readonly IEnumerable<IUpdateHandler> _handlers;
    private readonly ILogger<UpdateDispatcher> _logger;
    private readonly IProcessingContext? _processingContext;
    private readonly IServiceScopeFactory? _scopeFactory;

    public UpdateDispatcher(IEnumerable<IUpdateHandler> handlers, ILogger<UpdateDispatcher> logger, IProcessingContext? processingContext = null, IServiceScopeFactory? scopeFactory = null)
    {
        _handlers = handlers?.OrderBy(h => h.Command == null ? 1 : 0).ToList() ?? [];
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _processingContext = processingContext;
        _scopeFactory = scopeFactory;
    }

    public async Task DispatchAsync(Update update, CancellationToken cancellationToken = default)
    {
        var context = BuildContext(update);
        if (context == null)
        {
            _logger.LogWarning("Update {UpdateId} produced no context (no Message/CallbackQuery?), skipping", update.Id);
            return;
        }

        // ── Phase 1.3: Anti-spam — update_id deduplication ───────────
        if (context.UserId.HasValue && _scopeFactory != null)
        {
            try
            {
                using var dedupScope = _scopeFactory.CreateScope();
                var lastCmdStore = dedupScope.ServiceProvider.GetService<IUserLastCommandStore>();
                if (lastCmdStore != null)
                {
                    // Check update_id dedup
                    var lastUpdateKey = $"last_upd:{context.UserId.Value}";
                    var lastUpdateId = await lastCmdStore.GetLastCommandAsync(context.UserId.Value + 9_000_000_000L, cancellationToken).ConfigureAwait(false);
                    if (lastUpdateId == update.Id.ToString())
                    {
                        _logger.LogDebug("Duplicate update {UpdateId} for user {UserId}, skipping", update.Id, context.UserId);
                        return;
                    }
                    await lastCmdStore.SetLastCommandAsync(context.UserId.Value + 9_000_000_000L, update.Id.ToString(), cancellationToken).ConfigureAwait(false);

                    // Callback query dedup: lock per user+callback for 3s
                    if (context.IsCallbackQuery && !string.IsNullOrEmpty(context.MessageText))
                    {
                        var cbKey = $"cb_lock:{context.UserId.Value}:{context.MessageText}";
                        var cbLock = await lastCmdStore.GetLastCommandAsync(context.UserId.Value + 8_000_000_000L, cancellationToken).ConfigureAwait(false);
                        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                        if (cbLock != null && long.TryParse(cbLock, out var lockTs) && now - lockTs < 3000)
                        {
                            _logger.LogDebug("Anti-spam: callback {Cb} blocked for user {UserId}", context.MessageText, context.UserId);
                            // Answer callback to remove loading spinner
                            try
                            {
                                using var ansScope = _scopeFactory.CreateScope();
                                var sender = ansScope.ServiceProvider.GetService<IResponseSender>();
                                if (sender != null && context.CallbackQueryId != null)
                                    await sender.AnswerCallbackQueryAsync(context.CallbackQueryId, "لطفا صبر کنید...", cancellationToken).ConfigureAwait(false);
                            }
                            catch { }
                            return;
                        }
                        await lastCmdStore.SetLastCommandAsync(context.UserId.Value + 8_000_000_000L, now.ToString(), cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Anti-spam check failed for update {UpdateId}, continuing", update.Id);
            }
        }

        // Save/update user and message on every interaction
        if (context.UserId.HasValue && _scopeFactory != null)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var userRepo = scope.ServiceProvider.GetService<ITelegramUserRepository>();
                if (userRepo != null)
                    await userRepo.SaveOrUpdateAsync(context.UserId.Value, context.Username, context.FirstName, context.LastName, cancellationToken).ConfigureAwait(false);

                // Save incoming message
                var messageRepo = scope.ServiceProvider.GetService<IMessageRepository>();
                if (messageRepo != null && update.Message != null)
                {
                    var messageInfo = MessageRepository.ToIncomingMessageInfo(update.Message);
                    await messageRepo.SaveIncomingMessageAsync(messageInfo, update.Id, cancellationToken).ConfigureAwait(false);
                }
                // Handle edited message
                else if (messageRepo != null && update.EditedMessage != null)
                {
                    await messageRepo.UpdateMessageAsync(
                        update.EditedMessage.MessageId,
                        update.EditedMessage.Chat.Id,
                        info => { info.IsEdited = true; info.EditedAt = DateTimeOffset.UtcNow; },
                        cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to save user/message for update {UpdateId}", update.Id);
            }
        }

        foreach (var handler in _handlers)
        {
            if (!handler.CanHandle(context))
                continue;

            try
            {
                var handled = await handler.HandleAsync(context, cancellationToken).ConfigureAwait(false);
                if (handled)
                {
                    _logger.LogInformation("Update {UpdateId} handled by {Handler}", update.Id, handler.GetType().Name);
                    if (_processingContext != null)
                        _processingContext.HandlerName = handler.GetType().Name;
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Handler {Handler} failed for update {UpdateId}", handler.GetType().Name, update.Id);
                throw;
            }
        }

        _logger.LogDebug("No handler claimed update {UpdateId}", update.Id);
    }

    private static BotUpdateContext? BuildContext(Update update)
    {
        long chatId;
        long? userId = null;
        string? messageText = null;
        string? username = null;
        string? firstName = null;
        string? lastName = null;
        bool isCallbackQuery = false;
        int? callbackMessageId = null;
        string? callbackQueryId = null;
        string? contactPhoneNumber = null;
        string? photoFileId = null;

        int? incomingMessageId = null;

        if (update.Message != null)
        {
            chatId = update.Message.Chat.Id;
            userId = update.Message.From?.Id;
            messageText = update.Message.Text;
            username = update.Message.From?.Username;
            firstName = update.Message.From?.FirstName;
            lastName = update.Message.From?.LastName;
            incomingMessageId = update.Message.MessageId;

            // Extract contact phone number if present
            if (update.Message.Contact != null)
                contactPhoneNumber = update.Message.Contact.PhoneNumber;

            // Extract largest photo file ID if present
            if (update.Message.Photo != null && update.Message.Photo.Length > 0)
                photoFileId = update.Message.Photo[^1].FileId; // last element = largest
        }
        else if (update.CallbackQuery != null)
        {
            chatId = update.CallbackQuery.Message?.Chat.Id ?? update.CallbackQuery.From.Id;
            userId = update.CallbackQuery.From.Id;
            messageText = update.CallbackQuery.Data;
            username = update.CallbackQuery.From.Username;
            firstName = update.CallbackQuery.From.FirstName;
            lastName = update.CallbackQuery.From.LastName;
            isCallbackQuery = true;
            callbackMessageId = update.CallbackQuery.Message?.MessageId;
            callbackQueryId = update.CallbackQuery.Id;
        }
        else
        {
            return null;
        }

        return new BotUpdateContext
        {
            UpdateId = update.Id,
            ChatId = chatId,
            UserId = userId,
            MessageText = messageText,
            Username = username,
            FirstName = firstName,
            LastName = lastName,
            IncomingMessageId = incomingMessageId,
            IsCallbackQuery = isCallbackQuery,
            CallbackMessageId = callbackMessageId,
            CallbackQueryId = callbackQueryId,
            ContactPhoneNumber = contactPhoneNumber,
            PhotoFileId = photoFileId,
            RawUpdate = update
        };
    }
}
