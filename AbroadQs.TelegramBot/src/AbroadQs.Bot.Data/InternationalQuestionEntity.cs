namespace AbroadQs.Bot.Data;

/// <summary>Phase 6: International question with optional bounty.</summary>
public sealed class InternationalQuestionEntity
{
    public int Id { get; set; }
    public long TelegramUserId { get; set; }
    public string QuestionText { get; set; } = "";
    public string? TargetCountry { get; set; }
    public decimal BountyAmount { get; set; }
    /// <summary>Status: open, answered, closed.</summary>
    public string Status { get; set; } = "open";
    public int? ChannelMessageId { get; set; }
    public string? UserDisplayName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>Phase 6: An answer to an international question.</summary>
public sealed class QuestionAnswerEntity
{
    public int Id { get; set; }
    public int QuestionId { get; set; }
    public long AnswererTelegramUserId { get; set; }
    public string AnswerText { get; set; } = "";
    /// <summary>Status: pending, accepted, rejected.</summary>
    public string Status { get; set; } = "pending";
    public DateTimeOffset CreatedAt { get; set; }

    public InternationalQuestionEntity? Question { get; set; }
}
