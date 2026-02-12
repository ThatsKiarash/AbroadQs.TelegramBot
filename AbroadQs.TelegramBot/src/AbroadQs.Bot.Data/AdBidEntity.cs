namespace AbroadQs.Bot.Data;

/// <summary>
/// A bid on a channel-posted exchange ad (auction style).
/// </summary>
public sealed class AdBidEntity
{
    public int Id { get; set; }
    public int ExchangeRequestId { get; set; }
    public long BidderTelegramUserId { get; set; }
    public string? BidderDisplayName { get; set; }
    /// <summary>Bid amount in the ad's currency.</summary>
    public decimal BidAmount { get; set; }
    /// <summary>Bidder's proposed rate (Toman per unit).</summary>
    public decimal BidRate { get; set; }
    /// <summary>Optional message from the bidder.</summary>
    public string? Message { get; set; }
    /// <summary>"pending", "accepted", "rejected".</summary>
    public string Status { get; set; } = "pending";
    /// <summary>Telegram message ID of the bid reply in the channel.</summary>
    public int? ChannelReplyMessageId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public ExchangeRequestEntity? ExchangeRequest { get; set; }
}
