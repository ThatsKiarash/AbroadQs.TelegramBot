using AbroadQs.Bot.Contracts;
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

    public UpdateDispatcher(IEnumerable<IUpdateHandler> handlers, ILogger<UpdateDispatcher> logger, IProcessingContext? processingContext = null)
    {
        _handlers = handlers?.OrderBy(h => h.Command == null ? 1 : 0).ToList() ?? [];
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _processingContext = processingContext;
    }

    public async Task DispatchAsync(Update update, CancellationToken cancellationToken = default)
    {
        var context = BuildContext(update);
        if (context == null)
        {
            _logger.LogWarning("Update {UpdateId} produced no context (no Message/CallbackQuery?), skipping", update.Id);
            return;
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

        if (update.Message != null)
        {
            chatId = update.Message.Chat.Id;
            userId = update.Message.From?.Id;
            messageText = update.Message.Text;
            username = update.Message.From?.Username;
            firstName = update.Message.From?.FirstName;
            lastName = update.Message.From?.LastName;
        }
        else if (update.CallbackQuery != null)
        {
            chatId = update.CallbackQuery.Message?.Chat.Id ?? update.CallbackQuery.From.Id;
            userId = update.CallbackQuery.From.Id;
            messageText = update.CallbackQuery.Data;
            username = update.CallbackQuery.From.Username;
            firstName = update.CallbackQuery.From.FirstName;
            lastName = update.CallbackQuery.From.LastName;
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
            RawUpdate = update
        };
    }
}
