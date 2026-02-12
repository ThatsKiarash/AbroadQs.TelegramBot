using System.Text.Json;
using AbroadQs.Bot.Contracts;

namespace AbroadQs.Bot.Host.Webhook.Services;

/// <summary>
/// Fetches exchange rates from Navasan API with strict monthly limit (120 calls/month).
/// </summary>
public sealed class NavasanApiService
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly ISettingsRepository _settingsRepo;
    private readonly IExchangeRepository _exchangeRepo;
    private readonly ILogger<NavasanApiService> _logger;

    private const string ApiBaseUrl = "http://api.navasan.tech/latest/";
    private const string ApiKey = "freeTCflQhpTi44FOcoFCFPSIa4Jogh8";
    private const int MonthlyLimit = 120;

    // Currency codes to fetch from Navasan
    private static readonly (string navasanCode, string ourCode, string nameFa, string nameEn)[] CurrencyMap =
    {
        ("usd_sell", "USD", "دلار آمریکا", "US Dollar"),
        ("eur", "EUR", "یورو", "Euro"),
        ("gbp_hav", "GBP", "پوند انگلیس", "British Pound"),
        ("cad", "CAD", "دلار کانادا", "Canadian Dollar"),
        ("sek", "SEK", "کرون سوئد", "Swedish Krona"),
        ("chf", "CHF", "فرانک سوییس", "Swiss Franc"),
        ("try", "TRY", "لیر ترکیه", "Turkish Lira"),
        ("nok", "NOK", "کرون نروژ", "Norwegian Krone"),
        ("aud", "AUD", "دلار استرالیا", "Australian Dollar"),
        ("dkk", "DKK", "کرون دانمارک", "Danish Krone"),
        ("aed_sell", "AED", "درهم امارات", "UAE Dirham"),
        ("inr", "INR", "روپیه هند", "Indian Rupee"),
        ("usdt", "USDT", "تتر", "Tether"),
        ("afn", "AFN", "افغانی", "Afghan Afghani"),
    };

    public NavasanApiService(
        IHttpClientFactory httpFactory,
        ISettingsRepository settingsRepo,
        IExchangeRepository exchangeRepo,
        ILogger<NavasanApiService> logger)
    {
        _httpFactory = httpFactory;
        _settingsRepo = settingsRepo;
        _exchangeRepo = exchangeRepo;
        _logger = logger;
    }

    /// <summary>
    /// Returns current month's API usage count.
    /// </summary>
    public async Task<(int used, int limit)> GetUsageAsync(CancellationToken ct = default)
    {
        var monthKey = DateTime.UtcNow.ToString("yyyy-MM");
        var storedMonth = await _settingsRepo.GetValueAsync("navasan_api_month", ct).ConfigureAwait(false);
        var usedStr = await _settingsRepo.GetValueAsync("navasan_api_calls", ct).ConfigureAwait(false);

        if (storedMonth != monthKey)
            return (0, MonthlyLimit);

        int.TryParse(usedStr, out var used);
        return (used, MonthlyLimit);
    }

    /// <summary>
    /// Fetch latest rates from Navasan API. Returns false if rate limit exceeded.
    /// </summary>
    public async Task<(bool success, string message)> FetchAndCacheRatesAsync(CancellationToken ct = default)
    {
        // Check monthly limit
        var monthKey = DateTime.UtcNow.ToString("yyyy-MM");
        var storedMonth = await _settingsRepo.GetValueAsync("navasan_api_month", ct).ConfigureAwait(false);
        var usedStr = await _settingsRepo.GetValueAsync("navasan_api_calls", ct).ConfigureAwait(false);

        int used = 0;
        if (storedMonth == monthKey)
            int.TryParse(usedStr, out used);
        else
        {
            // New month — reset counter
            await _settingsRepo.SetValueAsync("navasan_api_month", monthKey, ct).ConfigureAwait(false);
            await _settingsRepo.SetValueAsync("navasan_api_calls", "0", ct).ConfigureAwait(false);
            used = 0;
        }

        if (used >= MonthlyLimit)
            return (false, $"Monthly API limit reached ({used}/{MonthlyLimit}). Please update rates manually.");

        try
        {
            var client = _httpFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(15);

            var url = $"{ApiBaseUrl}?api_key={ApiKey}";
            var response = await client.GetAsync(url, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            // Use JsonDocument for safer parsing — avoids type mismatch issues
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });

            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return (false, "Invalid response from Navasan API (expected JSON object).");

            // Increment usage counter
            used++;
            await _settingsRepo.SetValueAsync("navasan_api_calls", used.ToString(), ct).ConfigureAwait(false);

            var rates = new List<ExchangeRateDto>();
            foreach (var (navasanCode, ourCode, nameFa, nameEn) in CurrencyMap)
            {
                try
                {
                    if (!root.TryGetProperty(navasanCode, out var item)) continue;
                    if (item.ValueKind != JsonValueKind.Object) continue;

                    decimal rate = 0;
                    decimal change = 0;

                    if (item.TryGetProperty("value", out var valueProp))
                        rate = SafeParseDecimal(valueProp);

                    if (item.TryGetProperty("change", out var changeProp))
                        change = SafeParseDecimal(changeProp);

                    if (rate > 0)
                    {
                        // Navasan values are already in Toman — no conversion needed
                        rates.Add(new ExchangeRateDto(
                            Id: 0,
                            CurrencyCode: ourCode,
                            CurrencyNameFa: nameFa,
                            CurrencyNameEn: nameEn,
                            Rate: rate,
                            Change: change,
                            Source: "navasan",
                            LastUpdatedAt: DateTimeOffset.UtcNow));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to parse rate for {Code}", navasanCode);
                }
            }

            await _exchangeRepo.SaveRatesAsync(rates, ct).ConfigureAwait(false);

            _logger.LogInformation("Navasan API: fetched {Count} rates (usage: {Used}/{Limit})", rates.Count, used, MonthlyLimit);
            return (true, $"Successfully fetched {rates.Count} rates. API usage: {used}/{MonthlyLimit} this month.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Navasan API fetch failed");
            return (false, $"API error: {ex.Message}");
        }
    }

    /// <summary>
    /// Safely parse a JsonElement that may be String, Number, or other.
    /// Uses GetRawText() as the primary approach to avoid type mismatch exceptions.
    /// </summary>
    private static decimal SafeParseDecimal(JsonElement el)
    {
        try
        {
            // For numbers, use TryGetDecimal first (most reliable for Number kind)
            if (el.ValueKind == JsonValueKind.Number)
            {
                if (el.TryGetDecimal(out var d)) return d;
                // Fallback for extreme precision floats
                if (el.TryGetDouble(out var dbl)) return (decimal)dbl;
                return 0;
            }

            // For strings, extract and parse
            if (el.ValueKind == JsonValueKind.String)
            {
                var s = el.GetString();
                if (string.IsNullOrWhiteSpace(s)) return 0;
                return decimal.TryParse(s, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;
            }

            // For any other type, use raw text
            var raw = el.GetRawText().Trim('"', ' ');
            return decimal.TryParse(raw, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var fallback) ? fallback : 0;
        }
        catch
        {
            // Ultimate fallback — never crash
            try
            {
                var raw = el.GetRawText().Trim('"', ' ');
                return decimal.TryParse(raw, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;
            }
            catch { return 0; }
        }
    }
}
