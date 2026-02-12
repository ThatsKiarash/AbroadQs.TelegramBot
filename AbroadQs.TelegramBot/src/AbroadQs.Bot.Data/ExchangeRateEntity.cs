namespace AbroadQs.Bot.Data;

/// <summary>
/// Cached currency exchange rate (from Navasan API or manual entry).
/// </summary>
public sealed class ExchangeRateEntity
{
    public int Id { get; set; }
    /// <summary>Navasan API item key, e.g. "usd_sell", "eur".</summary>
    public string CurrencyCode { get; set; } = "";
    /// <summary>Farsi display name, e.g. "دلار آمریکا".</summary>
    public string? CurrencyNameFa { get; set; }
    /// <summary>English display name, e.g. "US Dollar".</summary>
    public string? CurrencyNameEn { get; set; }
    /// <summary>Rate value (Toman).</summary>
    public decimal Rate { get; set; }
    /// <summary>Rate change from previous day.</summary>
    public decimal Change { get; set; }
    /// <summary>"api" or "manual".</summary>
    public string Source { get; set; } = "manual";
    public DateTimeOffset LastUpdatedAt { get; set; }
}
