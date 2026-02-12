namespace AbroadQs.Bot.Data;

/// <summary>Phase 5: Student project marketplace listing.</summary>
public sealed class StudentProjectEntity
{
    public int Id { get; set; }
    public long TelegramUserId { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    /// <summary>Category: web, mobile, data, design, other.</summary>
    public string Category { get; set; } = "other";
    public decimal Budget { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset? Deadline { get; set; }
    public string? RequiredSkills { get; set; }
    /// <summary>Status: pending_approval, approved, in_progress, completed, cancelled.</summary>
    public string Status { get; set; } = "pending_approval";
    public int? ChannelMessageId { get; set; }
    public long? AssignedToUserId { get; set; }
    public string? AdminNote { get; set; }
    public string? UserDisplayName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>Phase 5: Bid/proposal on a student project.</summary>
public sealed class ProjectBidEntity
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public long BidderTelegramUserId { get; set; }
    public string? BidderDisplayName { get; set; }
    public decimal ProposedAmount { get; set; }
    public string? ProposedDuration { get; set; }
    public string? CoverLetter { get; set; }
    public string? PortfolioLink { get; set; }
    /// <summary>Status: pending, accepted, rejected.</summary>
    public string Status { get; set; } = "pending";
    public DateTimeOffset CreatedAt { get; set; }

    public StudentProjectEntity? Project { get; set; }
}
