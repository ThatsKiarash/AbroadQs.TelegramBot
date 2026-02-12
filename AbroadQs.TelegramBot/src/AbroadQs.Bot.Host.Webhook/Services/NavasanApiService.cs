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
            var data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);

            if (data == null)
                return (false, "Invalid response from Navasan API.");

            // Increment usage counter
            used++;
            await _settingsRepo.SetValueAsync("navasan_api_calls", used.ToString(), ct).ConfigureAwait(false);

            var rates = new List<ExchangeRateDto>();
            foreach (var (navasanCode, ourCode, nameFa, nameEn) in CurrencyMap)
            {
                if (data.TryGetValue(navasanCode, out var item))
                {
                    decimal rate = 0;
                    decimal change = 0;

                    if (item.TryGetProperty("value", out var valueProp))
                    {
                        var valueStr = valueProp.GetString() ?? valueProp.ToString();
                        decimal.TryParse(valueStr, out rate);
                    }

                    if (item.TryGetProperty("change", out var changeProp))
                    {
                        var changeStr = changeProp.GetString() ?? changeProp.ToString();
                        decimal.TryParse(changeStr, out change);
                    }

                    if (rate > 0)
                    {
                        // Navasan returns Rial values, convert to Toman
                        rate /= 10m;
                        change /= 10m;

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
}
