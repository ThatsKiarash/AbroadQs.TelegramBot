namespace AbroadQs.Bot.Data;

public sealed class MessageEntity
{
    public int Id { get; set; }
    public long TelegramMessageId { get; set; }
    public long TelegramChatId { get; set; }
    public long? TelegramUserId { get; set; }
    
    // Message direction
    public bool IsFromBot { get; set; } // true = bot sent, false = user sent
    
    // Message content
    public string? Text { get; set; }
    public string? MessageType { get; set; } // Text, Photo, Document, etc.
    
    // Message metadata
    public DateTimeOffset SentAt { get; set; }
    public DateTimeOffset? EditedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    
    // Message relationships
    public int? ReplyToMessageId { get; set; } // FK to MessageEntity.Id
    public long? ForwardFromChatId { get; set; }
    public long? ForwardFromMessageId { get; set; }
    
    // Keyboard/Inline keyboard
    public bool HasReplyKeyboard { get; set; }
    public bool HasInlineKeyboard { get; set; }
    public string? InlineKeyboardId { get; set; } // Custom identifier for keyboard
    
    // Message state flags
    public bool ShouldEdit { get; set; }
    public bool ShouldDelete { get; set; }
    public bool ShouldForward { get; set; }
    public bool ShouldKeepForEdit { get; set; } // Keep message for future edits
    public bool DeleteNextMessages { get; set; } // Delete subsequent messages
    
    // Update tracking
    public long? UpdateId { get; set; } // Telegram Update ID
    
    // Navigation
    public MessageEntity? ReplyToMessage { get; set; }
    public TelegramUserEntity? User { get; set; }
}
