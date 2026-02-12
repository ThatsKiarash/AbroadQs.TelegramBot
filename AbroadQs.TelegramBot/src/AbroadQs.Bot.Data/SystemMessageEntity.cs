namespace AbroadQs.Bot.Data;

/// <summary>Phase 4: System messages (notifications) sent to users automatically on events.</summary>
public sealed class SystemMessageEntity
{
    public int Id { get; set; }
    public long TelegramUserId { get; set; }
    public string? TitleFa { get; set; }
    public string? TitleEn { get; set; }
    public string? BodyFa { get; set; }
    public string? BodyEn { get; set; }
    /// <summary>Category: info, warning, success.</summary>
    public string Category { get; set; } = "info";
    public bool IsRead { get; set; }
    /// <summary>Related entity type, e.g. "exchange_request", "bid", "payment".</summary>
    public string? RelatedEntityType { get; set; }
    /// <summary>Related entity ID.</summary>
    public int? RelatedEntityId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
