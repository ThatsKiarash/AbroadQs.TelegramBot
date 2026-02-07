using AbroadQs.Bot.Contracts;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace AbroadQs.Bot.Data;

public sealed class MessageRepository : IMessageRepository
{
    /// <summary>Maximum number of messages kept per chat. Older messages are automatically deleted.</summary>
    private const int MaxMessagesPerUser = 5;

    private readonly ApplicationDbContext _db;
    private readonly IProcessingContext? _processingContext;

    public MessageRepository(ApplicationDbContext db, IProcessingContext? processingContext = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _processingContext = processingContext;
    }

    public async Task<int> SaveIncomingMessageAsync(IncomingMessageInfo message, long updateId, CancellationToken cancellationToken = default)
    {
        var entity = new MessageEntity
        {
            TelegramMessageId = message.TelegramMessageId,
            TelegramChatId = message.TelegramChatId,
            TelegramUserId = message.TelegramUserId,
            IsFromBot = false,
            Text = message.Text,
            MessageType = message.MessageType,
            SentAt = message.SentAt,
            ReplyToMessageId = message.ReplyToTelegramMessageId.HasValue ? await GetMessageIdByTelegramIdAsync(message.TelegramChatId, message.ReplyToTelegramMessageId.Value, cancellationToken).ConfigureAwait(false) : null,
            ForwardFromChatId = message.ForwardFromChatId,
            ForwardFromMessageId = message.ForwardFromMessageId,
            HasReplyKeyboard = message.HasReplyKeyboard,
            HasInlineKeyboard = message.HasInlineKeyboard,
            InlineKeyboardId = message.InlineKeyboardId,
            UpdateId = updateId
        };

        _db.Messages.Add(entity);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (_processingContext != null)
            _processingContext.SqlAccessed = true;

        // Keep only last MaxMessagesPerUser messages per user
        if (message.TelegramUserId.HasValue)
            await TrimOldMessagesAsync(message.TelegramChatId, cancellationToken).ConfigureAwait(false);

        return entity.Id;
    }

    public async Task<int> SaveOutgoingMessageAsync(OutgoingMessageInfo message, CancellationToken cancellationToken = default)
    {
        // Only set TelegramUserId if user exists (FK to TelegramUsers)
        long? userId = null;
        if (message.TelegramUserId.HasValue)
        {
            var userExists = await _db.TelegramUsers.AnyAsync(u => u.TelegramUserId == message.TelegramUserId.Value, cancellationToken).ConfigureAwait(false);
            if (userExists)
                userId = message.TelegramUserId;
        }

        var entity = new MessageEntity
        {
            TelegramMessageId = message.TelegramMessageId,
            TelegramChatId = message.TelegramChatId,
            TelegramUserId = userId,
            IsFromBot = true,
            Text = message.Text,
            MessageType = message.MessageType,
            SentAt = message.SentAt,
            ReplyToMessageId = message.ReplyToTelegramMessageId.HasValue ? await GetMessageIdByTelegramIdAsync(message.TelegramChatId, message.ReplyToTelegramMessageId.Value, cancellationToken).ConfigureAwait(false) : null,
            ForwardFromChatId = message.ForwardFromChatId,
            ForwardFromMessageId = message.ForwardFromMessageId,
            HasReplyKeyboard = message.HasReplyKeyboard,
            HasInlineKeyboard = message.HasInlineKeyboard,
            InlineKeyboardId = message.InlineKeyboardId
        };

        _db.Messages.Add(entity);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (_processingContext != null)
            _processingContext.SqlAccessed = true;

        // Keep only last MaxMessagesPerUser messages per chat
        await TrimOldMessagesAsync(message.TelegramChatId, cancellationToken).ConfigureAwait(false);

        return entity.Id;
    }

