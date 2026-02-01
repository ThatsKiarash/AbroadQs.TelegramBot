using System.Linq;
using AbroadQs.Bot.Application;
using AbroadQs.Bot.Contracts;
using AbroadQs.Bot.Host.Webhook.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Requests;
using Telegram.Bot.Types;

namespace AbroadQs.Bot.Host.Webhook.Services;

/// <summary>
/// وقتی حالت به‌روزرسانی «GetUpdates» است، با long polling آپدیت‌ها را می‌گیرد و همان‌طور که وب‌هوک به UpdateDispatcher می‌دهد، اینجا هم به همان دیسپچر می‌دهد تا معماری یکسان بماند.
/// </summary>
public sealed class GetUpdatesPollingService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<GetUpdatesPollingService> _logger;
    private readonly GetUpdatesPollingOptions _options;
    private readonly IConfiguration _configuration;
    private readonly UpdateLogService _logService;
    private readonly BotStatusService _botStatus;

    public GetUpdatesPollingService(
        IServiceProvider serviceProvider,
        ILogger<GetUpdatesPollingService> logger,
        IOptions<GetUpdatesPollingOptions> options,
        IConfiguration configuration,
        UpdateLogService logService,
        BotStatusService botStatus)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? new GetUpdatesPollingOptions();
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logService = logService ?? throw new ArgumentNullException(nameof(logService));
        _botStatus = botStatus ?? throw new ArgumentNullException(nameof(botStatus));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var updateMode = (_options.UpdateMode ?? _configuration["Telegram:UpdateMode"] ?? "Webhook").Trim();
        if (!string.Equals(updateMode, "GetUpdates", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("Update mode is '{Mode}'; GetUpdates long polling is disabled.", updateMode);
            return;
        }

        var client = _serviceProvider.GetService<ITelegramBotClient>();
        if (client == null || client is PlaceholderTelegramBotClient)
        {
            _logger.LogWarning("GetUpdates mode is set but bot client is not available (invalid or missing token?). Long polling will not run.");
            return;
        }

        try
        {
            await client.DeleteWebhook(cancellationToken: stoppingToken).ConfigureAwait(false);
            _logger.LogInformation("Webhook removed; starting GetUpdates long polling.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DeleteWebhook failed (may be none set). Continuing with polling.");
        }

        int? offset = null;
        var timeoutSeconds = Math.Clamp(_options.PollingTimeoutSeconds, 1, 50);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var request = new GetUpdatesRequest
                {
                    Offset = offset,
                    Timeout = timeoutSeconds
                };
                var updates = await client.SendRequest(request, stoppingToken).ConfigureAwait(false);
                if (updates == null)
                    continue;

                var list = updates.ToList();
                if (list.Count > 0)
                    _logger.LogInformation("GetUpdates: received {Count} update(s)", list.Count);

                foreach (var update in list)
                {
                    if (stoppingToken.IsCancellationRequested)
                        break;
                    offset = update.Id + 1;
                    var preview = GetUpdatePreview(update);
                    
                    // اگر ربات خاموش است، فقط لاگ کن و جواب نده
                    if (!_botStatus.IsEnabled)
                    {
                        _logService.Log(new UpdateLogEntry(
                            DateTime.UtcNow, "Skipped", preview, "Bot is disabled",
                            Source: "GetUpdates"
                        ));
                        continue;
                    }
                    
                    try
                    {
                        using var scope = _serviceProvider.CreateScope();
                        var procCtx = scope.ServiceProvider.GetRequiredService<ProcessingContext>();
                        procCtx.Source = "GetUpdates";
                        
                        var dispatcher = scope.ServiceProvider.GetRequiredService<UpdateDispatcher>();
                        await dispatcher.DispatchAsync(update, stoppingToken).ConfigureAwait(false);
                        
                        _logger.LogInformation("Update {UpdateId} dispatched successfully.", update.Id);
                        _logService.Log(new UpdateLogEntry(
                            DateTime.UtcNow, "Received", preview, null,
                            Source: procCtx.Source,
                            RedisProcessed: procCtx.RedisAccessed,
                            RabbitMqPublished: procCtx.RabbitMqPublished,
                            SqlProcessed: procCtx.SqlAccessed,
                            ResponseSent: procCtx.ResponseSent,
                            HandlerName: procCtx.HandlerName
                        ));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to dispatch update {UpdateId}", update.Id);
                        _logService.Log(new UpdateLogEntry(
                            DateTime.UtcNow, "Error", preview, ex.Message,
                            Source: "GetUpdates"
                        ));
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetUpdates error; retrying in 5s");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);
            }
        }

        _logger.LogInformation("GetUpdates polling stopped.");
    }

    private static string GetUpdatePreview(Update u)
    {
        var id = u.Id;
        if (u.Message != null) return $"UpdateId: {id}, Type: Message";
        if (u.CallbackQuery != null) return $"UpdateId: {id}, Type: CallbackQuery";
        if (u.InlineQuery != null) return $"UpdateId: {id}, Type: InlineQuery";
        if (u.ChosenInlineResult != null) return $"UpdateId: {id}, Type: ChosenInlineResult";
        return $"UpdateId: {id}, Type: Unknown";
    }
}

/// <summary>
/// تنظیمات حالت به‌روزرسانی و long polling.
/// </summary>
public class GetUpdatesPollingOptions
{
    public const string SectionName = "Telegram";

    /// <summary>حالت به‌روزرسانی: Webhook یا GetUpdates.</summary>
    public string UpdateMode { get; set; } = "Webhook";

    /// <summary>تایم‌اوت long polling به ثانیه (۱–۵۰).</summary>
    public int PollingTimeoutSeconds { get; set; } = 25;
}
