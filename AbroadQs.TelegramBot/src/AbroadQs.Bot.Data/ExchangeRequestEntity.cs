namespace AbroadQs.Bot.Data;

/// <summary>
/// A currency exchange request created by a user through the bot.
/// </summary>
public sealed class ExchangeRequestEntity
{
    public int Id { get; set; }
    /// <summary>Auto-increment display number (e.g. #6773).</summary>
    public int RequestNumber { get; set; }
    public long TelegramUserId { get; set; }
    /// <summary>Currency code, e.g. "USD", "EUR".</summary>
    public string Currency { get; set; } = "";
    /// <summary>"buy", "sell", or "exchange".</summary>
    public string TransactionType { get; set; } = "";
    /// <summary>"bank", "cash", or "paypal".</summary>
    public string DeliveryMethod { get; set; } = "";
    /// <summary>"personal" or "company" (for bank transfers); null otherwise.</summary>
    public string? AccountType { get; set; }
    /// <summary>Destination country (for bank transfers).</summary>
    public string? Country { get; set; }
    /// <summary>Amount in the selected currency.</summary>
    public decimal Amount { get; set; }
    /// <summary>User's proposed exchange rate (Toman per unit).</summary>
    public decimal ProposedRate { get; set; }
    /// <summary>Optional user description/notes.</summary>
    public string? Description { get; set; }
    /// <summary>Fee percentage applied at submission time.</summary>
    public decimal FeePercent { get; set; }
    /// <summary>Calculated fee amount in Toman.</summary>
    public decimal FeeAmount { get; set; }
    /// <summary>Total amount in Toman (Amount * Rate +/- Fee).</summary>
    public decimal TotalAmount { get; set; }
    /// <summary>"pending_approval", "approved", "rejected", "cancelled".</summary>
    public string Status { get; set; } = "pending_approval";
    /// <summary>Telegram message ID of the channel post (after approval).</summary>
    public int? ChannelMessageId { get; set; }
    /// <summary>Admin note (e.g. rejection reason).</summary>
    public string? AdminNote { get; set; }
    /// <summary>User's display name at submission time.</summary>
    public string? UserDisplayName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}