    public async Task UpdateMessageAsync(long telegramMessageId, long chatId, Action<MessageUpdateInfo> updateAction, CancellationToken cancellationToken = default)
    {
        var entity = await _db.Messages
            .FirstOrDefaultAsync(x => x.TelegramMessageId == telegramMessageId && x.TelegramChatId == chatId, cancellationToken)
            .ConfigureAwait(false);
        
        if (entity == null) return;

        var updateInfo = new MessageUpdateInfo();
        updateAction(updateInfo);

        if (updateInfo.IsEdited == true)
        {
            entity.EditedAt = updateInfo.EditedAt ?? DateTimeOffset.UtcNow;
        }
        if (updateInfo.IsDeleted == true)
        {
            entity.DeletedAt = updateInfo.DeletedAt ?? DateTimeOffset.UtcNow;
        }
        if (updateInfo.ShouldEdit.HasValue) entity.ShouldEdit = updateInfo.ShouldEdit.Value;
        if (updateInfo.ShouldDelete.HasValue) entity.ShouldDelete = updateInfo.ShouldDelete.Value;
        if (updateInfo.ShouldForward.HasValue) entity.ShouldForward = updateInfo.ShouldForward.Value;
        if (updateInfo.ShouldKeepForEdit.HasValue) entity.ShouldKeepForEdit = updateInfo.ShouldKeepForEdit.Value;
        if (updateInfo.DeleteNextMessages.HasValue) entity.DeleteNextMessages = updateInfo.DeleteNextMessages.Value;

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (_processingContext != null)
            _processingContext.SqlAccessed = true;
    }

    public async Task<IReadOnlyList<MessageDto>> GetUserMessagesAsync(long telegramUserId, int? limit = null, CancellationToken cancellationToken = default)
    {
        var baseQuery = _db.Messages
            .AsNoTracking()
            .Where(x => x.TelegramUserId == telegramUserId)
            .OrderByDescending(x => x.SentAt);

        var query = limit.HasValue ? baseQuery.Take(limit.Value) : baseQuery;

        var list = await query
            .Select(x => new MessageDto(
                x.Id,
                x.TelegramMessageId,
                x.TelegramChatId,
                x.TelegramUserId,
                x.IsFromBot,
                x.Text,
                x.MessageType,
                x.SentAt,
                x.EditedAt,
                x.DeletedAt,
                x.ReplyToMessageId,
                x.ForwardFromChatId,
                x.ForwardFromMessageId,
                x.HasReplyKeyboard,
                x.HasInlineKeyboard,
                x.InlineKeyboardId,
                x.ShouldEdit,
                x.ShouldDelete,
                x.ShouldForward,
                x.ShouldKeepForEdit,
                x.DeleteNextMessages))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return list;
    }

    public async Task<IReadOnlyList<MessageDto>> GetConversationAsync(long telegramUserId, int? limit = null, CancellationToken cancellationToken = default)
    {
        var user = await _db.TelegramUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.TelegramUserId == telegramUserId, cancellationToken)
            .ConfigureAwait(false);
        
        if (user == null) return Array.Empty<MessageDto>();

        var baseQuery = _db.Messages
            .AsNoTracking()
            .Where(x => x.TelegramChatId == user.TelegramUserId || x.TelegramUserId == telegramUserId)
            .OrderByDescending(x => x.SentAt);

        var query = limit.HasValue ? baseQuery.Take(limit.Value) : baseQuery;

