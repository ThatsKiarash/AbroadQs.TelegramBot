namespace AbroadQs.Bot.Data;

/// <summary>Phase 4: Support ticket.</summary>
public sealed class TicketEntity
{
    public int Id { get; set; }
    public long TelegramUserId { get; set; }
    public string Subject { get; set; } = "";
    /// <summary>Status: open, in_progress, closed.</summary>
    public string Status { get; set; } = "open";
    /// <summary>Priority: low, medium, high.</summary>
    public string Priority { get; set; } = "medium";
    public string? Category { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
}

/// <summary>Phase 4: A single message within a ticket conversation.</summary>
public sealed class TicketMessageEntity
{
    public int Id { get; set; }
    public int TicketId { get; set; }
    /// <summary>Sender type: user, admin.</summary>
    public string SenderType { get; set; } = "user";
    public string? SenderName { get; set; }
    public string? Text { get; set; }
    public string? FileId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public TicketEntity? Ticket { get; set; }
}
