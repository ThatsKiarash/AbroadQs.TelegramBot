namespace AbroadQs.Bot.Data;

public sealed class WalletEntity
{
    public int Id { get; set; }
    public long TelegramUserId { get; set; }
    /// <summary>Current balance in Rials.</summary>
    public decimal Balance { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class WalletTransactionEntity
{
    public int Id { get; set; }
    public int WalletId { get; set; }
    /// <summary>Positive = credit, Negative = debit.</summary>
    public decimal Amount { get; set; }
    /// <summary>"credit" or "debit".</summary>
    public string Type { get; set; } = "";
    /// <summary>Human-readable description (e.g. "Ad posting fee", "Payment received").</summary>
    public string? Description { get; set; }
    /// <summary>Reference to related entity (e.g. PaymentId, ExchangeRequestId).</summary>
    public string? ReferenceId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public WalletEntity? Wallet { get; set; }
}

public sealed class PaymentEntity
{
    public int Id { get; set; }
    public long TelegramUserId { get; set; }
    /// <summary>Amount in Rials.</summary>
    public decimal Amount { get; set; }
    /// <summary>"bitpay".</summary>
    public string GatewayName { get; set; } = "bitpay";
    /// <summary>Gateway-specific transaction ID.</summary>
    public string? GatewayTransactionId { get; set; }
    /// <summary>BitPay id_get for redirect.</summary>
    public long? GatewayIdGet { get; set; }
    /// <summary>"pending", "success", "failed".</summary>
    public string Status { get; set; } = "pending";
    /// <summary>Purpose: "wallet_charge", "ad_fee", "commission".</summary>
    public string? Purpose { get; set; }
    /// <summary>Reference to related entity.</summary>
    public string? ReferenceId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? VerifiedAt { get; set; }
}
