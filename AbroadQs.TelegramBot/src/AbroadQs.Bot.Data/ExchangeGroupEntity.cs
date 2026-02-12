namespace AbroadQs.Bot.Data;

/// <summary>
/// A Telegram group for currency exchange, categorized by currency or country.
/// </summary>
public sealed class ExchangeGroupEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    /// <summary>Telegram numeric group/channel ID (optional).</summary>
    public long? TelegramGroupId { get; set; }
    /// <summary>Invite link (t.me/... or @groupname).</summary>
    public string TelegramGroupLink { get; set; } = "";
    /// <summary>"currency", "country", "general".</summary>
    public string GroupType { get; set; } = "general";
    /// <summary>Currency code if GroupType is "currency".</summary>
    public string? CurrencyCode { get; set; }
    /// <summary>Country code if GroupType is "country".</summary>
    public string? CountryCode { get; set; }
    public string? Description { get; set; }
    public int MemberCount { get; set; }
    public long? SubmittedByUserId { get; set; }
    /// <summary>"pending", "approved", "rejected".</summary>
    public string Status { get; set; } = "pending";
    public bool IsOfficial { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}
