namespace AbroadQs.Bot.Data;

/// <summary>Phase 8: Crypto wallet saved by a user (address book style).</summary>
public sealed class CryptoWalletEntity
{
    public int Id { get; set; }
    public long TelegramUserId { get; set; }
    /// <summary>CurrencySymbol: USDT, BTC, ETH, SOL, etc.</summary>
    public string CurrencySymbol { get; set; } = "USDT";
    /// <summary>Network: TRC20, ERC20, BTC, SOL, etc.</summary>
    public string Network { get; set; } = "TRC20";
    public string WalletAddress { get; set; } = "";
    public string? Label { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Phase 8: A currency purchase order.</summary>
public sealed class CurrencyPurchaseEntity
{
    public int Id { get; set; }
    public long TelegramUserId { get; set; }
    /// <summary>CurrencySymbol: USD, EUR, USDT, BTC, etc.</summary>
    public string CurrencySymbol { get; set; } = "USDT";
    public decimal Amount { get; set; }
    /// <summary>Total price in Toman.</summary>
    public decimal TotalPrice { get; set; }
    /// <summary>Status: pending, processing, completed, rejected.</summary>
    public string Status { get; set; } = "pending";
    public string? TxHash { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
