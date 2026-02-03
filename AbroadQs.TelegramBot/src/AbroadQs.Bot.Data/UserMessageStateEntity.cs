namespace AbroadQs.Bot.Data;

/// <summary>
/// آخرین وضعیت پیام ربات برای هر کاربر - برای مدیریت پیام‌های بعدی
/// </summary>
public sealed class UserMessageStateEntity
{
    public int Id { get; set; }
    public long TelegramUserId { get; set; }
    
    // Last bot message sent to this user
    public int? LastBotMessageId { get; set; } // FK to MessageEntity.Id
    public long? LastBotTelegramMessageId { get; set; }
    
    // State flags
    public bool ShouldEdit { get; set; } // آیا این پیام باید ادیت شود؟
    public bool ShouldReply { get; set; } // آیا باید جواب داده شود؟
    public bool ShouldKeepStatic { get; set; } // آیا باید ثابت بماند و ادیت شود؟
    public bool DeleteNextMessages { get; set; } // آیا پیام‌های بعدی باید پاک شوند؟
    
    // What happened to the message
    public string? LastAction { get; set; } // Sent, Edited, Deleted, Forwarded, etc.
    public DateTimeOffset? LastActionAt { get; set; }
    
    // Navigation
    public MessageEntity? LastBotMessage { get; set; }
    public TelegramUserEntity? User { get; set; }
}
