namespace AbroadQs.Bot.Host.Webhook.Services;

/// <summary>
/// Background service that automatically fetches exchange rates from the Navasan API.
/// Distributes 120 API calls evenly across the month to ensure rates are always up-to-date.
/// Strategy: ~4 calls/day (every 6 hours) with smart backoff when limit is approaching.
/// </summary>
public sealed class RateAutoRefreshService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<RateAutoRefreshService> _logger;

    private const int MonthlyLimit = 120;
    // Reserve 10 calls for manual use from the admin panel
    private const int AutoCallReserve = 10;
    private const int AutoCallBudget = MonthlyLimit - AutoCallReserve; // 110 auto calls

    public RateAutoRefreshService(IServiceProvider serviceProvider, ILogger<RateAutoRefreshService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait 30 seconds after startup to let everything initialize
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken).ConfigureAwait(false);

        _logger.LogInformation("RateAutoRefreshService started. Budget: {Budget} auto calls/month (reserving {Reserve} for manual use)",
            AutoCallBudget, AutoCallReserve);

        // Fetch immediately on startup so rates are available right away
        try { await FetchRatesAsync(stoppingToken).ConfigureAwait(false); }
        catch (Exception ex) { _logger.LogWarning(ex, "RateAutoRefresh: Initial fetch failed"); }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var interval = await CalculateNextIntervalAsync(stoppingToken).ConfigureAwait(false);
                _logger.LogInformation("RateAutoRefresh: Next fetch in {Hours:F1} hours", interval.TotalHours);

                await Task.Delay(interval, stoppingToken).ConfigureAwait(false);

                await FetchRatesAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "RateAutoRefreshService error, will retry in 1 hour");
                await Task.Delay(TimeSpan.FromHours(1), stoppingToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Calculates the optimal interval between API calls based on remaining budget and days in month.
    /// </summary>
    private async Task<TimeSpan> CalculateNextIntervalAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var svc = scope.ServiceProvider.GetService<NavasanApiService>();
            if (svc == null) return TimeSpan.FromHours(6);

            var (used, _) = await svc.GetUsageAsync(ct).ConfigureAwait(false);
            var now = DateTime.UtcNow;
            var daysInMonth = DateTime.DaysInMonth(now.Year, now.Month);
            var remainingDays = Math.Max(1, daysInMonth - now.Day + 1);
            var remainingBudget = Math.Max(0, AutoCallBudget - used);

            if (remainingBudget <= 0)
            {
                // Budget exhausted — wait until next month
                var nextMonth = new DateTime(now.Year, now.Month, 1).AddMonths(1);
                var waitUntilNextMonth = nextMonth - now;
                _logger.LogWarning("RateAutoRefresh: Monthly budget exhausted ({Used}/{Limit}). Waiting until next month ({Wait:F1} days)",
                    used, MonthlyLimit, waitUntilNextMonth.TotalDays);
                return waitUntilNextMonth;
            }

            // Distribute remaining budget across remaining days
            var callsPerDay = (double)remainingBudget / remainingDays;
            var hoursPerCall = 24.0 / Math.Max(1, callsPerDay);

            // Clamp between 2 hours (min) and 12 hours (max)
            hoursPerCall = Math.Clamp(hoursPerCall, 2.0, 12.0);

            _logger.LogDebug("RateAutoRefresh: {Remaining} calls left for {Days} days = {CallsPerDay:F1} calls/day = every {Hours:F1}h",
                remainingBudget, remainingDays, callsPerDay, hoursPerCall);

            return TimeSpan.FromHours(hoursPerCall);
        }
        catch
        {
            return TimeSpan.FromHours(6); // Default fallback
        }
    }

    private async Task FetchRatesAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var svc = scope.ServiceProvider.GetService<NavasanApiService>();
        if (svc == null)
        {
            _logger.LogWarning("RateAutoRefresh: NavasanApiService not registered");
            return;
        }

        var (success, message) = await svc.FetchAndCacheRatesAsync(ct).ConfigureAwait(false);
        if (success)
            _logger.LogInformation("RateAutoRefresh: {Message}", message);
        else
            _logger.LogWarning("RateAutoRefresh failed: {Message}", message);
    }
}