        var list = await query
            .Select(x => new MessageDto(
                x.Id,
                x.TelegramMessageId,
                x.TelegramChatId,
                x.TelegramUserId,
                x.IsFromBot,
                x.Text,
                x.MessageType,
                x.SentAt,
                x.EditedAt,
                x.DeletedAt,
                x.ReplyToMessageId,
                x.ForwardFromChatId,
                x.ForwardFromMessageId,
                x.HasReplyKeyboard,
                x.HasInlineKeyboard,
                x.InlineKeyboardId,
                x.ShouldEdit,
                x.ShouldDelete,
                x.ShouldForward,
                x.ShouldKeepForEdit,
                x.DeleteNextMessages))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return list;
    }

    public async Task<MessageDto?> GetLastBotMessageAsync(long telegramUserId, CancellationToken cancellationToken = default)
    {
        var message = await _db.Messages
            .AsNoTracking()
            .Where(x => x.TelegramUserId == telegramUserId && x.IsFromBot)
            .OrderByDescending(x => x.SentAt)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (message == null) return null;

        return new MessageDto(
            message.Id,
            message.TelegramMessageId,
            message.TelegramChatId,
            message.TelegramUserId,
            message.IsFromBot,
            message.Text,
            message.MessageType,
            message.SentAt,
            message.EditedAt,
            message.DeletedAt,
            message.ReplyToMessageId,
            message.ForwardFromChatId,
            message.ForwardFromMessageId,
            message.HasReplyKeyboard,
            message.HasInlineKeyboard,
            message.InlineKeyboardId,
            message.ShouldEdit,
            message.ShouldDelete,
            message.ShouldForward,
            message.ShouldKeepForEdit,
            message.DeleteNextMessages);
    }

    /// <summary>
    /// Remove old messages for a chat, keeping only the most recent <see cref="MaxMessagesPerUser"/>.
    /// Uses raw SQL for efficiency (single DELETE instead of loading entities).
    /// </summary>
    private async Task TrimOldMessagesAsync(long chatId, CancellationToken cancellationToken)
    {
        try
        {
            // Delete all messages for this chat that are NOT in the top-N most recent
            var deleted = await _db.Database.ExecuteSqlRawAsync(
                @"DELETE FROM Messages
                  WHERE TelegramChatId = {0}
                    AND Id NOT IN (
                        SELECT TOP({1}) Id
                        FROM Messages
                        WHERE TelegramChatId = {0}
                        ORDER BY SentAt DESC
                    )", chatId, MaxMessagesPerUser, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Non-critical — don't break the main flow if cleanup fails
        }
    }

    private async Task<int?> GetMessageIdByTelegramIdAsync(long chatId, int telegramMessageId, CancellationToken cancellationToken)
    {
        var msg = await _db.Messages
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.TelegramChatId == chatId && x.TelegramMessageId == telegramMessageId, cancellationToken)
            .ConfigureAwait(false);
        return msg?.Id;
    }

    private static string GetMessageType(Telegram.Bot.Types.Message message)
    {
        if (message.Text != null) return "Text";
        if (message.Photo != null) return "Photo";
        if (message.Document != null) return "Document";
        if (message.Video != null) return "Video";
        if (message.Audio != null) return "Audio";
        if (message.Voice != null) return "Voice";
        if (message.Sticker != null) return "Sticker";
        if (message.Location != null) return "Location";
        if (message.Contact != null) return "Contact";
        return "Unknown";
    }

    private static string? GetInlineKeyboardId(object? markup)
    {
        if (markup == null) return null;
        var markupType = markup.GetType();
        if (markupType.Name == "InlineKeyboardMarkup")
        {
            // Use reflection to access InlineKeyboard property
            var inlineKeyboardProp = markupType.GetProperty("InlineKeyboard");
            if (inlineKeyboardProp?.GetValue(markup) is System.Collections.IEnumerable keyboard)
            {
                var buttons = new List<string>();
                foreach (var row in keyboard)
                {
                    if (row is System.Collections.IEnumerable rowItems)
                    {
                        foreach (var btn in rowItems)
                        {
                            var textProp = btn?.GetType().GetProperty("Text");
                            if (textProp?.GetValue(btn) is string text)
                                buttons.Add(text);
                        }
                    }
                }
                if (buttons.Count > 0)
                    return string.Join("|", buttons.Take(3)); // First 3 buttons as ID
            }
        }
        return null;
    }

    // Helper methods to convert Telegram.Bot.Types.Message to DTOs
    public static IncomingMessageInfo ToIncomingMessageInfo(Telegram.Bot.Types.Message message)
    {
        return new IncomingMessageInfo(
            message.MessageId,
            message.Chat.Id,
            message.From?.Id,
            message.Text,
            GetMessageType(message),
            new DateTimeOffset(message.Date, TimeSpan.Zero),
            message.ReplyToMessage?.MessageId,
            message.ForwardFromChat?.Id,
            message.ForwardFromMessageId,
            message.ReplyMarkup?.GetType().Name == "ReplyKeyboardMarkup",
            message.ReplyMarkup?.GetType().Name == "InlineKeyboardMarkup",
            GetInlineKeyboardId(message.ReplyMarkup));
    }

    public static OutgoingMessageInfo ToOutgoingMessageInfo(Telegram.Bot.Types.Message message)
    {
        return new OutgoingMessageInfo(
            message.MessageId,
            message.Chat.Id,
            message.From?.Id,
            message.Text,
            GetMessageType(message),
            new DateTimeOffset(message.Date, TimeSpan.Zero),
            message.ReplyToMessage?.MessageId,
            message.ForwardFromChat?.Id,
            message.ForwardFromMessageId,
            message.ReplyMarkup?.GetType().Name == "ReplyKeyboardMarkup",
            message.ReplyMarkup?.GetType().Name == "InlineKeyboardMarkup",
            GetInlineKeyboardId(message.ReplyMarkup));
    }
}
