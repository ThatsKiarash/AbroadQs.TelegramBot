namespace AbroadQs.Bot.Contracts;

/// <summary>
/// Repository for storing and retrieving Telegram messages.
/// </summary>
public interface IMessageRepository
{
    // Save incoming message (from user)
    Task<int> SaveIncomingMessageAsync(IncomingMessageInfo message, long updateId, CancellationToken cancellationToken = default);
    
    // Save outgoing message (from bot)
    Task<int> SaveOutgoingMessageAsync(OutgoingMessageInfo message, CancellationToken cancellationToken = default);
    
    // Update message (when edited/deleted)
    Task UpdateMessageAsync(long telegramMessageId, long chatId, Action<MessageUpdateInfo> updateAction, CancellationToken cancellationToken = default);
    
    // Get messages for a user
    Task<IReadOnlyList<MessageDto>> GetUserMessagesAsync(long telegramUserId, int? limit = null, CancellationToken cancellationToken = default);
    
    // Get conversation (messages between user and bot)
    Task<IReadOnlyList<MessageDto>> GetConversationAsync(long telegramUserId, int? limit = null, CancellationToken cancellationToken = default);
    
    // Get last bot message for user
    Task<MessageDto?> GetLastBotMessageAsync(long telegramUserId, CancellationToken cancellationToken = default);
}

public sealed record MessageUpdateInfo
{
    public bool? IsEdited { get; set; }
    public DateTimeOffset? EditedAt { get; set; }
    public bool? IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public bool? ShouldEdit { get; set; }
    public bool? ShouldDelete { get; set; }
    public bool? ShouldForward { get; set; }
    public bool? ShouldKeepForEdit { get; set; }
    public bool? DeleteNextMessages { get; set; }
}

public sealed record MessageDto(
    int Id,
    long TelegramMessageId,
    long TelegramChatId,
    long? TelegramUserId,
    bool IsFromBot,
    string? Text,
    string? MessageType,
    DateTimeOffset SentAt,
    DateTimeOffset? EditedAt,
    DateTimeOffset? DeletedAt,
    int? ReplyToMessageId,
    long? ForwardFromChatId,
    long? ForwardFromMessageId,
    bool HasReplyKeyboard,
    bool HasInlineKeyboard,
    string? InlineKeyboardId,
    bool ShouldEdit,
    bool ShouldDelete,
    bool ShouldForward,
    bool ShouldKeepForEdit,
    bool DeleteNextMessages);

public sealed record IncomingMessageInfo(
    int TelegramMessageId,
    long TelegramChatId,
    long? TelegramUserId,
    string? Text,
    string? MessageType,
    DateTimeOffset SentAt,
    int? ReplyToTelegramMessageId,
    long? ForwardFromChatId,
    long? ForwardFromMessageId,
    bool HasReplyKeyboard,
    bool HasInlineKeyboard,
    string? InlineKeyboardId);

public sealed record OutgoingMessageInfo(
    int TelegramMessageId,
    long TelegramChatId,
    long? TelegramUserId,
    string? Text,
    string? MessageType,
    DateTimeOffset SentAt,
    int? ReplyToTelegramMessageId,
    long? ForwardFromChatId,
    long? ForwardFromMessageId,
    bool HasReplyKeyboard,
    bool HasInlineKeyboard,
    string? InlineKeyboardId);
