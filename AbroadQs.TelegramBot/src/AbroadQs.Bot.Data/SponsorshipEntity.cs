namespace AbroadQs.Bot.Data;

/// <summary>Phase 7: A request for financial sponsorship on a project.</summary>
public sealed class SponsorshipRequestEntity
{
    public int Id { get; set; }
    public int? ProjectId { get; set; }
    public long RequesterTelegramUserId { get; set; }
    public decimal RequestedAmount { get; set; }
    public decimal ProfitSharePercent { get; set; }
    public DateTimeOffset? Deadline { get; set; }
    public string? Description { get; set; }
    /// <summary>Status: open, funded, completed, defaulted.</summary>
    public string Status { get; set; } = "open";
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Phase 7: A sponsorship (an investor funding a request).</summary>
public sealed class SponsorshipEntity
{
    public int Id { get; set; }
    public int RequestId { get; set; }
    public long SponsorTelegramUserId { get; set; }
    public decimal Amount { get; set; }
    /// <summary>Status: active, profit_paid, defaulted.</summary>
    public string Status { get; set; } = "active";
    public DateTimeOffset CreatedAt { get; set; }

    public SponsorshipRequestEntity? Request { get; set; }
}
