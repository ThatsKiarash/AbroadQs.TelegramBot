using AbroadQs.Bot.Application;
using AbroadQs.Bot.Contracts;
using AbroadQs.Bot.Data;
using AbroadQs.Bot.Host.Webhook.Services;
using AbroadQs.Bot.Modules.Common;
using AbroadQs.Bot.Modules.Example;
using AbroadQs.Bot.Telegram;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Types;
using Telegram.Bot;

var builder = WebApplication.CreateBuilder(args);

// توکن و حالت به‌روزرسانی از فایل‌های اختیاری (داشبورد ذخیره می‌کند)
builder.Configuration.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.Token.json"), optional: true, reloadOnChange: false);
builder.Configuration.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.Mode.json"), optional: true, reloadOnChange: false);

// اول از دیتابیس، بعد از فایل/کانفیگ — تا بعد از ذخیره در داشبورد، با ریستارت همان تنظیمات بیاید
var connStr = builder.Configuration.GetConnectionString("DefaultConnection");
string? dbToken = null;
string? dbMode = null;
if (!string.IsNullOrWhiteSpace(connStr))
{
    (dbToken, dbMode) = LoadSettingsFromDatabaseSync(connStr);
}

var botToken = dbToken ?? LoadTokenAtStartup();
if (string.IsNullOrWhiteSpace(botToken) || botToken == "<YOUR_BOT_TOKEN>" || botToken == "0:placeholder")
    botToken = builder.Configuration["Telegram:BotToken"];
if (string.IsNullOrWhiteSpace(botToken) || botToken == "<YOUR_BOT_TOKEN>")
    botToken = "0:placeholder";

// فقط با توکن معتبر TelegramBotClient می‌سازیم؛ وگرنه placeholder تا اپ کرش نکند و داشبورد باز شود
if (IsValidTokenFormat(botToken))
    builder.Services.AddTelegramBot(botToken);
else
{
    builder.Services.AddSingleton<ITelegramBotClient>(new PlaceholderTelegramBotClient());
    builder.Services.AddScoped<IResponseSender, TelegramResponseSender>();
}

// Application + modules (add more modules here)
builder.Services.AddBotApplication();
builder.Services.AddCommonModule();
builder.Services.AddExampleModule();

// SMS service (sms.ir) for OTP verification
builder.Services.AddSingleton<AbroadQs.Bot.Contracts.ISmsService>(sp =>
    new AbroadQs.Bot.Host.Webhook.Services.SmsIrService(
        "ZxpWSZ0nSgVcqRGecTPGS0KGltods6GJhfZSGyVUjLuEGXks",
        168094,
        sp.GetRequiredService<ILogger<AbroadQs.Bot.Host.Webhook.Services.SmsIrService>>()));

// HttpClientFactory for email relay
builder.Services.AddHttpClient();

// Email service — HTTP relay (primary) + SMTP fallback
builder.Services.AddSingleton<AbroadQs.Bot.Contracts.IEmailService>(sp =>
    new AbroadQs.Bot.Host.Webhook.Services.EmailOtpService(
        "abroadqs.com", 465, "info@abroadqs.com", "Kia135724!",
        sp.GetRequiredService<ILogger<AbroadQs.Bot.Host.Webhook.Services.EmailOtpService>>(),
        relayUrl: "https://abroadqs.com/emailrelay",
        relayToken: "AbroadQs_Email_Relay_2026_Secure",
        httpClientFactory: sp.GetRequiredService<IHttpClientFactory>()));

// Scoped processing context برای ردیابی عملیات هر درخواست
builder.Services.AddScoped<ProcessingContext>();
builder.Services.AddScoped<IProcessingContext>(sp => sp.GetRequiredService<ProcessingContext>());

// RabbitMQ (optional: if HostName is set, webhook publishes to queue and consumer dispatches)
builder.Services.Configure<RabbitMqOptions>(builder.Configuration.GetSection(RabbitMqOptions.SectionName));
var rabbitHost = builder.Configuration["RabbitMQ:HostName"];
if (!string.IsNullOrWhiteSpace(rabbitHost))
{
    builder.Services.AddSingleton<IRabbitMqPublisher, RabbitMqPublisher>();
    builder.Services.AddHostedService<RabbitMqConsumerService>();
}

// حالت به‌روزرسانی: Webhook یا GetUpdates (long polling) — اگر از دیتابیس خوانده شد، همان اولویت دارد
builder.Services.Configure<GetUpdatesPollingOptions>(builder.Configuration.GetSection(GetUpdatesPollingOptions.SectionName));
if (!string.IsNullOrWhiteSpace(dbMode))
    builder.Services.Configure<GetUpdatesPollingOptions>(o => o.UpdateMode = dbMode);
builder.Services.AddHostedService<GetUpdatesPollingService>();

// Redis (optional: last command store; if not configured, no-op is used)
builder.Services.Configure<RedisOptions>(builder.Configuration.GetSection(RedisOptions.SectionName));
var redisConfig = builder.Configuration["Redis:Configuration"];
if (!string.IsNullOrWhiteSpace(redisConfig))
{
    builder.Services.AddSingleton<StackExchange.Redis.IConnectionMultiplexer>(
        _ => StackExchange.Redis.ConnectionMultiplexer.Connect(redisConfig));
    builder.Services.AddScoped<IUserLastCommandStore>(sp =>
        new RedisUserLastCommandStore(
            sp.GetRequiredService<StackExchange.Redis.IConnectionMultiplexer>(),
            sp.GetRequiredService<ILogger<RedisUserLastCommandStore>>(),
            sp.GetService<IProcessingContext>()));
    builder.Services.AddScoped<IUserConversationStateStore>(sp =>
        new RedisUserConversationStateStore(
            sp.GetRequiredService<StackExchange.Redis.IConnectionMultiplexer>(),
            sp.GetRequiredService<ILogger<RedisUserConversationStateStore>>(),
            sp.GetService<IProcessingContext>()));
}
else
{
    builder.Services.AddSingleton<IUserLastCommandStore, NoOpUserLastCommandStore>();
    builder.Services.AddSingleton<IUserConversationStateStore, NoOpUserConversationStateStore>();
}

// SQL Server (optional: if connection string is set, users and settings are persisted)
if (!string.IsNullOrWhiteSpace(connStr))
{
    builder.Services.AddDbContext<ApplicationDbContext>(o => o.UseSqlServer(connStr));
    builder.Services.AddScoped<ITelegramUserRepository, TelegramUserRepository>();
    builder.Services.AddScoped<ISettingsRepository, SettingsRepository>();
    builder.Services.AddScoped<IMessageRepository, MessageRepository>();
    builder.Services.AddScoped<IUserMessageStateRepository, UserMessageStateRepository>();
    builder.Services.AddScoped<IBotStageRepository, BotStageRepository>();
    builder.Services.AddScoped<IPermissionRepository, PermissionRepository>();
    builder.Services.AddScoped<IExchangeRepository, ExchangeRepository>();
    builder.Services.AddScoped<IGroupRepository, GroupRepository>();
    builder.Services.AddScoped<IWalletRepository, WalletRepository>();
    builder.Services.AddScoped<IBidRepository, BidRepository>();
    builder.Services.AddScoped<IGroupRepository, GroupRepository>();
    builder.Services.AddScoped<ISystemMessageRepository, SystemMessageRepository>();
    builder.Services.AddScoped<ITicketRepository, TicketRepository>();
    builder.Services.AddScoped<IStudentProjectRepository, StudentProjectRepository>();
    builder.Services.AddScoped<IProjectBidRepository, ProjectBidRepository>();
    builder.Services.AddScoped<IInternationalQuestionRepository, InternationalQuestionRepository>();
    builder.Services.AddScoped<ISponsorshipRepository, SponsorshipRepository>();
    builder.Services.AddScoped<ICryptoWalletRepository, CryptoWalletRepository>();
    builder.Services.AddScoped<NavasanApiService>();
    builder.Services.AddHostedService<RateAutoRefreshService>();

    // BitPay service
    builder.Services.AddHttpClient<AbroadQs.Bot.Host.Webhook.Services.BitPayService>();
    builder.Services.AddScoped(sp =>
    {
        var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(AbroadQs.Bot.Host.Webhook.Services.BitPayService));
        var logger = sp.GetRequiredService<ILogger<AbroadQs.Bot.Host.Webhook.Services.BitPayService>>();
        var settingsRepo = sp.GetService<ISettingsRepository>();
        var apiKey = settingsRepo?.GetValueAsync("bitpay_api_key").GetAwaiter().GetResult() ?? "";
        var testMode = settingsRepo?.GetValueAsync("bitpay_test_mode").GetAwaiter().GetResult() == "true";
        return new AbroadQs.Bot.Host.Webhook.Services.BitPayService(http, logger, apiKey, testMode);
    });

    // Payment gateway abstraction (adapts BitPayService to IPaymentGatewayService)
    builder.Services.AddScoped<IPaymentGatewayService>(sp =>
        new BitPayPaymentGatewayAdapter(
            sp.GetRequiredService<AbroadQs.Bot.Host.Webhook.Services.BitPayService>(),
            sp.GetRequiredService<IWalletRepository>(),
            sp.GetService<ISettingsRepository>()));

    // Phase 8: Crypto wallet service (TRX/ETH address generation, monitoring)
    builder.Services.AddSingleton(sp =>
    {
        var logger = sp.GetRequiredService<ILogger<AbroadQs.Bot.Host.Webhook.Services.CryptoWalletService>>();
        var settingsRepo = sp.GetService<ISettingsRepository>();
        var tronApiKey = settingsRepo?.GetValueAsync("crypto_tron_api_key").GetAwaiter().GetResult() ?? "";
        var masterAddr = settingsRepo?.GetValueAsync("crypto_master_wallet_address").GetAwaiter().GetResult() ?? "";
        return new AbroadQs.Bot.Host.Webhook.Services.CryptoWalletService(logger, tronApiKey, masterAddr);
    });
}
else
{
    builder.Services.AddSingleton<ITelegramUserRepository, NoOpTelegramUserRepository>();
    builder.Services.AddSingleton<IBotStageRepository, NoOpBotStageRepository>();
    builder.Services.AddSingleton<IPermissionRepository, NoOpPermissionRepository>();
    builder.Services.AddSingleton<IExchangeRepository, NoOpExchangeRepository>();
}

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// سرویس مشترک لاگ آپدیت‌ها
builder.Services.AddSingleton<UpdateLogService>();

// سرویس وضعیت روشن/خاموش ربات
builder.Services.AddSingleton<BotStatusService>();

var app = builder.Build();

// برای داشبورد: زمان آخرین ست Webhook
var webhookSyncedAt = (DateTime?)null;
var updateLogService = app.Services.GetRequiredService<UpdateLogService>();

// Optional: apply EF migrations at startup when SQL Server is configured
if (!string.IsNullOrWhiteSpace(connStr))
{
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.MigrateAsync().ConfigureAwait(false);
        app.Logger.LogInformation("Database migrations applied.");

        // Seed default stages, buttons, and permissions if not already present
        await SeedDefaultDataAsync(db).ConfigureAwait(false);
    }
}

// Optional: set webhook at startup only when mode is Webhook and URL is configured (حالت از دیتابیس/فایل/کانفیگ)
var updateMode = dbMode ?? builder.Configuration["Telegram:UpdateMode"] ?? "Webhook";
var webhookUrl = (builder.Configuration["Telegram:WebhookUrl"] ?? "").Trim();
if (string.IsNullOrWhiteSpace(webhookUrl))
    webhookUrl = (builder.Configuration["Telegram:PublicWebhookUrl"] ?? Environment.GetEnvironmentVariable("PUBLIC_WEBHOOK_URL") ?? "").Trim();
if (string.Equals(updateMode, "Webhook", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(webhookUrl))
{
    try
    {
        using var scope = app.Services.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<ITelegramBotClient>();
        if (client is not PlaceholderTelegramBotClient)
        {
            await client.SetWebhook(webhookUrl).ConfigureAwait(false);
            app.Logger.LogInformation("Webhook set to {WebhookUrl}", webhookUrl);
        }
        // Persist webhook_url so payment adapter can resolve redirect URLs
        var startupSettings = scope.ServiceProvider.GetService<ISettingsRepository>();
        if (startupSettings != null)
        {
            var existing = await startupSettings.GetValueAsync("webhook_url").ConfigureAwait(false);
            if (string.IsNullOrEmpty(existing))
                await startupSettings.SetValueAsync("webhook_url", webhookUrl).ConfigureAwait(false);
        }
    }
    catch (Exception ex) { app.Logger.LogWarning(ex, "SetWebhook at startup failed (token may not be set yet)."); }
}
else if (string.Equals(updateMode, "GetUpdates", StringComparison.OrdinalIgnoreCase))
{
    try
    {
        using var scope = app.Services.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<ITelegramBotClient>();
        if (client is not PlaceholderTelegramBotClient)
        {
            await client.DeleteWebhook().ConfigureAwait(false);
            app.Logger.LogInformation("Update mode is GetUpdates; webhook deleted. Long polling will run.");
        }
    }
    catch (Exception ex) { app.Logger.LogWarning(ex, "DeleteWebhook at startup failed."); }
}
else
{
    // Webhook mode but no URL (e.g. local dev): remove any existing webhook so updates don't go to old server
    try
    {
        using var scope = app.Services.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<ITelegramBotClient>();
        if (client is not PlaceholderTelegramBotClient)
        {
            await client.DeleteWebhook().ConfigureAwait(false);
            app.Logger.LogInformation("Webhook mode but no URL set; webhook removed. Set a webhook URL or use GetUpdates for local testing.");
        }
    }
    catch (Exception ex) { app.Logger.LogWarning(ex, "DeleteWebhook failed."); }
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

if (!app.Environment.IsDevelopment())
    app.UseHttpsRedirection();
app.UseStaticFiles();

// Dashboard: serve HTML directly
app.MapGet("/dashboard", (IWebHostEnvironment env) =>
{
    var path = Path.Combine(env.ContentRootPath, "wwwroot", "dashboard", "index.html");
    if (System.IO.File.Exists(path))
    {
        var html = System.IO.File.ReadAllText(path);
        return Results.Content(html, "text/html; charset=utf-8");
    }
    // Fallback if file not found (e.g. running from different folder)
    var fallback = """
        <!DOCTYPE html><html dir="rtl"><head><meta charset="utf-8"><title>داشبورد</title></head>
        <body style="font-family:tahoma;padding:2rem;background:#1a1a20;color:#e4e4e7;">
        <h1>داشبورد Webhook</h1>
        <p>فایل داشبورد پیدا نشد. از پوشهٔ پروژه اجرا کن: dotnet run</p>
        <p><a href="/" style="color:#3b82f6;">برگشت</a></p>
        </body></html>
        """;
    return Results.Content(fallback, "text/html; charset=utf-8");
})
.WithName("Dashboard");

// API: Webhook management (for dashboard) — از توکن فعلی (فایل/DB) استفاده می‌کند تا بعد از ذخیره بدون ریستارت تست جواب بدهد
app.MapGet("/api/webhook/info", async (HttpContext ctx, CancellationToken ct) =>
{
    var token = await ReadTokenAsync(ctx.RequestServices, ct).ConfigureAwait(false);
    if (!IsValidTokenFormat(token))
        return Results.Json(new { detail = "توکن ست نشده." }, statusCode: 400);
    try
    {
        var client = new TelegramBotClient(token!);
        var info = await client.GetWebhookInfo(ct).ConfigureAwait(false);
        return Results.Json(new
        {
            url = info.Url ?? (string?)null,
            pendingUpdateCount = info.PendingUpdateCount,
            lastErrorDate = info.LastErrorDate,
            lastErrorMessage = info.LastErrorMessage
        });
    }
    catch (Exception ex)
    {
        return Results.Json(new { detail = ex.Message }, statusCode: 500);
    }
}).WithName("WebhookInfo");

// API: اطلاعات ربات (getMe) برای دکمهٔ تست توکن — از توکن فعلی (فایل/DB) استفاده می‌کند
app.MapGet("/api/bot/me", async (HttpContext ctx, CancellationToken ct) =>
{
    var token = await ReadTokenAsync(ctx.RequestServices, ct).ConfigureAwait(false);
    if (!IsValidTokenFormat(token))
        return Results.Json(new { detail = "توکن ست نشده." }, statusCode: 400);
    try
    {
        var client = new TelegramBotClient(token!);
        var me = await client.GetMe(ct).ConfigureAwait(false);
        return Results.Json(new
        {
            id = me.Id,
            isBot = me.IsBot,
            firstName = me.FirstName ?? "",
            lastName = me.LastName ?? "",
            username = me.Username ?? (string?)null,
            languageCode = me.LanguageCode ?? (string?)null,
            canJoinGroups = me.CanJoinGroups,
            canReadAllGroupMessages = me.CanReadAllGroupMessages,
            supportsInlineQueries = me.SupportsInlineQueries
        });
    }
    catch (Exception ex)
    {
        return Results.Json(new { detail = ex.Message }, statusCode: 500);
    }
}).WithName("BotMe");

app.MapPost("/api/webhook/set", async (HttpContext ctx, CancellationToken ct) =>
{
    var body = await ctx.Request.ReadFromJsonAsync<SetWebhookRequest>(ct).ConfigureAwait(false);
    var url = body?.Url?.Trim();
    if (string.IsNullOrEmpty(url))
        return Results.BadRequest(new { detail = "آدرس Webhook الزامی است." });
    if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        return Results.BadRequest(new { detail = "آدرس باید با https:// شروع شود." });
    var token = await ReadTokenAsync(ctx.RequestServices, ct).ConfigureAwait(false);
    if (!IsValidTokenFormat(token))
        return Results.BadRequest(new { detail = "توکن ست نشده." });
    try
    {
        var client = new TelegramBotClient(token!);
        await client.SetWebhook(url, cancellationToken: ct).ConfigureAwait(false);
        webhookSyncedAt = DateTime.UtcNow;
        return Results.Json(new { success = true });
    }
    catch (Exception ex)
    {
        return Results.Json(new { detail = ex.Message }, statusCode: 500);
    }
}).WithName("WebhookSet");

app.MapPost("/api/webhook/delete", async (HttpContext ctx, CancellationToken ct) =>
{
    var token = await ReadTokenAsync(ctx.RequestServices, ct).ConfigureAwait(false);
    if (!IsValidTokenFormat(token))
        return Results.Json(new { detail = "توکن ست نشده." }, statusCode: 400);
    try
    {
        var client = new TelegramBotClient(token!);
        await client.DeleteWebhook(cancellationToken: ct).ConfigureAwait(false);
        return Results.Json(new { success = true });
    }
    catch (Exception ex)
    {
        return Results.Json(new { detail = ex.Message }, statusCode: 500);
    }
}).WithName("WebhookDelete");

// API: لیست آخرین ۵ پیام آپدیت (برای جدول داشبورد)
app.MapGet("/api/webhook/logs", (UpdateLogService logService) =>
{
    var logs = logService.GetRecentLogs(5).Select(x => new
    {
        time = x.Time,
        status = x.Status,
        payloadPreview = x.PayloadPreview,
        error = x.Error,
        source = x.Source,
        redisProcessed = x.RedisProcessed,
        rabbitMqPublished = x.RabbitMqPublished,
        sqlProcessed = x.SqlProcessed,
        responseSent = x.ResponseSent,
        handlerName = x.HandlerName
    });
    return Results.Json(logs);
}).WithName("WebhookLogs");

// API: وضعیت داشبورد (ثبت شده/فعال)
app.MapGet("/api/dashboard/status", async (HttpContext ctx, UpdateLogService logService) =>
{
    var token = await ReadTokenAsync(ctx.RequestServices, ctx.RequestAborted).ConfigureAwait(false);
    var tokenSet = token.Length > 0 && token != "<YOUR_BOT_TOKEN>" && token != "0:placeholder";
    var updateMode = await ReadUpdateModeAsync(ctx.RequestServices, ctx.RequestAborted).ConfigureAwait(false);

    var lastLog = logService.GetRecentLogs(1).FirstOrDefault();
    
    // Check Redis connection
    bool redisConnected = false;
    try
    {
        var redis = ctx.RequestServices.GetService<StackExchange.Redis.IConnectionMultiplexer>();
        redisConnected = redis?.IsConnected ?? false;
    }
    catch { }
    
    // Check RabbitMQ connection (check if service is registered)
    bool rabbitMqConfigured = ctx.RequestServices.GetService<IRabbitMqPublisher>() != null;
    
    // Check SQL Server connection
    bool sqlConnected = false;
    try
    {
        using var scope = ctx.RequestServices.CreateScope();
        var db = scope.ServiceProvider.GetService<ApplicationDbContext>();
        if (db != null)
        {
            sqlConnected = await db.Database.CanConnectAsync(ctx.RequestAborted).ConfigureAwait(false);
        }
    }
    catch { }
    
    return Results.Json(new
    {
        tokenSet,
        updateMode,
        webhookSyncedAt,
        lastEventAt = lastLog?.Time,
        lastEventStatus = lastLog?.Status,
        lastEventError = lastLog?.Error,
        redisConnected,
        rabbitMqConfigured,
        sqlConnected
    });
}).WithName("DashboardStatus");

// API: لیست کاربران ربات (از SQL Server)
app.MapGet("/api/users", async (HttpContext ctx) =>
{
    try
    {
        using var scope = ctx.RequestServices.CreateScope();
        var userRepo = scope.ServiceProvider.GetService<ITelegramUserRepository>();
        if (userRepo == null)
            return Results.Json(new List<object>());
        var users = await userRepo.ListAllAsync(ctx.RequestAborted).ConfigureAwait(false);
        var payload = users.Select(u => new
        {
            telegramUserId = u.TelegramUserId,
            username = u.Username,
            firstName = u.FirstName,
            lastName = u.LastName,
            preferredLanguage = u.PreferredLanguage,
            firstSeenAt = u.FirstSeenAt,
            lastSeenAt = u.LastSeenAt
        });
        return Results.Json(payload);
    }
    catch (Exception ex)
    {
        return Results.Json(new { error = ex.Message }, statusCode: 500);
    }
}).WithName("UsersList");

// API: لیست پیام‌های یک کاربر
app.MapGet("/api/messages/user/{userId:long}", async (long userId, HttpContext ctx, int? limit = null) =>
{
    try
    {
        using var scope = ctx.RequestServices.CreateScope();
        var messageRepo = scope.ServiceProvider.GetService<IMessageRepository>();
        if (messageRepo == null)
            return Results.Json(new List<object>());
        var messages = await messageRepo.GetUserMessagesAsync(userId, limit ?? 5, ctx.RequestAborted).ConfigureAwait(false);
        var payload = messages.Select(m => new
        {
            id = m.Id,
            telegramMessageId = m.TelegramMessageId,
            telegramChatId = m.TelegramChatId,
            telegramUserId = m.TelegramUserId,
            isFromBot = m.IsFromBot,
            text = m.Text,
            messageType = m.MessageType,
            sentAt = m.SentAt,
            editedAt = m.EditedAt,
            deletedAt = m.DeletedAt,
            replyToMessageId = m.ReplyToMessageId,
            forwardFromChatId = m.ForwardFromChatId,
            forwardFromMessageId = m.ForwardFromMessageId,
            hasReplyKeyboard = m.HasReplyKeyboard,
            hasInlineKeyboard = m.HasInlineKeyboard,
            inlineKeyboardId = m.InlineKeyboardId,
            shouldEdit = m.ShouldEdit,
            shouldDelete = m.ShouldDelete,
            shouldForward = m.ShouldForward,
            shouldKeepForEdit = m.ShouldKeepForEdit,
            deleteNextMessages = m.DeleteNextMessages
        });
        return Results.Json(payload);
    }
    catch (Exception ex)
    {
        return Results.Json(new { error = ex.Message }, statusCode: 500);
    }
}).WithName("UserMessages");

// API: مکالمه بین کاربر و ربات
app.MapGet("/api/messages/conversation/{userId:long}", async (long userId, HttpContext ctx, int? limit = null) =>
{
    try
    {
        using var scope = ctx.RequestServices.CreateScope();
        var messageRepo = scope.ServiceProvider.GetService<IMessageRepository>();
        if (messageRepo == null)
            return Results.Json(new List<object>());
        var messages = await messageRepo.GetConversationAsync(userId, limit ?? 5, ctx.RequestAborted).ConfigureAwait(false);
        var payload = messages.Select(m => new
        {
            id = m.Id,
            telegramMessageId = m.TelegramMessageId,
            telegramChatId = m.TelegramChatId,
            telegramUserId = m.TelegramUserId,
            isFromBot = m.IsFromBot,
            text = m.Text,
            messageType = m.MessageType,
            sentAt = m.SentAt,
            editedAt = m.EditedAt,
            deletedAt = m.DeletedAt,
            replyToMessageId = m.ReplyToMessageId,
            forwardFromChatId = m.ForwardFromChatId,
            forwardFromMessageId = m.ForwardFromMessageId,
            hasReplyKeyboard = m.HasReplyKeyboard,
            hasInlineKeyboard = m.HasInlineKeyboard,
            inlineKeyboardId = m.InlineKeyboardId,
            shouldEdit = m.ShouldEdit,
            shouldDelete = m.ShouldDelete,
            shouldForward = m.ShouldForward,
            shouldKeepForEdit = m.ShouldKeepForEdit,
            deleteNextMessages = m.DeleteNextMessages
        });
        return Results.Json(payload);
    }
    catch (Exception ex)
    {
        return Results.Json(new { error = ex.Message }, statusCode: 500);
    }
}).WithName("ConversationMessages");

// API: آخرین پیام ربات برای کاربر و وضعیت آن
app.MapGet("/api/messages/user/{userId:long}/state", async (long userId, HttpContext ctx) =>
{
    try
    {
        using var scope = ctx.RequestServices.CreateScope();
        var messageRepo = scope.ServiceProvider.GetService<IMessageRepository>();
        var stateRepo = scope.ServiceProvider.GetService<IUserMessageStateRepository>();
        
        var lastMessage = messageRepo != null ? await messageRepo.GetLastBotMessageAsync(userId, ctx.RequestAborted).ConfigureAwait(false) : null;
        var state = stateRepo != null ? await stateRepo.GetUserMessageStateAsync(userId, ctx.RequestAborted).ConfigureAwait(false) : null;
        
        return Results.Json(new
        {
            lastMessage = lastMessage != null ? new
            {
                id = lastMessage.Id,
                telegramMessageId = lastMessage.TelegramMessageId,
                text = lastMessage.Text,
                sentAt = lastMessage.SentAt,
                editedAt = lastMessage.EditedAt,
                deletedAt = lastMessage.DeletedAt,
                hasInlineKeyboard = lastMessage.HasInlineKeyboard,
                inlineKeyboardId = lastMessage.InlineKeyboardId,
                shouldEdit = lastMessage.ShouldEdit,
                shouldDelete = lastMessage.ShouldDelete,
                shouldForward = lastMessage.ShouldForward,
                shouldKeepForEdit = lastMessage.ShouldKeepForEdit,
                deleteNextMessages = lastMessage.DeleteNextMessages
            } : null,
            state = state != null ? new
            {
                lastBotMessageId = state.LastBotMessageId,
                lastBotTelegramMessageId = state.LastBotTelegramMessageId,
                shouldEdit = state.ShouldEdit,
                shouldReply = state.ShouldReply,
                shouldKeepStatic = state.ShouldKeepStatic,
                deleteNextMessages = state.DeleteNextMessages,
                lastAction = state.LastAction,
                lastActionAt = state.LastActionAt
            } : null
        });
    }
    catch (Exception ex)
    {
        return Results.Json(new { error = ex.Message }, statusCode: 500);
    }
}).WithName("UserMessageState");

// ===== Bot Stages API =====
app.MapGet("/api/stages", async (HttpContext ctx) =>
{
    using var scope = ctx.RequestServices.CreateScope();
    var repo = scope.ServiceProvider.GetService<IBotStageRepository>();
    if (repo == null) return Results.Json(new { detail = "DB not configured" }, statusCode: 503);
    var stages = await repo.ListAllAsync(ctx.RequestAborted).ConfigureAwait(false);
    return Results.Json(stages);
}).WithName("StagesList");

app.MapGet("/api/stages/{key}", async (string key, HttpContext ctx) =>
{
    using var scope = ctx.RequestServices.CreateScope();
    var repo = scope.ServiceProvider.GetService<IBotStageRepository>();
    if (repo == null) return Results.Json(new { detail = "DB not configured" }, statusCode: 503);
    var stage = await repo.GetByKeyAsync(key, ctx.RequestAborted).ConfigureAwait(false);
    return stage == null ? Results.NotFound() : Results.Json(stage);
}).WithName("StageGet");

app.MapPost("/api/stages", async (HttpContext ctx) =>
{
    using var scope = ctx.RequestServices.CreateScope();
    var repo = scope.ServiceProvider.GetService<IBotStageRepository>();
    if (repo == null) return Results.Json(new { detail = "DB not configured" }, statusCode: 503);
    var dto = await ctx.Request.ReadFromJsonAsync<BotStageCreateDto>(ctx.RequestAborted).ConfigureAwait(false);
    if (dto == null || string.IsNullOrWhiteSpace(dto.StageKey)) return Results.BadRequest(new { detail = "StageKey is required" });
    try
    {
        var created = await repo.CreateAsync(dto, ctx.RequestAborted).ConfigureAwait(false);
        return Results.Json(created, statusCode: 201);
    }
    catch (Exception ex) { return Results.Json(new { detail = ex.Message }, statusCode: 400); }
}).WithName("StageCreate");

app.MapPut("/api/stages/{id:int}", async (int id, HttpContext ctx) =>
{
    using var scope = ctx.RequestServices.CreateScope();
    var repo = scope.ServiceProvider.GetService<IBotStageRepository>();
    if (repo == null) return Results.Json(new { detail = "DB not configured" }, statusCode: 503);
    var dto = await ctx.Request.ReadFromJsonAsync<BotStageUpdateDto>(ctx.RequestAborted).ConfigureAwait(false);
    if (dto == null) return Results.BadRequest();
    var updated = await repo.UpdateAsync(id, dto, ctx.RequestAborted).ConfigureAwait(false);
    return updated == null ? Results.NotFound() : Results.Json(updated);
}).WithName("StageUpdate");

app.MapDelete("/api/stages/{id:int}", async (int id, HttpContext ctx) =>
{
    using var scope = ctx.RequestServices.CreateScope();
    var repo = scope.ServiceProvider.GetService<IBotStageRepository>();
    if (repo == null) return Results.Json(new { detail = "DB not configured" }, statusCode: 503);
    var deleted = await repo.DeleteAsync(id, ctx.RequestAborted).ConfigureAwait(false);
    return deleted ? Results.Ok() : Results.NotFound();
}).WithName("StageDelete");

// ===== Stage Buttons API =====
app.MapGet("/api/stages/{key}/buttons", async (string key, HttpContext ctx) =>
{
    using var scope = ctx.RequestServices.CreateScope();
    var repo = scope.ServiceProvider.GetService<IBotStageRepository>();
    if (repo == null) return Results.Json(new { detail = "DB not configured" }, statusCode: 503);
    var buttons = await repo.GetButtonsAsync(key, ctx.RequestAborted).ConfigureAwait(false);
    return Results.Json(buttons);
}).WithName("StageButtonsList");

app.MapPost("/api/stages/{id:int}/buttons", async (int id, HttpContext ctx) =>
{
    using var scope = ctx.RequestServices.CreateScope();
    var repo = scope.ServiceProvider.GetService<IBotStageRepository>();
    if (repo == null) return Results.Json(new { detail = "DB not configured" }, statusCode: 503);
    var dto = await ctx.Request.ReadFromJsonAsync<BotStageButtonCreateDto>(ctx.RequestAborted).ConfigureAwait(false);
    if (dto == null) return Results.BadRequest();
    try
    {
        var created = await repo.CreateButtonAsync(id, dto, ctx.RequestAborted).ConfigureAwait(false);
        return Results.Json(created, statusCode: 201);
    }
    catch (Exception ex) { return Results.Json(new { detail = ex.Message }, statusCode: 400); }
}).WithName("StageButtonCreate");

app.MapPut("/api/buttons/{id:int}", async (int id, HttpContext ctx) =>
{
    using var scope = ctx.RequestServices.CreateScope();
    var repo = scope.ServiceProvider.GetService<IBotStageRepository>();
    if (repo == null) return Results.Json(new { detail = "DB not configured" }, statusCode: 503);
    var dto = await ctx.Request.ReadFromJsonAsync<BotStageButtonUpdateDto>(ctx.RequestAborted).ConfigureAwait(false);
    if (dto == null) return Results.BadRequest();
    var updated = await repo.UpdateButtonAsync(id, dto, ctx.RequestAborted).ConfigureAwait(false);
    return updated == null ? Results.NotFound() : Results.Json(updated);
}).WithName("StageButtonUpdate");

app.MapDelete("/api/buttons/{id:int}", async (int id, HttpContext ctx) =>
{
    using var scope = ctx.RequestServices.CreateScope();
    var repo = scope.ServiceProvider.GetService<IBotStageRepository>();
    if (repo == null) return Results.Json(new { detail = "DB not configured" }, statusCode: 503);
    var deleted = await repo.DeleteButtonAsync(id, ctx.RequestAborted).ConfigureAwait(false);
    return deleted ? Results.Ok() : Results.NotFound();
}).WithName("StageButtonDelete");

// ===== Permissions API =====
app.MapGet("/api/permissions", async (HttpContext ctx) =>
{
    using var scope = ctx.RequestServices.CreateScope();
    var repo = scope.ServiceProvider.GetService<IPermissionRepository>();
    if (repo == null) return Results.Json(new { detail = "DB not configured" }, statusCode: 503);
    var perms = await repo.ListAllAsync(ctx.RequestAborted).ConfigureAwait(false);
    return Results.Json(perms);
}).WithName("PermissionsList");

app.MapPost("/api/permissions", async (HttpContext ctx) =>
{
    using var scope = ctx.RequestServices.CreateScope();
    var repo = scope.ServiceProvider.GetService<IPermissionRepository>();
    if (repo == null) return Results.Json(new { detail = "DB not configured" }, statusCode: 503);
    var dto = await ctx.Request.ReadFromJsonAsync<PermissionCreateDto>(ctx.RequestAborted).ConfigureAwait(false);
    if (dto == null || string.IsNullOrWhiteSpace(dto.PermissionKey)) return Results.BadRequest(new { detail = "PermissionKey is required" });
    try
    {
        var created = await repo.CreateAsync(dto, ctx.RequestAborted).ConfigureAwait(false);
        return Results.Json(created, statusCode: 201);
    }
    catch (Exception ex) { return Results.Json(new { detail = ex.Message }, statusCode: 400); }
}).WithName("PermissionCreate");

app.MapDelete("/api/permissions/{key}", async (string key, HttpContext ctx) =>
{
    using var scope = ctx.RequestServices.CreateScope();
    var repo = scope.ServiceProvider.GetService<IPermissionRepository>();
    if (repo == null) return Results.Json(new { detail = "DB not configured" }, statusCode: 503);
    var deleted = await repo.DeleteAsync(key, ctx.RequestAborted).ConfigureAwait(false);
    return deleted ? Results.Ok() : Results.NotFound();
}).WithName("PermissionDelete");

// ===== User Permissions API =====
app.MapGet("/api/users/{userId:long}/permissions", async (long userId, HttpContext ctx) =>
{
    using var scope = ctx.RequestServices.CreateScope();
    var repo = scope.ServiceProvider.GetService<IPermissionRepository>();
    if (repo == null) return Results.Json(new { detail = "DB not configured" }, statusCode: 503);
    var perms = await repo.GetAllUserPermissionsAsync(userId, ctx.RequestAborted).ConfigureAwait(false);
    return Results.Json(perms);
}).WithName("UserPermissionsList");

app.MapPost("/api/users/{userId:long}/permissions/{permKey}", async (long userId, string permKey, HttpContext ctx) =>
{
    using var scope = ctx.RequestServices.CreateScope();
    var repo = scope.ServiceProvider.GetService<IPermissionRepository>();
    if (repo == null) return Results.Json(new { detail = "DB not configured" }, statusCode: 503);
    await repo.GrantPermissionAsync(userId, permKey, ctx.RequestAborted).ConfigureAwait(false);
    return Results.Ok(new { granted = true });
}).WithName("UserPermissionGrant");

app.MapDelete("/api/users/{userId:long}/permissions/{permKey}", async (long userId, string permKey, HttpContext ctx) =>
{
    using var scope = ctx.RequestServices.CreateScope();
    var repo = scope.ServiceProvider.GetService<IPermissionRepository>();
    if (repo == null) return Results.Json(new { detail = "DB not configured" }, statusCode: 503);
    await repo.RevokePermissionAsync(userId, permKey, ctx.RequestAborted).ConfigureAwait(false);
    return Results.Ok(new { revoked = true });
}).WithName("UserPermissionRevoke");

// ===== KYC Review API =====
app.MapGet("/api/kyc/pending", async (HttpContext ctx) =>
{
    try
    {
        using var scope = ctx.RequestServices.CreateScope();
        var userRepo = scope.ServiceProvider.GetService<ITelegramUserRepository>();
        if (userRepo == null) return Results.Json(new { detail = "DB not configured" }, statusCode: 503);
        var allUsers = await userRepo.ListAllAsync(ctx.RequestAborted).ConfigureAwait(false);
        var pending = allUsers
            .Where(u => string.Equals(u.KycStatus, "pending_review", StringComparison.OrdinalIgnoreCase))
            .Select(u => new
            {
                telegramUserId = u.TelegramUserId,
                username = u.Username,
                firstName = u.FirstName,
                lastName = u.LastName,
                phoneNumber = u.PhoneNumber,
                email = u.Email,
                emailVerified = u.EmailVerified,
                country = u.Country,
                kycStatus = u.KycStatus,
                verificationPhotoFileId = u.VerificationPhotoFileId
            });
        return Results.Json(pending);
    }
    catch (Exception ex) { return Results.Json(new { error = ex.Message }, statusCode: 500); }
}).WithName("KycPending");

app.MapGet("/api/kyc/all", async (HttpContext ctx) =>
{
    try
    {
        using var scope = ctx.RequestServices.CreateScope();
        var userRepo = scope.ServiceProvider.GetService<ITelegramUserRepository>();
        if (userRepo == null) return Results.Json(new { detail = "DB not configured" }, statusCode: 503);
        var allUsers = await userRepo.ListAllAsync(ctx.RequestAborted).ConfigureAwait(false);
        var kycUsers = allUsers
            .Where(u => !string.IsNullOrEmpty(u.KycStatus) && u.KycStatus != "none")
            .Select(u => new
            {
                telegramUserId = u.TelegramUserId,
                username = u.Username,
                firstName = u.FirstName,
                lastName = u.LastName,
                phoneNumber = u.PhoneNumber,
                email = u.Email,
                emailVerified = u.EmailVerified,
                country = u.Country,
                kycStatus = u.KycStatus,
                kycRejectionData = u.KycRejectionData,
                verificationPhotoFileId = u.VerificationPhotoFileId
            });
        return Results.Json(kycUsers);
    }
    catch (Exception ex) { return Results.Json(new { error = ex.Message }, statusCode: 500); }
}).WithName("KycAll");

app.MapGet("/api/kyc/{userId:long}", async (long userId, HttpContext ctx) =>
{
    try
    {
        using var scope = ctx.RequestServices.CreateScope();
        var userRepo = scope.ServiceProvider.GetService<ITelegramUserRepository>();
        if (userRepo == null) return Results.Json(new { detail = "DB not configured" }, statusCode: 503);
        var user = await userRepo.GetByTelegramUserIdAsync(userId, ctx.RequestAborted).ConfigureAwait(false);
        if (user == null) return Results.NotFound();
        return Results.Json(new
        {
            telegramUserId = user.TelegramUserId,
            username = user.Username,
            firstName = user.FirstName,
            lastName = user.LastName,
            phoneNumber = user.PhoneNumber,
            phoneVerified = user.PhoneVerified,
            phoneVerificationMethod = user.PhoneVerificationMethod,
            email = user.Email,
            emailVerified = user.EmailVerified,
            country = user.Country,
            kycStatus = user.KycStatus,
            kycRejectionData = user.KycRejectionData,
            verificationPhotoFileId = user.VerificationPhotoFileId,
            isVerified = user.IsVerified,
            registeredAt = user.RegisteredAt
        });
    }
    catch (Exception ex) { return Results.Json(new { error = ex.Message }, statusCode: 500); }
}).WithName("KycUserDetail");

// Proxy verification photo (downloads from Telegram and returns image bytes)
app.MapGet("/api/kyc/{userId:long}/photo", async (long userId, HttpContext ctx) =>
{
    try
    {
        using var scope = ctx.RequestServices.CreateScope();
        var userRepo = scope.ServiceProvider.GetService<ITelegramUserRepository>();
        if (userRepo == null) return Results.Json(new { detail = "DB not configured" }, statusCode: 503);
        var user = await userRepo.GetByTelegramUserIdAsync(userId, ctx.RequestAborted).ConfigureAwait(false);
        if (user == null || string.IsNullOrEmpty(user.VerificationPhotoFileId))
            return Results.NotFound();

        var token = await ReadTokenAsync(ctx.RequestServices, ctx.RequestAborted).ConfigureAwait(false);
        if (!IsValidTokenFormat(token))
            return Results.Json(new { detail = "Bot token not configured" }, statusCode: 400);

        var client = new TelegramBotClient(token!);
        var file = await client.GetFile(user.VerificationPhotoFileId, ctx.RequestAborted).ConfigureAwait(false);
        var photoUrl = $"https://api.telegram.org/file/bot{token}/{file.FilePath}";

        // Download the image and return it directly (proxy)
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        var imageBytes = await httpClient.GetByteArrayAsync(photoUrl, ctx.RequestAborted).ConfigureAwait(false);
        return Results.File(imageBytes, "image/jpeg");
    }
    catch (Exception ex) { return Results.Json(new { error = ex.Message }, statusCode: 500); }
}).WithName("KycUserPhoto");

app.MapPost("/api/kyc/{userId:long}/approve", async (long userId, HttpContext ctx) =>
{
    try
    {
        using var scope = ctx.RequestServices.CreateScope();
        var userRepo = scope.ServiceProvider.GetService<ITelegramUserRepository>();
        if (userRepo == null) return Results.Json(new { detail = "DB not configured" }, statusCode: 503);

        await userRepo.SetKycStatusAsync(userId, "approved", null, ctx.RequestAborted).ConfigureAwait(false);

        // Notify user via Telegram
        try
        {
            var token = await ReadTokenAsync(ctx.RequestServices, ctx.RequestAborted).ConfigureAwait(false);
            if (IsValidTokenFormat(token))
            {
                var client = new TelegramBotClient(token!);
                var user = await userRepo.GetByTelegramUserIdAsync(userId, ctx.RequestAborted).ConfigureAwait(false);
                var isFa = (user?.PreferredLanguage ?? "fa") == "fa";
                var msg = isFa
                    ? "احراز هویت شما تأیید شد!\nاکنون می‌توانید از تمامی خدمات تبادل ارز استفاده کنید."
                    : "Your identity has been verified!\nYou can now use all currency exchange services.";
                await client.SendMessage(new Telegram.Bot.Types.ChatId(userId), msg, parseMode: Telegram.Bot.Types.Enums.ParseMode.Html, cancellationToken: ctx.RequestAborted).ConfigureAwait(false);
            }
        }
        catch { /* notification best-effort */ }

        return Results.Json(new { success = true, message = "User approved" });
    }
    catch (Exception ex) { return Results.Json(new { error = ex.Message }, statusCode: 500); }
}).WithName("KycApprove");

app.MapPost("/api/kyc/{userId:long}/reject", async (long userId, HttpContext ctx) =>
{
    try
    {
        var body = await ctx.Request.ReadFromJsonAsync<KycRejectRequest>(ctx.RequestAborted).ConfigureAwait(false);
        if (body == null || body.Reasons == null || body.Reasons.Count == 0)
            return Results.BadRequest(new { detail = "Rejection reasons are required" });

        var rejectionJson = System.Text.Json.JsonSerializer.Serialize(body.Reasons);

        using var scope = ctx.RequestServices.CreateScope();
        var userRepo = scope.ServiceProvider.GetService<ITelegramUserRepository>();
        if (userRepo == null) return Results.Json(new { detail = "DB not configured" }, statusCode: 503);

        await userRepo.SetKycStatusAsync(userId, "rejected", rejectionJson, ctx.RequestAborted).ConfigureAwait(false);

        // Notify user via Telegram
        try
        {
            var token = await ReadTokenAsync(ctx.RequestServices, ctx.RequestAborted).ConfigureAwait(false);
            if (IsValidTokenFormat(token))
            {
                var client = new TelegramBotClient(token!);
                var user = await userRepo.GetByTelegramUserIdAsync(userId, ctx.RequestAborted).ConfigureAwait(false);
                var isFa = (user?.PreferredLanguage ?? "fa") == "fa";

                var fieldLabels = new Dictionary<string, (string fa, string en)>
                {
                    ["name"] = ("نام و نام خانوادگی", "Name"),
                    ["phone"] = ("شماره تلفن", "Phone"),
                    ["email"] = ("ایمیل", "Email"),
                    ["country"] = ("کشور", "Country"),
                    ["photo"] = ("عکس تأییدیه", "Verification Photo"),
                };

                var reasonLines = string.Join("\n", body.Reasons.Select(kv =>
                {
                    var label = fieldLabels.ContainsKey(kv.Key)
                        ? (isFa ? fieldLabels[kv.Key].fa : fieldLabels[kv.Key].en)
                        : kv.Key;
                    return $"- <b>{label}</b>: {kv.Value}";
                }));

                var msg = isFa
                    ? $"متأسفانه احراز هویت شما رد شد.\n\nدلایل:\n{reasonLines}\n\nلطفاً موارد را اصلاح و دوباره ارسال کنید."
                    : $"Unfortunately your verification was rejected.\n\nReasons:\n{reasonLines}\n\nPlease fix the issues and resubmit.";

                var keyboard = new Telegram.Bot.Types.ReplyMarkups.InlineKeyboardMarkup(
                    new[] { new[] { Telegram.Bot.Types.ReplyMarkups.InlineKeyboardButton.WithCallbackData(
                        isFa ? "اصلاح و ارسال مجدد" : "Fix and Resubmit", "start_kyc_fix") } });

                await client.SendMessage(new Telegram.Bot.Types.ChatId(userId), msg,
                    parseMode: Telegram.Bot.Types.Enums.ParseMode.Html,
                    replyMarkup: keyboard,
                    cancellationToken: ctx.RequestAborted).ConfigureAwait(false);
            }
        }
        catch { /* notification best-effort */ }

        return Results.Json(new { success = true, message = "User rejected" });
    }
    catch (Exception ex) { return Results.Json(new { error = ex.Message }, statusCode: 500); }
}).WithName("KycReject");

// ===== Support Telegram Username Setting =====
app.MapGet("/api/settings/support-telegram", async (HttpContext ctx) =>
{
    try
    {
        using var scope = ctx.RequestServices.CreateScope();
        var repo = scope.ServiceProvider.GetService<ISettingsRepository>();
        if (repo == null) return Results.Json(new { detail = "DB not configured" }, statusCode: 503);
        var username = await repo.GetValueAsync("SupportTelegramUsername", ctx.RequestAborted).ConfigureAwait(false);
        return Results.Json(new { username = username ?? "" });
    }
    catch (Exception ex) { return Results.Json(new { error = ex.Message }, statusCode: 500); }
}).WithName("SupportTelegramGet");

app.MapPost("/api/settings/support-telegram", async (HttpContext ctx) =>
{
    try
    {
        using var scope = ctx.RequestServices.CreateScope();
        var repo = scope.ServiceProvider.GetService<ISettingsRepository>();
        if (repo == null) return Results.Json(new { detail = "DB not configured" }, statusCode: 503);
        var body = await ctx.Request.ReadFromJsonAsync<SetSupportTelegramRequest>(ctx.RequestAborted).ConfigureAwait(false);
        var username = (body?.Username ?? "").Trim().TrimStart('@');
        if (string.IsNullOrEmpty(username))
            return Results.BadRequest(new { detail = "Username is required" });
        await repo.SetValueAsync("SupportTelegramUsername", username, ctx.RequestAborted).ConfigureAwait(false);
        return Results.Json(new { ok = true, username });
    }
    catch (Exception ex) { return Results.Json(new { error = ex.Message }, statusCode: 500); }
}).WithName("SupportTelegramSet");

// ===== Exchange Rates =====
app.MapGet("/api/exchange/rates", async (HttpContext ctx) =>
{
    try
    {
        using var scope = ctx.RequestServices.CreateScope();
        var repo = scope.ServiceProvider.GetService<IExchangeRepository>();
        if (repo == null) return Results.Json(new { detail = "DB not configured" }, statusCode: 503);
        var rates = await repo.GetRatesAsync(ctx.RequestAborted).ConfigureAwait(false);
        return Results.Json(rates);
    }
    catch (Exception ex) { return Results.Json(new { error = ex.Message }, statusCode: 500); }
}).WithName("ExchangeRatesGet");

app.MapPost("/api/exchange/rates/fetch", async (HttpContext ctx) =>
{
    try
    {
        using var scope = ctx.RequestServices.CreateScope();
        var svc = scope.ServiceProvider.GetService<NavasanApiService>();
        if (svc == null) return Results.Json(new { detail = "Service not configured" }, statusCode: 503);
        var (success, message) = await svc.FetchAndCacheRatesAsync(ctx.RequestAborted).ConfigureAwait(false);
        return Results.Json(new { success, message });
    }
    catch (Exception ex) { return Results.Json(new { error = ex.Message }, statusCode: 500); }
}).WithName("ExchangeRatesFetch");

app.MapGet("/api/exchange/rates/usage", async (HttpContext ctx) =>
{
    try
    {
        using var scope = ctx.RequestServices.CreateScope();
        var svc = scope.ServiceProvider.GetService<NavasanApiService>();
        if (svc == null) return Results.Json(new { detail = "Service not configured" }, statusCode: 503);
        var (used, limit) = await svc.GetUsageAsync(ctx.RequestAborted).ConfigureAwait(false);
        return Results.Json(new { used, limit });
    }
    catch (Exception ex) { return Results.Json(new { error = ex.Message }, statusCode: 500); }
}).WithName("ExchangeRatesUsage");

app.MapPut("/api/exchange/rates/{id:int}", async (int id, HttpContext ctx) =>
{
    try
    {
        using var scope = ctx.RequestServices.CreateScope();
        var repo = scope.ServiceProvider.GetService<IExchangeRepository>();
        if (repo == null) return Results.Json(new { detail = "DB not configured" }, statusCode: 503);
        var body = await ctx.Request.ReadFromJsonAsync<ManualRateUpdate>(ctx.RequestAborted).ConfigureAwait(false);
        if (body == null) return Results.BadRequest(new { detail = "Invalid body" });
        await repo.SaveRateAsync(new ExchangeRateDto(id, body.CurrencyCode ?? "", body.CurrencyNameFa, body.CurrencyNameEn,
            body.Rate, 0, "manual", DateTimeOffset.UtcNow), ctx.RequestAborted).ConfigureAwait(false);
        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return Results.Json(new { error = ex.Message }, statusCode: 500); }
}).WithName("ExchangeRateUpdate");

// ===== Exchange Requests =====
app.MapGet("/api/exchange/requests", async (HttpContext ctx) =>
{
    try
    {
        using var scope = ctx.RequestServices.CreateScope();
        var repo = scope.ServiceProvider.GetService<IExchangeRepository>();
        if (repo == null) return Results.Json(new { detail = "DB not configured" }, statusCode: 503);
        var status = ctx.Request.Query["status"].FirstOrDefault();
        var requests = await repo.ListRequestsAsync(status, null, ctx.RequestAborted).ConfigureAwait(false);
        return Results.Json(requests);
    }
    catch (Exception ex) { return Results.Json(new { error = ex.Message }, statusCode: 500); }
}).WithName("ExchangeRequestsList");

app.MapGet("/api/exchange/requests/{id:int}", async (int id, HttpContext ctx) =>
{
    try
    {
        using var scope = ctx.RequestServices.CreateScope();
        var repo = scope.ServiceProvider.GetService<IExchangeRepository>();
        if (repo == null) return Results.Json(new { detail = "DB not configured" }, statusCode: 503);
        var req = await repo.GetRequestAsync(id, ctx.RequestAborted).ConfigureAwait(false);
        if (req == null) return Results.NotFound(new { detail = "Not found" });
        return Results.Json(req);
    }
    catch (Exception ex) { return Results.Json(new { error = ex.Message }, statusCode: 500); }
}).WithName("ExchangeRequestGet");

app.MapPost("/api/exchange/requests/{id:int}/approve", async (int id, HttpContext ctx) =>
{
    try
    {
        using var scope = ctx.RequestServices.CreateScope();
        var repo = scope.ServiceProvider.GetService<IExchangeRepository>();
        var settingsRepo = scope.ServiceProvider.GetService<ISettingsRepository>();
        var botClient = scope.ServiceProvider.GetService<ITelegramBotClient>();
        if (repo == null) return Results.Json(new { detail = "DB not configured" }, statusCode: 503);

        var req = await repo.GetRequestAsync(id, ctx.RequestAborted).ConfigureAwait(false);
        if (req == null) return Results.NotFound(new { detail = "Not found" });

        // Post to channel
        int? channelMsgId = null;
        if (settingsRepo != null && botClient != null && botClient is not PlaceholderTelegramBotClient)
        {
            var channelId = await settingsRepo.GetValueAsync("exchange_channel_id", ctx.RequestAborted).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(channelId))
            {
                var currFa = AbroadQs.Bot.Modules.Common.ExchangeStateHandler.GetCurrencyNameFa(req.Currency);
                var currFlag = AbroadQs.Bot.Modules.Common.ExchangeStateHandler.GetCurrencyFlag(req.Currency);
                var txFa = req.TransactionType == "buy" ? "خرید" : req.TransactionType == "sell" ? "فروش" : "تبادل";
                var roleFa = req.TransactionType == "buy" ? "خریدار" : req.TransactionType == "sell" ? "فروشنده" : "متقاضی تبادل";
                var txHashtag = $"#{txFa}_{currFa.Replace(" ", "_")}";
                // Delivery label (no sensitive info: no IBAN, no PayPal email, no bank name)
                var deliveryFa = req.DeliveryMethod switch
                {
                    "bank" => $"حواله بانکی{(req.Country != null ? $" ({req.Country})" : "")}",
                    "paypal" => "پی‌پال",
                    "cash" => $"اسکناس (حضوری){(!string.IsNullOrEmpty(req.City) ? $" — {req.City}" : (req.Country != null ? $" — {req.Country}" : ""))}",
                    _ => req.DeliveryMethod
                };

                var adSb = new System.Text.StringBuilder();
                adSb.AppendLine($"❗ آگهی {txFa} ارز {txHashtag}");
                adSb.AppendLine();
                adSb.AppendLine($"👤 {roleFa}: <b>{req.UserDisplayName}</b>");
                adSb.AppendLine($"💰 مبلغ: {currFlag} <b>{req.Amount:N0}</b> {currFa}");
                if (req.TransactionType == "exchange" && !string.IsNullOrEmpty(req.DestinationCurrency))
                {
                    var destFlag = AbroadQs.Bot.Modules.Common.ExchangeStateHandler.GetCurrencyFlag(req.DestinationCurrency);
                    var destFa = AbroadQs.Bot.Modules.Common.ExchangeStateHandler.GetCurrencyNameFa(req.DestinationCurrency);
                    adSb.AppendLine($"➡️ مقصد: {destFlag} <b>{destFa}</b>");
                }
                adSb.AppendLine($"💲 نرخ پیشنهادی: <b>{req.ProposedRate:N0}</b> تومان");
                adSb.AppendLine($"🚚 روش تحویل: {deliveryFa}");
                if (!string.IsNullOrEmpty(req.Description))
                    adSb.AppendLine($"📝 توضیحات: {req.Description}");
                adSb.AppendLine($"\n🏷 مبلغ کل: <b>{req.TotalAmount:N0}</b> تومان");
                var text = adSb.ToString();

                try
                {
                    var chatIdParsed = long.TryParse(channelId, out var cid) ? cid : 0;
                    if (channelId.StartsWith("@") || chatIdParsed != 0)
                    {
                        var targetChat = chatIdParsed != 0
                            ? (Telegram.Bot.Types.ChatId)chatIdParsed
                            : (Telegram.Bot.Types.ChatId)channelId;

                        // Build bid button: deep link to bot with bid_{requestId}
                        Telegram.Bot.Types.ReplyMarkups.InlineKeyboardMarkup? channelKb = null;
                        try
                        {
                            var me = await botClient.GetMe(ctx.RequestAborted).ConfigureAwait(false);
                            if (!string.IsNullOrEmpty(me.Username))
                            {
                                channelKb = new Telegram.Bot.Types.ReplyMarkups.InlineKeyboardMarkup(new[]
                                {
                                    new[] { Telegram.Bot.Types.ReplyMarkups.InlineKeyboardButton.WithUrl("📩 ارسال پیشنهاد", $"https://t.me/{me.Username}?start=bid_{id}") },
                                });
                            }
                        }
                        catch { /* getMe failed — post without button */ }

                        var sent = await botClient.SendMessage(targetChat, text, parseMode: Telegram.Bot.Types.Enums.ParseMode.Html, replyMarkup: channelKb).ConfigureAwait(false);
                        channelMsgId = sent.Id;
                    }
                }
                catch (Exception cex)
                {
                    app.Logger.LogWarning(cex, "Failed to post exchange request #{Num} to channel", req.RequestNumber);
                }
            }
        }

        await repo.UpdateStatusAsync(id, "approved", null, channelMsgId, ctx.RequestAborted).ConfigureAwait(false);

        // Notify user without buttons, then send main menu
        if (botClient != null && botClient is not PlaceholderTelegramBotClient)
        {
            try
            {
                var currFaShort = AbroadQs.Bot.Modules.Common.ExchangeStateHandler.GetCurrencyNameFa(req.Currency);
                var flag = AbroadQs.Bot.Modules.Common.ExchangeStateHandler.GetCurrencyFlag(req.Currency);
                var approveMsg = $"✅ <b>درخواست #{req.RequestNumber} تأیید شد!</b>\n\n" +
                    $"{flag} {req.Amount:N0} {currFaShort} — {req.ProposedRate:N0} تومان\n" +
                    $"💵 مبلغ نهایی: <b>{req.TotalAmount:N0}</b> تومان\n\n" +
                    "📢 آگهی شما در کانال منتشر شد.";

                // Send notification without buttons
                await botClient.SendMessage(req.TelegramUserId, approveMsg,
                    parseMode: Telegram.Bot.Types.Enums.ParseMode.Html).ConfigureAwait(false);

                // Send main menu reply keyboard
                await SendMainMenuFromApi(scope.ServiceProvider, botClient, req.TelegramUserId, ctx.RequestAborted).ConfigureAwait(false);
            }
            catch { }
        }

        return Results.Json(new { ok = true, channelMsgId });
    }
    catch (Exception ex) { return Results.Json(new { error = ex.Message }, statusCode: 500); }
}).WithName("ExchangeRequestApprove");

app.MapPost("/api/exchange/requests/{id:int}/reject", async (int id, HttpContext ctx) =>
{
    try
    {
        using var scope = ctx.RequestServices.CreateScope();
        var repo = scope.ServiceProvider.GetService<IExchangeRepository>();
        var botClient = scope.ServiceProvider.GetService<ITelegramBotClient>();
        if (repo == null) return Results.Json(new { detail = "DB not configured" }, statusCode: 503);

        var body = await ctx.Request.ReadFromJsonAsync<RejectRequest>(ctx.RequestAborted).ConfigureAwait(false);
        var req = await repo.GetRequestAsync(id, ctx.RequestAborted).ConfigureAwait(false);
        if (req == null) return Results.NotFound(new { detail = "Not found" });

        await repo.UpdateStatusAsync(id, "rejected", body?.Note, null, ctx.RequestAborted).ConfigureAwait(false);

        // Notify user without buttons, then send main menu
        if (botClient != null && botClient is not PlaceholderTelegramBotClient)
        {
            try
            {
                var note = !string.IsNullOrEmpty(body?.Note) ? $"\n\n📝 دلیل: {body.Note}" : "";
                var rejectMsg = $"❌ <b>درخواست #{req.RequestNumber} رد شد.</b>{note}\n\n" +
                    "<i>در صورت نیاز می‌توانید درخواست جدیدی ثبت کنید.</i>";

                // Send notification without buttons
                await botClient.SendMessage(req.TelegramUserId, rejectMsg,
                    parseMode: Telegram.Bot.Types.Enums.ParseMode.Html).ConfigureAwait(false);

                // Send main menu reply keyboard
                await SendMainMenuFromApi(scope.ServiceProvider, botClient, req.TelegramUserId, ctx.RequestAborted).ConfigureAwait(false);
            }
            catch { }
        }

        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return Results.Json(new { error = ex.Message }, statusCode: 500); }
}).WithName("ExchangeRequestReject");

// ===== Exchange Channel Setting =====
app.MapGet("/api/settings/exchange-channel", async (HttpContext ctx) =>
{
    try
    {
        using var scope = ctx.RequestServices.CreateScope();
        var repo = scope.ServiceProvider.GetService<ISettingsRepository>();
        if (repo == null) return Results.Json(new { detail = "DB not configured" }, statusCode: 503);
        var channelId = await repo.GetValueAsync("exchange_channel_id", ctx.RequestAborted).ConfigureAwait(false);
        return Results.Json(new { channelId = channelId ?? "" });
    }
    catch (Exception ex) { return Results.Json(new { error = ex.Message }, statusCode: 500); }
}).WithName("ExchangeChannelGet");

app.MapPost("/api/settings/exchange-channel", async (HttpContext ctx) =>
{
    try
    {
        using var scope = ctx.RequestServices.CreateScope();
        var repo = scope.ServiceProvider.GetService<ISettingsRepository>();
        if (repo == null) return Results.Json(new { detail = "DB not configured" }, statusCode: 503);
        var body = await ctx.Request.ReadFromJsonAsync<SetChannelRequest>(ctx.RequestAborted).ConfigureAwait(false);
        var channelId = (body?.ChannelId ?? "").Trim();
        await repo.SetValueAsync("exchange_channel_id", channelId, ctx.RequestAborted).ConfigureAwait(false);
        return Results.Json(new { ok = true, channelId });
    }
    catch (Exception ex) { return Results.Json(new { error = ex.Message }, statusCode: 500); }
}).WithName("ExchangeChannelSet");

// ===== Exchange Fee Setting =====
app.MapGet("/api/settings/exchange-fee", async (HttpContext ctx) =>
{
    try
    {
        using var scope = ctx.RequestServices.CreateScope();
        var repo = scope.ServiceProvider.GetService<ISettingsRepository>();
        if (repo == null) return Results.Json(new { detail = "DB not configured" }, statusCode: 503);
        var fee = await repo.GetValueAsync("exchange_fee_percent", ctx.RequestAborted).ConfigureAwait(false);
        return Results.Json(new { feePercent = fee ?? "0" });
    }
    catch (Exception ex) { return Results.Json(new { error = ex.Message }, statusCode: 500); }
}).WithName("ExchangeFeeGet");

app.MapPost("/api/settings/exchange-fee", async (HttpContext ctx) =>
{
    try
    {
        using var scope = ctx.RequestServices.CreateScope();
        var repo = scope.ServiceProvider.GetService<ISettingsRepository>();
        if (repo == null) return Results.Json(new { detail = "DB not configured" }, statusCode: 503);
        var body = await ctx.Request.ReadFromJsonAsync<SetFeeRequest>(ctx.RequestAborted).ConfigureAwait(false);
        await repo.SetValueAsync("exchange_fee_percent", (body?.FeePercent ?? "0").Trim(), ctx.RequestAborted).ConfigureAwait(false);
        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return Results.Json(new { error = ex.Message }, statusCode: 500); }
}).WithName("ExchangeFeeSet");

// ===== Ad Pricing Setting =====
app.MapGet("/api/settings/ad-pricing", async (HttpContext ctx) =>
{
    try
    {
        using var scope = ctx.RequestServices.CreateScope();
        var repo = scope.ServiceProvider.GetService<ISettingsRepository>();
        if (repo == null) return Results.Json(new { mode = "free", price = "0", paymentMethod = "wallet", commissionTiming = "after_match", commissionPercent = "0", commissionFixed = "0" });
        var mode = await repo.GetValueAsync("ad_pricing_mode", ctx.RequestAborted).ConfigureAwait(false) ?? "free";
        var price = await repo.GetValueAsync("ad_price_amount", ctx.RequestAborted).ConfigureAwait(false) ?? "0";
        var paymentMethod = await repo.GetValueAsync("ad_payment_method", ctx.RequestAborted).ConfigureAwait(false) ?? "wallet";
        var commissionTiming = await repo.GetValueAsync("commission_timing", ctx.RequestAborted).ConfigureAwait(false) ?? "after_match";
        var commissionPercent = await repo.GetValueAsync("commission_percent", ctx.RequestAborted).ConfigureAwait(false) ?? "0";
        var commissionFixed = await repo.GetValueAsync("commission_fixed_amount", ctx.RequestAborted).ConfigureAwait(false) ?? "0";
        return Results.Json(new { mode, price, paymentMethod, commissionTiming, commissionPercent, commissionFixed });
    }
    catch (Exception ex) { return Results.Json(new { error = ex.Message }, statusCode: 500); }
}).WithName("AdPricingGet");

app.MapPost("/api/settings/ad-pricing", async (HttpContext ctx) =>
{
    try
    {
        using var scope = ctx.RequestServices.CreateScope();
        var repo = scope.ServiceProvider.GetService<ISettingsRepository>();
        if (repo == null) return Results.Json(new { detail = "DB not configured" }, statusCode: 503);
        var body = await ctx.Request.ReadFromJsonAsync<Dictionary<string, string>>(ctx.RequestAborted).ConfigureAwait(false);
        if (body != null)
        {
            if (body.ContainsKey("mode")) await repo.SetValueAsync("ad_pricing_mode", body["mode"], ctx.RequestAborted).ConfigureAwait(false);
            if (body.ContainsKey("price")) await repo.SetValueAsync("ad_price_amount", body["price"], ctx.RequestAborted).ConfigureAwait(false);
            if (body.ContainsKey("paymentMethod")) await repo.SetValueAsync("ad_payment_method", body["paymentMethod"], ctx.RequestAborted).ConfigureAwait(false);
            if (body.ContainsKey("commissionTiming")) await repo.SetValueAsync("commission_timing", body["commissionTiming"], ctx.RequestAborted).ConfigureAwait(false);
            if (body.ContainsKey("commissionPercent")) await repo.SetValueAsync("commission_percent", body["commissionPercent"], ctx.RequestAborted).ConfigureAwait(false);
            if (body.ContainsKey("commissionFixed")) await repo.SetValueAsync("commission_fixed_amount", body["commissionFixed"], ctx.RequestAborted).ConfigureAwait(false);
        }
        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return Results.Json(new { error = ex.Message }, statusCode: 500); }
}).WithName("AdPricingSet");

// ===== Payment Gateway Settings API =====
app.MapGet("/api/settings/payment-gateway", async (HttpContext ctx) =>
{
    try
    {
        using var scope = ctx.RequestServices.CreateScope();
        var repo = scope.ServiceProvider.GetService<ISettingsRepository>();
        if (repo == null) return Results.Json(new { detail = "DB not configured" }, statusCode: 503);
        var apiKey = await repo.GetValueAsync("bitpay_api_key", ctx.RequestAborted).ConfigureAwait(false);
        var testMode = await repo.GetValueAsync("bitpay_test_mode", ctx.RequestAborted).ConfigureAwait(false);
        var webhookUrlVal = await repo.GetValueAsync("webhook_url", ctx.RequestAborted).ConfigureAwait(false);
        return Results.Json(new
        {
            bitpayApiKey = string.IsNullOrEmpty(apiKey) ? "" : apiKey[..Math.Min(6, apiKey.Length)] + "****",
            bitpayTestMode = testMode == "true",
            webhookUrl = webhookUrlVal ?? ""
        });
    }
    catch (Exception ex) { return Results.Json(new { error = ex.Message }, statusCode: 500); }
}).WithName("PaymentGatewayGet");

app.MapPut("/api/settings/payment-gateway", async (HttpContext ctx) =>
{
    try
    {
        using var scope = ctx.RequestServices.CreateScope();
        var repo = scope.ServiceProvider.GetService<ISettingsRepository>();
        if (repo == null) return Results.Json(new { detail = "DB not configured" }, statusCode: 503);
        var body = await ctx.Request.ReadFromJsonAsync<Dictionary<string, string>>(ctx.RequestAborted).ConfigureAwait(false);
        if (body != null)
        {
            if (body.ContainsKey("bitpayApiKey") && !string.IsNullOrWhiteSpace(body["bitpayApiKey"]))
                await repo.SetValueAsync("bitpay_api_key", body["bitpayApiKey"].Trim(), ctx.RequestAborted).ConfigureAwait(false);
            if (body.ContainsKey("bitpayTestMode"))
                await repo.SetValueAsync("bitpay_test_mode", body["bitpayTestMode"].Trim(), ctx.RequestAborted).ConfigureAwait(false);
        }
        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return Results.Json(new { error = ex.Message }, statusCode: 500); }
}).WithName("PaymentGatewaySet");

// ===== Exchange Groups API =====
app.MapGet("/api/exchange-groups", async (HttpContext ctx) =>
{
    try
    {
        using var scope = ctx.RequestServices.CreateScope();
        var db = scope.ServiceProvider.GetService<AbroadQs.Bot.Data.ApplicationDbContext>();
        if (db == null) return Results.Json(Array.Empty<object>());
        var groups = await db.ExchangeGroups.OrderByDescending(g => g.CreatedAt).ToListAsync(ctx.RequestAborted).ConfigureAwait(false);
        return Results.Json(groups.Select(g => new { g.Id, g.Name, g.CurrencyCode, g.CountryCode, g.TelegramGroupLink, g.Description, g.Status, g.AdminNote, g.MemberCount, g.SubmittedByUserId, g.CreatedAt }));
    }
    catch (Exception ex) { return Results.Json(new { error = ex.Message }, statusCode: 500); }
}).WithName("ExchangeGroupsList");

app.MapPost("/api/exchange-groups", async (HttpContext ctx) =>
{
    try
    {
        using var scope = ctx.RequestServices.CreateScope();
        var db = scope.ServiceProvider.GetService<AbroadQs.Bot.Data.ApplicationDbContext>();
        if (db == null) return Results.Json(new { detail = "DB not configured" }, statusCode: 503);
        var body = await ctx.Request.ReadFromJsonAsync<Dictionary<string, string>>(ctx.RequestAborted).ConfigureAwait(false);
        if (body == null || !body.ContainsKey("name") || !body.ContainsKey("telegramGroupLink"))
            return Results.BadRequest(new { detail = "name and telegramGroupLink required" });
        var entity = new AbroadQs.Bot.Data.ExchangeGroupEntity
        {
            Name = body["name"],
            TelegramGroupLink = body["telegramGroupLink"],
            CurrencyCode = body.GetValueOrDefault("currencyCode"),
            CountryCode = body.GetValueOrDefault("countryCode"),
            Description = body.GetValueOrDefault("description"),
            Status = "approved",
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.ExchangeGroups.Add(entity);
        await db.SaveChangesAsync(ctx.RequestAborted).ConfigureAwait(false);
        return Results.Json(new { ok = true, id = entity.Id });
    }
    catch (Exception ex) { return Results.Json(new { error = ex.Message }, statusCode: 500); }
}).WithName("ExchangeGroupsCreate");

app.MapPost("/api/exchange-groups/{id}/approve", async (int id, HttpContext ctx) =>
{
    try
    {
        using var scope = ctx.RequestServices.CreateScope();
        var db = scope.ServiceProvider.GetService<AbroadQs.Bot.Data.ApplicationDbContext>();
        var botClient = scope.ServiceProvider.GetService<ITelegramBotClient>();
        if (db == null) return Results.Json(new { detail = "DB not configured" }, statusCode: 503);
        var g = await db.ExchangeGroups.FindAsync(new object[] { id }, ctx.RequestAborted).ConfigureAwait(false);
        if (g == null) return Results.NotFound();
        g.Status = "approved"; g.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ctx.RequestAborted).ConfigureAwait(false);

        // Notify user who submitted the group
        if (g.SubmittedByUserId.HasValue && botClient != null && botClient is not PlaceholderTelegramBotClient)
        {
            try
            {
                var notifyMsg = $"✅ <b>گروه شما تأیید شد!</b>\n\n🔗 {g.TelegramGroupLink}\n\nگروه شما در لیست گروه‌های تبادل ارز نمایش داده می‌شود.";
                await botClient.SendMessage(g.SubmittedByUserId.Value, notifyMsg, parseMode: Telegram.Bot.Types.Enums.ParseMode.Html).ConfigureAwait(false);
                await SendMainMenuFromApi(scope.ServiceProvider, botClient, g.SubmittedByUserId.Value, ctx.RequestAborted).ConfigureAwait(false);
            }
            catch { }
        }

        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return Results.Json(new { error = ex.Message }, statusCode: 500); }
}).WithName("ExchangeGroupsApprove");

app.MapPost("/api/exchange-groups/{id}/reject", async (int id, HttpContext ctx) =>
{
    try
    {
        using var scope = ctx.RequestServices.CreateScope();
        var db = scope.ServiceProvider.GetService<AbroadQs.Bot.Data.ApplicationDbContext>();
        var botClient = scope.ServiceProvider.GetService<ITelegramBotClient>();
        if (db == null) return Results.Json(new { detail = "DB not configured" }, statusCode: 503);

        var body = await ctx.Request.ReadFromJsonAsync<GroupRejectRequest>(ctx.RequestAborted).ConfigureAwait(false);
        var g = await db.ExchangeGroups.FindAsync(new object[] { id }, ctx.RequestAborted).ConfigureAwait(false);
        if (g == null) return Results.NotFound();
        g.Status = "rejected"; g.UpdatedAt = DateTimeOffset.UtcNow;
        if (!string.IsNullOrEmpty(body?.Note)) g.AdminNote = body.Note;
        await db.SaveChangesAsync(ctx.RequestAborted).ConfigureAwait(false);

        // Notify user who submitted the group
        if (g.SubmittedByUserId.HasValue && botClient != null && botClient is not PlaceholderTelegramBotClient)
        {
            try
            {
                var reasonText = !string.IsNullOrEmpty(body?.Note) ? $"\n\n📝 دلیل: {body.Note}" : "";
                var notifyMsg = $"❌ <b>گروه شما رد شد.</b>\n\n🔗 {g.TelegramGroupLink}{reasonText}\n\n<i>در صورت نیاز می‌توانید گروه جدیدی ثبت کنید.</i>";
                await botClient.SendMessage(g.SubmittedByUserId.Value, notifyMsg, parseMode: Telegram.Bot.Types.Enums.ParseMode.Html).ConfigureAwait(false);
                await SendMainMenuFromApi(scope.ServiceProvider, botClient, g.SubmittedByUserId.Value, ctx.RequestAborted).ConfigureAwait(false);
            }
            catch { }
        }

        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return Results.Json(new { error = ex.Message }, statusCode: 500); }
}).WithName("ExchangeGroupsReject");

app.MapDelete("/api/exchange-groups/{id}", async (int id, HttpContext ctx) =>
{
    try
    {
        using var scope = ctx.RequestServices.CreateScope();
        var db = scope.ServiceProvider.GetService<AbroadQs.Bot.Data.ApplicationDbContext>();
        if (db == null) return Results.Json(new { detail = "DB not configured" }, statusCode: 503);
        var g = await db.ExchangeGroups.FindAsync(new object[] { id }, ctx.RequestAborted).ConfigureAwait(false);
        if (g == null) return Results.NotFound();
        db.ExchangeGroups.Remove(g);
        await db.SaveChangesAsync(ctx.RequestAborted).ConfigureAwait(false);
        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return Results.Json(new { error = ex.Message }, statusCode: 500); }
}).WithName("ExchangeGroupsDelete");

// ===== Bids API =====
app.MapGet("/api/bids/{requestId}", async (int requestId, HttpContext ctx) =>
{
    try
    {
        using var scope = ctx.RequestServices.CreateScope();
        var bidRepo = scope.ServiceProvider.GetService<IBidRepository>();
        if (bidRepo == null) return Results.Json(Array.Empty<object>());
        var bids = await bidRepo.GetBidsForRequestAsync(requestId, ctx.RequestAborted).ConfigureAwait(false);
        return Results.Json(bids);
    }
    catch (Exception ex) { return Results.Json(new { error = ex.Message }, statusCode: 500); }
}).WithName("BidsList");

app.MapPost("/api/bids/{bidId}/accept", async (int bidId, HttpContext ctx) =>
{
    try
    {
        using var scope = ctx.RequestServices.CreateScope();
        var bidRepo = scope.ServiceProvider.GetService<IBidRepository>();
        if (bidRepo == null) return Results.Json(new { detail = "DB not configured" }, statusCode: 503);
        var bid = await bidRepo.GetBidAsync(bidId, ctx.RequestAborted).ConfigureAwait(false);
        if (bid == null) return Results.NotFound();
        await bidRepo.UpdateBidStatusAsync(bidId, "accepted", ctx.RequestAborted).ConfigureAwait(false);
        // Reject all other pending bids for same request
        var allBids = await bidRepo.GetBidsForRequestAsync(bid.ExchangeRequestId, ctx.RequestAborted).ConfigureAwait(false);
        foreach (var other in allBids.Where(b => b.Id != bidId && b.Status == "pending"))
            await bidRepo.UpdateBidStatusAsync(other.Id, "rejected", ctx.RequestAborted).ConfigureAwait(false);
        // Update exchange request status to matched
        var excRepo = scope.ServiceProvider.GetService<IExchangeRepository>();
        if (excRepo != null)
            await excRepo.UpdateStatusAsync(bid.ExchangeRequestId, "matched", null, null, ctx.RequestAborted).ConfigureAwait(false);
        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return Results.Json(new { error = ex.Message }, statusCode: 500); }
}).WithName("BidAccept");

app.MapPost("/api/bids/{bidId}/reject", async (int bidId, HttpContext ctx) =>
{
    try
    {
        using var scope = ctx.RequestServices.CreateScope();
        var bidRepo = scope.ServiceProvider.GetService<IBidRepository>();
        if (bidRepo == null) return Results.Json(new { detail = "DB not configured" }, statusCode: 503);
        await bidRepo.UpdateBidStatusAsync(bidId, "rejected", ctx.RequestAborted).ConfigureAwait(false);
        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return Results.Json(new { error = ex.Message }, statusCode: 500); }
}).WithName("BidReject");

// ===== Payment Callback =====
app.MapGet("/api/payment/callback", async (HttpContext ctx) =>
{
    string status = "error";
    string title = "خطا";
    string message = "پارامترها نامعتبر هستند.";
    string amountText = "";
    string botUsername = "AbroadQsBot";

    try
    {
        using var scope = ctx.RequestServices.CreateScope();
        var walletRepo = scope.ServiceProvider.GetService<IWalletRepository>();
        var bitpay = scope.ServiceProvider.GetService<AbroadQs.Bot.Host.Webhook.Services.BitPayService>();
        var settingsRepo = scope.ServiceProvider.GetService<ISettingsRepository>();
        var sender = scope.ServiceProvider.GetService<IResponseSender>();
        botUsername = settingsRepo != null
            ? await settingsRepo.GetValueAsync("bot_username", ctx.RequestAborted).ConfigureAwait(false) ?? "AbroadQsBot"
            : "AbroadQsBot";

        var idGetStr = ctx.Request.Query["id_get"].ToString();
        var transId = ctx.Request.Query["trans_id"].ToString();

        if (!long.TryParse(idGetStr, out var idGet) || string.IsNullOrEmpty(transId))
        {
            status = "error"; title = "خطا"; message = "پارامترهای بازگشتی نامعتبر هستند.";
        }
        else if (walletRepo == null || bitpay == null)
        {
            status = "error"; title = "خطا"; message = "سرویس پرداخت در دسترس نیست.";
        }
        else
        {
            var payment = await walletRepo.GetPaymentByIdGetAsync(idGet, ctx.RequestAborted).ConfigureAwait(false);
            if (payment == null)
            {
                status = "error"; title = "خطا"; message = "تراکنش یافت نشد.";
            }
            else if (payment.Status == "success")
            {
                status = "success"; title = "پرداخت قبلاً تأیید شده";
                amountText = $"{payment.Amount / 10m:N0} تومان";
                message = $"این پرداخت قبلاً با موفقیت تأیید و اعمال شده است.";
            }
            else
            {
                var result = await bitpay.VerifyPaymentAsync(idGet, transId, ctx.RequestAborted).ConfigureAwait(false);
                amountText = $"{payment.Amount / 10m:N0} تومان";

                if (result.Success)
                {
                    await walletRepo.UpdatePaymentStatusAsync(payment.Id, "success", transId, ctx.RequestAborted).ConfigureAwait(false);
                    if (payment.Purpose == "wallet_charge")
                        await walletRepo.CreditAsync(payment.TelegramUserId, payment.Amount, "شارژ کیف پول", payment.Id.ToString(), ctx.RequestAborted).ConfigureAwait(false);

                    status = "success"; title = "پرداخت موفق";
                    message = "مبلغ با موفقیت به کیف پول شما اضافه شد.";

                    // Send Telegram notification to user
                    try
                    {
                        if (sender != null)
                        {
                            // Fetch updated balance
                            decimal newBalance = 0;
                            try { newBalance = await walletRepo.GetBalanceAsync(payment.TelegramUserId, ctx.RequestAborted).ConfigureAwait(false); } catch { }
                            var chargedToman = payment.Amount / 10m;
                            var tgMsg = $"✅ <b>پرداخت موفق</b>\n━━━━━━━━━━━━━━━━━━━\n\n" +
                                        $"💰 مبلغ واریزی: <b>{chargedToman:N0}</b> تومان\n" +
                                        $"💳 موجودی جدید: <b>{newBalance:N0}</b> تومان\n" +
                                        $"🔢 شماره تراکنش: <code>{transId}</code>\n\n" +
                                        $"مبلغ با موفقیت به کیف پول شما اضافه شد. ✨";
                            await sender.SendTextMessageAsync(payment.TelegramUserId, tgMsg, ctx.RequestAborted).ConfigureAwait(false);
                        }
                    }
                    catch { /* notification failure is non-critical */ }
                }
                else
                {
                    await walletRepo.UpdatePaymentStatusAsync(payment.Id, "failed", transId, ctx.RequestAborted).ConfigureAwait(false);
                    status = "failed"; title = "پرداخت ناموفق";
                    message = "پرداخت تأیید نشد. در صورتی که مبلغ از حساب شما کسر شده، طی ۷۲ ساعت به حساب شما بازگردانده می‌شود.";

                    // Send Telegram notification for failure
                    try
                    {
                        if (sender != null)
                        {
                            var tgMsg = $"❌ <b>پرداخت ناموفق</b>\n━━━━━━━━━━━━━━━━━━━\n\n" +
                                        $"💰 مبلغ: <b>{payment.Amount / 10m:N0}</b> تومان\n\n" +
                                        $"پرداخت تأیید نشد. در صورت کسر مبلغ، طی ۷۲ ساعت بازگردانده می‌شود.";
                            await sender.SendTextMessageAsync(payment.TelegramUserId, tgMsg, ctx.RequestAborted).ConfigureAwait(false);
                        }
                    }
                    catch { /* notification failure is non-critical */ }
                }
            }
        }
    }
    catch
    {
        status = "error"; title = "خطای سیستمی"; message = "خطایی رخ داد. لطفاً دوباره تلاش کنید.";
    }

    // Build the HTML response page
    var icon = status switch { "success" => "✅", "failed" => "❌", _ => "⚠️" };
    var color = status switch { "success" => "#22c55e", "failed" => "#ef4444", _ => "#f59e0b" };
    var bgColor = status switch { "success" => "#f0fdf4", "failed" => "#fef2f2", _ => "#fffbeb" };

    var html = $@"<!DOCTYPE html>
<html lang=""fa"" dir=""rtl"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>{title} - AbroadQs</title>
    <style>
        * {{ margin: 0; padding: 0; box-sizing: border-box; }}
        body {{
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Tahoma, Arial, sans-serif;
            background: linear-gradient(135deg, #0f172a 0%, #1e293b 100%);
            min-height: 100vh;
            display: flex;
            align-items: center;
            justify-content: center;
            padding: 20px;
        }}
        .card {{
            background: #fff;
            border-radius: 20px;
            box-shadow: 0 25px 50px rgba(0,0,0,0.25);
            max-width: 420px;
            width: 100%;
            overflow: hidden;
            animation: slideUp 0.5s ease-out;
        }}
        @keyframes slideUp {{
            from {{ opacity: 0; transform: translateY(30px); }}
            to {{ opacity: 1; transform: translateY(0); }}
        }}
        .card-header {{
            background: {bgColor};
            padding: 40px 30px 30px;
            text-align: center;
            border-bottom: 1px solid rgba(0,0,0,0.05);
        }}
        .icon {{ font-size: 64px; margin-bottom: 16px; }}
        .card-header h1 {{
            font-size: 24px;
            color: {color};
            margin-bottom: 8px;
        }}
        .amount {{
            font-size: 28px;
            font-weight: 700;
            color: #1e293b;
            margin: 12px 0;
        }}
        .card-body {{
            padding: 30px;
            text-align: center;
        }}
        .card-body p {{
            color: #64748b;
            font-size: 15px;
            line-height: 1.7;
            margin-bottom: 24px;
        }}
        .btn {{
            display: inline-flex;
            align-items: center;
            justify-content: center;
            gap: 8px;
            width: 100%;
            padding: 16px 24px;
            border-radius: 12px;
            font-size: 16px;
            font-weight: 600;
            text-decoration: none;
            transition: all 0.2s;
            cursor: pointer;
            border: none;
        }}
        .btn-primary {{
            background: linear-gradient(135deg, #2563eb, #3b82f6);
            color: #fff;
            box-shadow: 0 4px 15px rgba(37,99,235,0.4);
        }}
        .btn-primary:hover {{
            transform: translateY(-2px);
            box-shadow: 0 6px 20px rgba(37,99,235,0.5);
        }}
        .logo {{
            margin-top: 20px;
            font-size: 13px;
            color: #94a3b8;
        }}
    </style>
</head>
<body>
    <div class=""card"">
        <div class=""card-header"">
            <div class=""icon"">{icon}</div>
            <h1>{title}</h1>
            {(string.IsNullOrEmpty(amountText) ? "" : $@"<div class=""amount"">{amountText}</div>")}
        </div>
        <div class=""card-body"">
            <p>{message}</p>
            <a href=""https://t.me/{botUsername}"" class=""btn btn-primary"">
                🤖 بازگشت به ربات
            </a>
            <div class=""logo"">AbroadQs © 2026</div>
        </div>
    </div>
</body>
</html>";

    ctx.Response.ContentType = "text/html; charset=utf-8";
    await ctx.Response.WriteAsync(html, ctx.RequestAborted);
    return Results.Empty;
}).WithName("PaymentCallback");

// ===== Wallet API =====
app.MapGet("/api/wallet/{telegramUserId}", async (long telegramUserId, HttpContext ctx) =>
{
    try
    {
        using var scope = ctx.RequestServices.CreateScope();
        var walletRepo = scope.ServiceProvider.GetService<IWalletRepository>();
        if (walletRepo == null) return Results.Json(new { balance = 0 });
        var wallet = await walletRepo.GetOrCreateWalletAsync(telegramUserId, ctx.RequestAborted).ConfigureAwait(false);
        return Results.Json(new { wallet.Balance, wallet.TelegramUserId });
    }
    catch (Exception ex) { return Results.Json(new { error = ex.Message }, statusCode: 500); }
}).WithName("WalletGet");

// تست: اگر این 200 برگردونه، مسیرهای API درست ثبت شدن
app.MapGet("/api/ping", () => Results.Json(new { ok = true })).WithName("Ping");

// API: توکن ربات (برای تب داشبورد) — اول از دیتابیس، بعد از فایل
const string TokenSettingKey = "Telegram.BotToken";
app.MapGet("/api/settings/token", async (HttpContext ctx) =>
{
    var token = await ReadTokenAsync(ctx.RequestServices, ctx.RequestAborted).ConfigureAwait(false);
    var isSet = token.Length > 0 && token != "<YOUR_BOT_TOKEN>" && token != "0:placeholder";
    var masked = isSet ? token[..Math.Min(4, token.Length)] + "***" + (token.Length > 7 ? token[^4..] : "") : "";
    var reveal = string.Equals(ctx.Request.Query["reveal"], "true", StringComparison.OrdinalIgnoreCase);
    var payload = new { set = isSet, masked };
    if (reveal && isSet)
        return Results.Json(new { set = isSet, masked, token });
    return Results.Json(payload);
}).WithName("TokenInfo");

app.MapPost("/api/settings/token", async (HttpContext ctx) =>
{
    var body = await ctx.Request.ReadFromJsonAsync<SetTokenRequest>(ctx.RequestAborted).ConfigureAwait(false);
    var token = body?.Token?.Trim();
    if (string.IsNullOrEmpty(token))
        return Results.BadRequest(new { detail = "توکن الزامی است." });
    var json = System.Text.Json.JsonSerializer.Serialize(new { Telegram = new { BotToken = token, WebhookUrl = (string?)null } }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

    // ۱) فایل: همیشه در مسیر ثابت (پوشهٔ اجرای اپ) ذخیره کن تا رفرش همان را بخواند
    var tokenFilePath = GetTokenFilePath(ctx.RequestServices);
    try { await System.IO.File.WriteAllTextAsync(tokenFilePath, json, ctx.RequestAborted).ConfigureAwait(false); }
    catch (Exception ex) { return Results.Json(new { detail = "خطا در ذخیره فایل: " + ex.Message }, statusCode: 500); }

    // ۲) دیتابیس: اگر SQL Server وصل بود، اینجا هم ذخیره می‌شود
    try
    {
        using var scope = ctx.RequestServices.CreateScope();
        var settingsRepo = scope.ServiceProvider.GetService<ISettingsRepository>();
        if (settingsRepo != null)
            await settingsRepo.SetValueAsync(TokenSettingKey, token, ctx.RequestAborted).ConfigureAwait(false);
    }
    catch { /* اگر DB خطا داد، فایل قبلاً ذخیره شده؛ داشبورد از فایل می‌خواند */ }

    return Results.Json(new { success = true, message = "Token saved. Refresh the page; if it disappears, check SQL Server (Docker)." });
}).WithName("TokenSet");

// API: حالت به‌روزرسانی (Webhook یا GetUpdates) — اول از دیتابیس، بعد از config/فایل
const string UpdateModeSettingKey = "Telegram.UpdateMode";
app.MapGet("/api/settings/update-mode", async (HttpContext ctx) =>
{
    var mode = await ReadUpdateModeAsync(ctx.RequestServices, ctx.RequestAborted).ConfigureAwait(false);
    return Results.Json(new { mode });
}).WithName("UpdateModeInfo");

app.MapPost("/api/settings/update-mode", async (HttpContext ctx) =>
{
    var body = await ctx.Request.ReadFromJsonAsync<SetUpdateModeRequest>(ctx.RequestAborted).ConfigureAwait(false);
    var mode = (body?.Mode ?? "").Trim();
    if (mode.Length == 0)
        return Results.BadRequest(new { detail = "حالت به‌روزرسانی الزامی است (Webhook یا GetUpdates)." });
    if (!string.Equals(mode, "Webhook", StringComparison.OrdinalIgnoreCase) && !string.Equals(mode, "GetUpdates", StringComparison.OrdinalIgnoreCase))
        return Results.BadRequest(new { detail = "حالت باید Webhook یا GetUpdates باشد." });
    mode = string.Equals(mode, "GetUpdates", StringComparison.OrdinalIgnoreCase) ? "GetUpdates" : "Webhook";

    var modeFilePath = Path.Combine(AppContext.BaseDirectory, "appsettings.Mode.json");
    var json = System.Text.Json.JsonSerializer.Serialize(new { Telegram = new { UpdateMode = mode } }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    try { await System.IO.File.WriteAllTextAsync(modeFilePath, json, ctx.RequestAborted).ConfigureAwait(false); }
    catch (Exception ex) { return Results.Json(new { detail = "خطا در ذخیره فایل: " + ex.Message }, statusCode: 500); }

    try
    {
        using var scope = ctx.RequestServices.CreateScope();
        var settingsRepo = scope.ServiceProvider.GetService<ISettingsRepository>();
        if (settingsRepo != null)
            await settingsRepo.SetValueAsync(UpdateModeSettingKey, mode, ctx.RequestAborted).ConfigureAwait(false);
    }
    catch { /* اگر DB خطا داد، فایل ذخیره شده */ }

    // وقتی به GetUpdates تغییر می‌کند، webhook را از Telegram حذف کن
    string? webhookDeleteMessage = null;
    if (string.Equals(mode, "GetUpdates", StringComparison.OrdinalIgnoreCase))
    {
        try
        {
            var token = await ReadTokenAsync(ctx.RequestServices, ctx.RequestAborted).ConfigureAwait(false);
            if (IsValidTokenFormat(token))
            {
                var client = new TelegramBotClient(token!);
                await client.DeleteWebhook(cancellationToken: ctx.RequestAborted).ConfigureAwait(false);
                webhookDeleteMessage = "Webhook از Telegram حذف شد.";
            }
        }
        catch (Exception ex)
        {
            webhookDeleteMessage = "خطا در حذف webhook: " + ex.Message;
        }
    }

    return Results.Json(new { success = true, mode, webhookDeleted = webhookDeleteMessage, message = "Update mode saved. Restart the application to apply changes." });
}).WithName("UpdateModeSet");

// سرویس وضعیت ربات
var botStatusService = app.Services.GetRequiredService<BotStatusService>();

// API: وضعیت روشن/خاموش ربات
app.MapGet("/api/bot/status", (BotStatusService status) => Results.Json(new { enabled = status.IsEnabled })).WithName("BotStatus");

app.MapPost("/api/bot/toggle", (BotStatusService status) =>
{
    var newState = status.Toggle();
    return Results.Json(new { enabled = newState, message = newState ? "Bot started" : "Bot stopped" });
}).WithName("BotToggle");

app.MapPost("/api/bot/start", (BotStatusService status) =>
{
    status.Start();
    return Results.Json(new { enabled = true, message = "Bot started" });
}).WithName("BotStart");

app.MapPost("/api/bot/stop", (BotStatusService status) =>
{
    status.Stop();
    return Results.Json(new { enabled = false, message = "Bot stopped" });
}).WithName("BotStop");

// Telegram sends POST with Update JSON body — همیشه dispatch (جواب ربات)؛ اگر RabbitMQ ست باشد، علاوه بر آن publish هم می‌کنیم تا صف هم استفاده شود
app.MapPost("/webhook", async (HttpRequest request, HttpContext ctx, UpdateDispatcher dispatcher, ProcessingContext procCtx, UpdateLogService logService, BotStatusService botStatus, CancellationToken ct) =>
{
    var update = await request.ReadFromJsonAsync<Update>(ct).ConfigureAwait(false);
    if (update == null)
        return Results.BadRequest();

    procCtx.Source = "Webhook";
    var preview = GetUpdatePreview(update);
    
    // اگر ربات خاموش است، فقط لاگ کن و جواب نده
    if (!botStatus.IsEnabled)
    {
        logService.Log(new UpdateLogEntry(
            DateTime.UtcNow, "Skipped", preview, "Bot is disabled",
            Source: procCtx.Source
        ));
        return Results.Ok();
    }
    
    var rabbitPublisher = ctx.RequestServices.GetService<IRabbitMqPublisher>();
    try
    {
        await dispatcher.DispatchAsync(update, ct).ConfigureAwait(false);
        if (rabbitPublisher != null)
        {
            await rabbitPublisher.PublishAsync(update, ct).ConfigureAwait(false);
            procCtx.RabbitMqPublished = true;
        }
        logService.Log(new UpdateLogEntry(
            DateTime.UtcNow, "Received", preview, null,
            Source: procCtx.Source,
            RedisProcessed: procCtx.RedisAccessed,
            RabbitMqPublished: procCtx.RabbitMqPublished,
            SqlProcessed: procCtx.SqlAccessed,
            ResponseSent: procCtx.ResponseSent,
            HandlerName: procCtx.HandlerName
        ));
        return Results.Ok();
    }
    catch (Exception ex)
    {
        logService.Log(new UpdateLogEntry(
            DateTime.UtcNow, "Error", preview, ex.Message,
            Source: procCtx.Source,
            RedisProcessed: procCtx.RedisAccessed,
            RabbitMqPublished: procCtx.RabbitMqPublished,
            SqlProcessed: procCtx.SqlAccessed,
            ResponseSent: procCtx.ResponseSent,
            HandlerName: procCtx.HandlerName
        ));
        return Results.Json(new { error = ex.Message }, statusCode: 500);
    }
})
.WithName("TelegramWebhook");

// ===== Seed default stages, buttons, and permissions =====
static async Task SeedDefaultDataAsync(ApplicationDbContext db)
{
    try
    {
        // Seed permissions
        var defaultPerms = new[]
        {
            ("default", "مجوز پایه", "Default", "Granted to all registered users"),
            ("access_settings", "دسترسی تنظیمات", "Settings Access", "Access to settings menu"),
            ("access_admin", "دسترسی ادمین", "Admin Access", "Full admin access"),
        };
        foreach (var (key, fa, en, desc) in defaultPerms)
        {
            if (!db.Permissions.Any(p => p.PermissionKey == key))
                db.Permissions.Add(new PermissionEntity { PermissionKey = key, NameFa = fa, NameEn = en, Description = desc });
        }
        await db.SaveChangesAsync().ConfigureAwait(false);

        // Seed stages
        var stages = new (string key, string fa, string en, bool enabled, string? perm, string? parent, int order)[]
        {
            ("welcome",
                "<b>سلام {name}!</b>\n\nبه ربات <b>AbroadQs</b> خوش آمدید.\nما در زمینه خدمات ارزی و مالی بین‌المللی فعالیت می‌کنیم.\n\nلطفاً ابتدا زبان خود را انتخاب کنید:",
                "<b>Hello {name}!</b>\n\nWelcome to <b>AbroadQs</b> bot.\nWe provide international financial and currency exchange services.\n\nPlease select your language:",
                true, null, null, 0),
            ("main_menu",
                "<b>منوی اصلی</b>\n\nاز دکمه‌های زیر یکی را انتخاب کنید:",
                "<b>Main Menu</b>\n\nPlease choose an option below:",
                true, null, null, 1),
            ("settings",
                "<b>تنظیمات</b>\n\nاز این بخش می‌توانید زبان ربات و حالت چت تمیز را مدیریت کنید.",
                "<b>Settings</b>\n\nManage bot language and clean chat mode from this section.",
                true, null, "main_menu", 2),
            ("lang_select",
                "زبان مورد نظر خود را انتخاب کنید:",
                "Select your preferred language:",
                true, null, "settings", 3),
            ("profile",
                "<b>پروفایل من</b>\n\nاطلاعات حساب و وضعیت احراز هویت شما.",
                "<b>My Profile</b>\n\nYour account information and verification status.",
                true, null, "main_menu", 4),
            ("new_request",
                "<b>ثبت درخواست</b>\n\nنوع خدمات مورد نظر خود را انتخاب کنید:",
                "<b>Submit Request</b>\n\nSelect the type of service you need:",
                true, null, "main_menu", 5),
            ("student_exchange",
                "<b>تبادل مالی دانشجویی</b>\n\nاز این بخش می‌توانید درخواست تبادل ارز دانشجویی ثبت کنید، تبادلات خود را پیگیری کنید و از نرخ‌های روز مطلع شوید.",
                "<b>Student Financial Exchange</b>\n\nSubmit student currency exchange requests, track your exchanges, and check current rates.",
                true, null, "new_request", 6),
            ("international_question",
                "<b>سوال بین الملل</b>\n\nاز این بخش می‌توانید سوالات خود در زمینه امور بین‌المللی را مطرح کنید.\nتیم کارشناسان ما پاسخگوی شما خواهد بود.",
                "<b>International Questions</b>\n\nAsk your questions about international affairs.\nOur expert team will respond to you.",
                true, null, "new_request", 7),
            ("student_project",
                "<b>پروژه دانشجویی</b>\n\nاز این بخش می‌توانید درخواست همکاری در پروژه‌های دانشجویی ثبت کنید.",
                "<b>Student Project</b>\n\nSubmit a collaboration request for student projects.",
                true, null, "new_request", 8),
            ("financial_sponsor",
                "<b>حامی مالی</b>\n\nاز این بخش می‌توانید درخواست حمایت مالی ثبت کنید یا به عنوان حامی مالی فعالیت کنید.",
                "<b>Financial Sponsor</b>\n\nSubmit a financial sponsorship request or become a financial sponsor.",
                true, null, "new_request", 9),
            ("submit_exchange",
                "<b>ثبت درخواست تبادل</b>\n\nنوع درخواست خود را انتخاب کنید:",
                "<b>Submit Exchange Request</b>\n\nSelect your request type:",
                true, null, "student_exchange", 10),
            ("buy_currency",
                "<b>خرید ارز</b>\n\nاز این بخش می‌توانید درخواست خرید ارز ثبت کنید.",
                "<b>Buy Currency</b>\n\nSubmit a currency purchase request.",
                true, null, "submit_exchange", 15),
            ("sell_currency",
                "<b>فروش ارز</b>\n\nاز این بخش می‌توانید درخواست فروش ارز ثبت کنید.",
                "<b>Sell Currency</b>\n\nSubmit a currency sell request.",
                true, null, "submit_exchange", 16),
            ("do_exchange",
                "<b>تبادل</b>\n\nاز این بخش می‌توانید درخواست تبادل ارز ثبت کنید.",
                "<b>Exchange</b>\n\nSubmit a currency exchange request.",
                true, null, "submit_exchange", 17),
            ("my_exchanges",
                "<b>تبادلات من</b>\n\nاز این بخش می‌توانید لیست تبادلات قبلی خود را مشاهده و پیگیری کنید.",
                "<b>My Exchanges</b>\n\nView and track your previous exchange transactions.",
                true, null, "student_exchange", 11),
            ("exchange_groups",
                "<b>گروه‌های تبادل</b>\n\nاز این بخش می‌توانید به گروه‌های تبادل ارز دانشجویی دسترسی پیدا کنید.",
                "<b>Exchange Groups</b>\n\nAccess student currency exchange groups.",
                true, null, "student_exchange", 12),
            ("exchange_rates",
                "<b>نرخ ارزها</b>\n\nاز این بخش می‌توانید نرخ‌های به‌روز ارزهای مختلف را مشاهده کنید.",
                "<b>Exchange Rates</b>\n\nView up-to-date exchange rates for various currencies.",
                true, null, "student_exchange", 13),
            ("exchange_guide",
                "<b>شرایط و راهنما</b>\n\nاز بخش‌های زیر برای آشنایی با قوانین و راهنمای خدمات استفاده کنید.",
                "<b>Terms & Guide</b>\n\nUse the sections below to learn about our services and guidelines.",
                true, null, "student_exchange", 14),
            ("guide_exchange",
                "<b>📖 راهنمای تبادل ارز</b>\n━━━━━━━━━━━━━━━━━━━\n\n" +
                "۱. ابتدا احراز هویت خود را تکمیل کنید.\n" +
                "۲. نوع درخواست (خرید/فروش/تبادل) را انتخاب کنید.\n" +
                "۳. ارز، مبلغ و نرخ پیشنهادی خود را وارد کنید.\n" +
                "۴. پس از تأیید ادمین، آگهی شما در کانال منتشر می‌شود.\n" +
                "۵. سایر کاربران پیشنهاد قیمت می‌دهند.\n" +
                "۶. پس از توافق، تبادل انجام و تراکنش ثبت می‌شود.\n\n" +
                "⚠️ حداقل مبلغ تبادل: ۱۰۰ واحد ارز\n" +
                "⚠️ کارمزد: بر اساس تنظیمات سیستم (معمولاً ۱-۳٪)",
                "<b>📖 Exchange Guide</b>\n━━━━━━━━━━━━━━━━━━━\n\n" +
                "1. Complete your identity verification first.\n" +
                "2. Select request type (buy/sell/exchange).\n" +
                "3. Enter currency, amount and your proposed rate.\n" +
                "4. After admin approval, your ad is posted to the channel.\n" +
                "5. Other users submit price proposals.\n" +
                "6. After agreement, exchange is completed and recorded.\n\n" +
                "⚠️ Minimum exchange: 100 currency units\n" +
                "⚠️ Fee: Based on system settings (usually 1-3%)",
                true, null, "exchange_guide", 14),
            ("guide_groups",
                "<b>👥 راهنمای گروه‌ها</b>\n━━━━━━━━━━━━━━━━━━━\n\n" +
                "گروه‌های تبادل برای ارتباط مستقیم بین کاربران ایجاد شده‌اند.\n\n" +
                "• هر کاربر می‌تواند گروه خود را ثبت کند.\n" +
                "• گروه‌ها پس از تأیید ادمین نمایش داده می‌شوند.\n" +
                "• لینک عضویت به صورت خودکار نمایش داده می‌شود.",
                "<b>👥 Groups Guide</b>\n━━━━━━━━━━━━━━━━━━━\n\n" +
                "Exchange groups are created for direct user-to-user communication.\n\n" +
                "• Any user can register their group.\n" +
                "• Groups are shown after admin approval.\n" +
                "• Join links are displayed automatically.",
                true, null, "exchange_guide", 15),
            ("guide_wallet",
                "<b>💰 راهنمای کیف پول</b>\n━━━━━━━━━━━━━━━━━━━\n\n" +
                "• کیف پول شما برای پرداخت کارمزد و هزینه آگهی استفاده می‌شود.\n" +
                "• شارژ از طریق درگاه پرداخت آنلاین (بیت‌پی) انجام می‌شود.\n" +
                "• انتقال وجه بین کاربران با نام کاربری امکان‌پذیر است.\n" +
                "• تاریخچه تراکنش‌ها قابل مشاهده است.",
                "<b>💰 Wallet Guide</b>\n━━━━━━━━━━━━━━━━━━━\n\n" +
                "• Your wallet is used for paying fees and ad costs.\n" +
                "• Top up via online payment gateway (BitPay).\n" +
                "• User-to-user transfers by username are supported.\n" +
                "• Full transaction history is available.",
                true, null, "exchange_guide", 16),
            ("guide_projects",
                "<b>📁 راهنمای پروژه‌ها</b>\n━━━━━━━━━━━━━━━━━━━\n\n" +
                "• پروژه دانشجویی خود را ثبت و منتشر کنید.\n" +
                "• سایر کاربران می‌توانند پیشنهاد همکاری ارسال کنند.\n" +
                "• پس از پذیرش پیشنهاد، پروژه وارد مرحله اجرا می‌شود.\n" +
                "• پس از تکمیل، هر دو طرف تأیید می‌کنند.",
                "<b>📁 Projects Guide</b>\n━━━━━━━━━━━━━━━━━━━\n\n" +
                "• Post and publish your student project.\n" +
                "• Other users can submit collaboration proposals.\n" +
                "• After accepting a proposal, the project enters execution phase.\n" +
                "• Both parties confirm upon completion.",
                true, null, "exchange_guide", 17),
            ("guide_faq",
                "<b>❓ سوالات متداول</b>\n━━━━━━━━━━━━━━━━━━━\n\n" +
                "<b>س: آیا احراز هویت اجباری است؟</b>\nج: بله، برای استفاده از خدمات تبادل ارز باید احراز هویت کنید.\n\n" +
                "<b>س: کارمزد چقدر است؟</b>\nج: کارمزد بر اساس نوع تراکنش و مبلغ متفاوت است.\n\n" +
                "<b>س: آیا می‌توانم درخواست خود را لغو کنم؟</b>\nج: تا زمانی که درخواست مچ نشده باشد، امکان لغو وجود دارد.\n\n" +
                "<b>س: چگونه با پشتیبانی ارتباط برقرار کنم؟</b>\nج: از بخش تیکت‌ها استفاده کنید.",
                "<b>❓ FAQ</b>\n━━━━━━━━━━━━━━━━━━━\n\n" +
                "<b>Q: Is identity verification mandatory?</b>\nA: Yes, verification is required for currency exchange services.\n\n" +
                "<b>Q: What are the fees?</b>\nA: Fees vary by transaction type and amount.\n\n" +
                "<b>Q: Can I cancel my request?</b>\nA: You can cancel as long as the request hasn't been matched.\n\n" +
                "<b>Q: How do I contact support?</b>\nA: Use the Tickets section.",
                true, null, "exchange_guide", 18),
            ("finance",
                "<b>امور مالی</b>\n\nاز این بخش می‌توانید وضعیت تراکنش‌ها و حساب مالی خود را مشاهده کنید.",
                "<b>Finance</b>\n\nView your transactions and financial account status from this section.",
                true, null, "main_menu", 20),
            ("my_suggestions",
                "<b>پیشنهادات من</b>\n\nاز این بخش می‌توانید پیشنهادات و انتقادات خود را ثبت کنید.\nنظرات شما به بهبود خدمات ما کمک می‌کند.",
                "<b>My Suggestions</b>\n\nSubmit your suggestions and feedback.\nYour opinions help us improve our services.",
                true, null, "main_menu", 21),
            ("my_messages",
                "<b>پیام‌های من</b>\n\nاز این بخش می‌توانید پیام‌های دریافتی از تیم پشتیبانی و اطلاع‌رسانی‌ها را مشاهده کنید.",
                "<b>My Messages</b>\n\nView messages from support team and notifications from this section.",
                true, null, "main_menu", 22),
            ("about_us",
                "<b>درباره ما</b>\n\n<b>AbroadQs</b> ارائه‌دهنده خدمات ارزی و مالی بین‌المللی است.\nما با هدف تسهیل تراکنش‌های بین‌المللی فعالیت می‌کنیم.\n\nبرای ارتباط با ما می‌توانید از بخش تیکت‌ها استفاده کنید.",
                "<b>About Us</b>\n\n<b>AbroadQs</b> provides international financial and currency exchange services.\nWe aim to facilitate international transactions.\n\nContact us through the Tickets section.",
                true, null, "main_menu", 23),
            ("tickets",
                "<b>تیکت‌ها</b>\n\nاز این بخش می‌توانید تیکت پشتیبانی جدید ایجاد کنید یا وضعیت تیکت‌های قبلی خود را پیگیری کنید.",
                "<b>Tickets</b>\n\nCreate a new support ticket or track your existing tickets from this section.",
                true, null, "main_menu", 24),
        };

        foreach (var (key, fa, en, enabled, perm, parent, order) in stages)
        {
            if (!db.BotStages.Any(s => s.StageKey == key))
                db.BotStages.Add(new BotStageEntity { StageKey = key, TextFa = fa, TextEn = en, IsEnabled = enabled, RequiredPermission = perm, ParentStageKey = parent, SortOrder = order });
        }
        await db.SaveChangesAsync().ConfigureAwait(false);

        // Seed buttons (only if stage exists and has no buttons yet)
        var welcomeStage = db.BotStages.FirstOrDefault(s => s.StageKey == "welcome");
        if (welcomeStage != null && !db.BotStageButtons.Any(b => b.StageId == welcomeStage.Id))
        {
            db.BotStageButtons.AddRange(
                new BotStageButtonEntity { StageId = welcomeStage.Id, TextFa = "فارسی 🇮🇷", TextEn = "فارسی 🇮🇷", ButtonType = "callback", CallbackData = "lang:fa", Row = 0, Column = 0, IsEnabled = true },
                new BotStageButtonEntity { StageId = welcomeStage.Id, TextFa = "English 🇬🇧", TextEn = "English 🇬🇧", ButtonType = "callback", CallbackData = "lang:en", Row = 0, Column = 1, IsEnabled = true }
            );
        }

        var mainMenuStage = db.BotStages.FirstOrDefault(s => s.StageKey == "main_menu");
        if (mainMenuStage != null)
        {
            // Always reset main_menu buttons to ensure correct layout
            var oldMainButtons = db.BotStageButtons.Where(b => b.StageId == mainMenuStage.Id).ToList();
            if (oldMainButtons.Count > 0)
                db.BotStageButtons.RemoveRange(oldMainButtons);

            db.BotStageButtons.AddRange(
                // Row 0: ثبت درخواست (full width)
                new BotStageButtonEntity { StageId = mainMenuStage.Id, TextFa = "ثبت درخواست", TextEn = "Submit Request", ButtonType = "callback", CallbackData = "stage:new_request", Row = 0, Column = 0, IsEnabled = true },
                // Row 1: امور مالی | پیشنهادات من | پیام های من
                new BotStageButtonEntity { StageId = mainMenuStage.Id, TextFa = "امور مالی", TextEn = "Finance", ButtonType = "callback", CallbackData = "stage:finance", Row = 1, Column = 0, IsEnabled = true },
                new BotStageButtonEntity { StageId = mainMenuStage.Id, TextFa = "پیشنهادات من", TextEn = "My Suggestions", ButtonType = "callback", CallbackData = "stage:my_suggestions", Row = 1, Column = 1, IsEnabled = true },
                new BotStageButtonEntity { StageId = mainMenuStage.Id, TextFa = "پیام های من", TextEn = "My Messages", ButtonType = "callback", CallbackData = "stage:my_messages", Row = 1, Column = 2, IsEnabled = true },
                // Row 2: پروفایل من | درباره ما | تیکت ها
                new BotStageButtonEntity { StageId = mainMenuStage.Id, TextFa = "پروفایل من", TextEn = "My Profile", ButtonType = "callback", CallbackData = "stage:profile", Row = 2, Column = 0, IsEnabled = true },
                new BotStageButtonEntity { StageId = mainMenuStage.Id, TextFa = "درباره ما", TextEn = "About Us", ButtonType = "callback", CallbackData = "stage:about_us", Row = 2, Column = 1, IsEnabled = true },
                new BotStageButtonEntity { StageId = mainMenuStage.Id, TextFa = "تیکت ها", TextEn = "Tickets", ButtonType = "callback", CallbackData = "stage:tickets", Row = 2, Column = 2, IsEnabled = true },
                // Row 3: تنظیمات (full width)
                new BotStageButtonEntity { StageId = mainMenuStage.Id, TextFa = "تنظیمات", TextEn = "Settings", ButtonType = "callback", TargetStageKey = "settings", Row = 3, Column = 0, IsEnabled = true }
            );
        }

        var settingsStage = db.BotStages.FirstOrDefault(s => s.StageKey == "settings");
        if (settingsStage != null)
        {
            // Reset settings buttons
            var oldSettingsButtons = db.BotStageButtons.Where(b => b.StageId == settingsStage.Id).ToList();
            if (oldSettingsButtons.Count > 0)
                db.BotStageButtons.RemoveRange(oldSettingsButtons);
            db.BotStageButtons.AddRange(
                // Row 0: زبان | حالت چت تمیز (side by side)
                new BotStageButtonEntity { StageId = settingsStage.Id, TextFa = "زبان", TextEn = "Language", ButtonType = "callback", TargetStageKey = "lang_select", Row = 0, Column = 0, IsEnabled = true },
                new BotStageButtonEntity { StageId = settingsStage.Id, TextFa = "حالت چت تمیز", TextEn = "Clean Chat Mode", ButtonType = "callback", CallbackData = "toggle:clean_chat", Row = 0, Column = 1, IsEnabled = true }
                // Row 1: بازگشت (auto back-button added by ShowStageInlineAsync)
            );
        }

        // new_request sub-menu (reply keyboard)
        var newRequestStage = db.BotStages.FirstOrDefault(s => s.StageKey == "new_request");
        if (newRequestStage != null)
        {
            var oldNewReqButtons = db.BotStageButtons.Where(b => b.StageId == newRequestStage.Id).ToList();
            if (oldNewReqButtons.Count > 0)
                db.BotStageButtons.RemoveRange(oldNewReqButtons);
            db.BotStageButtons.AddRange(
                // Row 0: تبادل مالی دانشجویی (full width)
                new BotStageButtonEntity { StageId = newRequestStage.Id, TextFa = "تبادل مالی دانشجویی", TextEn = "Student Exchange", ButtonType = "callback", CallbackData = "stage:student_exchange", Row = 0, Column = 0, IsEnabled = true },
                // Row 1: سوال بین الملل | پروژه دانشجویی | حامی مالی
                new BotStageButtonEntity { StageId = newRequestStage.Id, TextFa = "سوال بین الملل", TextEn = "Intl Questions", ButtonType = "callback", CallbackData = "stage:international_question", Row = 1, Column = 0, IsEnabled = true },
                new BotStageButtonEntity { StageId = newRequestStage.Id, TextFa = "پروژه دانشجویی", TextEn = "Student Project", ButtonType = "callback", CallbackData = "stage:student_project", Row = 1, Column = 1, IsEnabled = true },
                new BotStageButtonEntity { StageId = newRequestStage.Id, TextFa = "حامی مالی", TextEn = "Financial Sponsor", ButtonType = "callback", CallbackData = "stage:financial_sponsor", Row = 1, Column = 2, IsEnabled = true },
                // Row 2: بازگشت
                new BotStageButtonEntity { StageId = newRequestStage.Id, TextFa = "بازگشت", TextEn = "Back", ButtonType = "callback", CallbackData = "stage:main_menu", Row = 2, Column = 0, IsEnabled = true }
            );
        }

        // student_exchange sub-menu (reply keyboard)
        var studentExchangeStage = db.BotStages.FirstOrDefault(s => s.StageKey == "student_exchange");
        if (studentExchangeStage != null)
        {
            var oldStudentButtons = db.BotStageButtons.Where(b => b.StageId == studentExchangeStage.Id).ToList();
            if (oldStudentButtons.Count > 0)
                db.BotStageButtons.RemoveRange(oldStudentButtons);
            db.BotStageButtons.AddRange(
                // Row 0: ثبت درخواست تبادل (full width)
                new BotStageButtonEntity { StageId = studentExchangeStage.Id, TextFa = "ثبت درخواست تبادل", TextEn = "Submit Exchange", ButtonType = "callback", CallbackData = "stage:submit_exchange", Row = 0, Column = 0, IsEnabled = true },
                // Row 1: تبادلات من | گروه های تبادل | نرخ ارز ها
                new BotStageButtonEntity { StageId = studentExchangeStage.Id, TextFa = "تبادلات من", TextEn = "My Exchanges", ButtonType = "callback", CallbackData = "stage:my_exchanges", Row = 1, Column = 0, IsEnabled = true },
                new BotStageButtonEntity { StageId = studentExchangeStage.Id, TextFa = "گروه های تبادل", TextEn = "Exchange Groups", ButtonType = "callback", CallbackData = "stage:exchange_groups", Row = 1, Column = 1, IsEnabled = true },
                new BotStageButtonEntity { StageId = studentExchangeStage.Id, TextFa = "نرخ ارز ها", TextEn = "Exchange Rates", ButtonType = "callback", CallbackData = "stage:exchange_rates", Row = 1, Column = 2, IsEnabled = true },
                // Row 2: شرایط و راهنما (full width)
                new BotStageButtonEntity { StageId = studentExchangeStage.Id, TextFa = "شرایط و راهنما", TextEn = "Terms & Guide", ButtonType = "callback", CallbackData = "stage:exchange_guide", Row = 2, Column = 0, IsEnabled = true },
                // Row 3: بازگشت
                new BotStageButtonEntity { StageId = studentExchangeStage.Id, TextFa = "بازگشت", TextEn = "Back", ButtonType = "callback", CallbackData = "stage:new_request", Row = 3, Column = 0, IsEnabled = true }
            );
        }

        // submit_exchange sub-menu (reply keyboard)
        var submitExchangeStage = db.BotStages.FirstOrDefault(s => s.StageKey == "submit_exchange");
        if (submitExchangeStage != null)
        {
            var oldSubmitExButtons = db.BotStageButtons.Where(b => b.StageId == submitExchangeStage.Id).ToList();
            if (oldSubmitExButtons.Count > 0)
                db.BotStageButtons.RemoveRange(oldSubmitExButtons);
            db.BotStageButtons.AddRange(
                // Row 0: خرید ارز | فروش ارز
                new BotStageButtonEntity { StageId = submitExchangeStage.Id, TextFa = "خرید ارز", TextEn = "Buy Currency", ButtonType = "callback", CallbackData = "stage:buy_currency", Row = 0, Column = 0, IsEnabled = true },
                new BotStageButtonEntity { StageId = submitExchangeStage.Id, TextFa = "فروش ارز", TextEn = "Sell Currency", ButtonType = "callback", CallbackData = "stage:sell_currency", Row = 0, Column = 1, IsEnabled = true },
                // Row 1: تبادل
                new BotStageButtonEntity { StageId = submitExchangeStage.Id, TextFa = "تبادل", TextEn = "Exchange", ButtonType = "callback", CallbackData = "stage:do_exchange", Row = 1, Column = 0, IsEnabled = true },
                // Row 2: بازگشت
                new BotStageButtonEntity { StageId = submitExchangeStage.Id, TextFa = "بازگشت", TextEn = "Back", ButtonType = "callback", CallbackData = "stage:student_exchange", Row = 2, Column = 0, IsEnabled = true }
            );
        }

        // exchange_guide sub-menu (Phase 1.5: Terms & Guide sub-stages)
        var guideStage = db.BotStages.FirstOrDefault(s => s.StageKey == "exchange_guide");
        if (guideStage != null)
        {
            var oldGuideButtons = db.BotStageButtons.Where(b => b.StageId == guideStage.Id).ToList();
            if (oldGuideButtons.Count > 0)
                db.BotStageButtons.RemoveRange(oldGuideButtons);
            db.BotStageButtons.AddRange(
                new BotStageButtonEntity { StageId = guideStage.Id, TextFa = "📖 راهنمای تبادل", TextEn = "📖 Exchange Guide", ButtonType = "callback", TargetStageKey = "guide_exchange", Row = 0, Column = 0, IsEnabled = true },
                new BotStageButtonEntity { StageId = guideStage.Id, TextFa = "👥 راهنمای گروه‌ها", TextEn = "👥 Groups Guide", ButtonType = "callback", TargetStageKey = "guide_groups", Row = 0, Column = 1, IsEnabled = true },
                new BotStageButtonEntity { StageId = guideStage.Id, TextFa = "💰 راهنمای کیف پول", TextEn = "💰 Wallet Guide", ButtonType = "callback", TargetStageKey = "guide_wallet", Row = 1, Column = 0, IsEnabled = true },
                new BotStageButtonEntity { StageId = guideStage.Id, TextFa = "📁 راهنمای پروژه‌ها", TextEn = "📁 Projects Guide", ButtonType = "callback", TargetStageKey = "guide_projects", Row = 1, Column = 1, IsEnabled = true },
                new BotStageButtonEntity { StageId = guideStage.Id, TextFa = "❓ سوالات متداول", TextEn = "❓ FAQ", ButtonType = "callback", TargetStageKey = "guide_faq", Row = 2, Column = 0, IsEnabled = true }
            );
        }

        var langSelectStage = db.BotStages.FirstOrDefault(s => s.StageKey == "lang_select");
        if (langSelectStage != null && !db.BotStageButtons.Any(b => b.StageId == langSelectStage.Id))
        {
            db.BotStageButtons.AddRange(
                new BotStageButtonEntity { StageId = langSelectStage.Id, TextFa = "فارسی", TextEn = "فارسی", ButtonType = "callback", CallbackData = "lang:fa", Row = 0, Column = 0, IsEnabled = true },
                new BotStageButtonEntity { StageId = langSelectStage.Id, TextFa = "English", TextEn = "English", ButtonType = "callback", CallbackData = "lang:en", Row = 0, Column = 1, IsEnabled = true }
            );
        }

        // ── Finance reply-kb stage buttons ────
        var financeStage = db.BotStages.FirstOrDefault(s => s.StageKey == "finance");
        if (financeStage != null)
        {
            var oldFinBtns = db.BotStageButtons.Where(b => b.StageId == financeStage.Id).ToList();
            if (oldFinBtns.Count > 0) db.BotStageButtons.RemoveRange(oldFinBtns);
            db.BotStageButtons.AddRange(
                new BotStageButtonEntity { StageId = financeStage.Id, TextFa = "💰 موجودی", TextEn = "💰 Balance", ButtonType = "callback", TargetStageKey = "fin_balance", Row = 0, Column = 0, IsEnabled = true },
                new BotStageButtonEntity { StageId = financeStage.Id, TextFa = "💳 شارژ حساب", TextEn = "💳 Charge", ButtonType = "callback", TargetStageKey = "fin_charge", Row = 0, Column = 1, IsEnabled = true },
                new BotStageButtonEntity { StageId = financeStage.Id, TextFa = "📤 انتقال وجه", TextEn = "📤 Transfer", ButtonType = "callback", TargetStageKey = "fin_transfer", Row = 1, Column = 0, IsEnabled = true },
                new BotStageButtonEntity { StageId = financeStage.Id, TextFa = "📊 تاریخچه", TextEn = "📊 History", ButtonType = "callback", TargetStageKey = "fin_history", Row = 1, Column = 1, IsEnabled = true },
                new BotStageButtonEntity { StageId = financeStage.Id, TextFa = "🔙 بازگشت", TextEn = "🔙 Back", ButtonType = "callback", CallbackData = "stage:main_menu", Row = 2, Column = 0, IsEnabled = true }
            );
        }

        // ── Tickets reply-kb stage buttons ────
        var ticketsStage = db.BotStages.FirstOrDefault(s => s.StageKey == "tickets");
        if (ticketsStage != null)
        {
            var oldTktBtns = db.BotStageButtons.Where(b => b.StageId == ticketsStage.Id).ToList();
            if (oldTktBtns.Count > 0) db.BotStageButtons.RemoveRange(oldTktBtns);
            db.BotStageButtons.AddRange(
                new BotStageButtonEntity { StageId = ticketsStage.Id, TextFa = "📝 تیکت جدید", TextEn = "📝 New Ticket", ButtonType = "callback", TargetStageKey = "tkt_new", Row = 0, Column = 0, IsEnabled = true },
                new BotStageButtonEntity { StageId = ticketsStage.Id, TextFa = "📋 تیکت‌های من", TextEn = "📋 My Tickets", ButtonType = "callback", TargetStageKey = "tkt_list", Row = 0, Column = 1, IsEnabled = true },
                new BotStageButtonEntity { StageId = ticketsStage.Id, TextFa = "🔙 بازگشت", TextEn = "🔙 Back", ButtonType = "callback", CallbackData = "stage:main_menu", Row = 1, Column = 0, IsEnabled = true }
            );
        }

        // ── Student Project reply-kb stage buttons ────
        var projStage = db.BotStages.FirstOrDefault(s => s.StageKey == "student_project");
        if (projStage != null)
        {
            var oldProjBtns = db.BotStageButtons.Where(b => b.StageId == projStage.Id).ToList();
            if (oldProjBtns.Count > 0) db.BotStageButtons.RemoveRange(oldProjBtns);
            db.BotStageButtons.AddRange(
                new BotStageButtonEntity { StageId = projStage.Id, TextFa = "📝 ثبت پروژه", TextEn = "📝 Post Project", ButtonType = "callback", TargetStageKey = "proj_post", Row = 0, Column = 0, IsEnabled = true },
                new BotStageButtonEntity { StageId = projStage.Id, TextFa = "🔍 جستجوی پروژه", TextEn = "🔍 Browse Projects", ButtonType = "callback", TargetStageKey = "proj_browse", Row = 0, Column = 1, IsEnabled = true },
                new BotStageButtonEntity { StageId = projStage.Id, TextFa = "📁 پروژه‌های من", TextEn = "📁 My Projects", ButtonType = "callback", TargetStageKey = "proj_my", Row = 1, Column = 0, IsEnabled = true },
                new BotStageButtonEntity { StageId = projStage.Id, TextFa = "📋 پیشنهادات من", TextEn = "📋 My Proposals", ButtonType = "callback", TargetStageKey = "proj_my_proposals", Row = 1, Column = 1, IsEnabled = true },
                new BotStageButtonEntity { StageId = projStage.Id, TextFa = "🔙 بازگشت", TextEn = "🔙 Back", ButtonType = "callback", CallbackData = "stage:new_request", Row = 2, Column = 0, IsEnabled = true }
            );
        }

        // ── International Question reply-kb stage buttons ────
        var iqStage = db.BotStages.FirstOrDefault(s => s.StageKey == "international_question");
        if (iqStage != null)
        {
            var oldIqBtns = db.BotStageButtons.Where(b => b.StageId == iqStage.Id).ToList();
            if (oldIqBtns.Count > 0) db.BotStageButtons.RemoveRange(oldIqBtns);
            db.BotStageButtons.AddRange(
                new BotStageButtonEntity { StageId = iqStage.Id, TextFa = "❓ ثبت سوال", TextEn = "❓ Post Question", ButtonType = "callback", TargetStageKey = "iq_post", Row = 0, Column = 0, IsEnabled = true },
                new BotStageButtonEntity { StageId = iqStage.Id, TextFa = "🌍 مرور سوالات", TextEn = "🌍 Browse Questions", ButtonType = "callback", TargetStageKey = "iq_browse", Row = 0, Column = 1, IsEnabled = true },
                new BotStageButtonEntity { StageId = iqStage.Id, TextFa = "📝 سوالات من", TextEn = "📝 My Questions", ButtonType = "callback", TargetStageKey = "iq_my", Row = 1, Column = 0, IsEnabled = true },
                new BotStageButtonEntity { StageId = iqStage.Id, TextFa = "💬 پاسخ‌های من", TextEn = "💬 My Answers", ButtonType = "callback", TargetStageKey = "iq_my_answers", Row = 1, Column = 1, IsEnabled = true },
                new BotStageButtonEntity { StageId = iqStage.Id, TextFa = "🔙 بازگشت", TextEn = "🔙 Back", ButtonType = "callback", CallbackData = "stage:new_request", Row = 2, Column = 0, IsEnabled = true }
            );
        }

        // ── Financial Sponsor reply-kb stage buttons ────
        var spStage = db.BotStages.FirstOrDefault(s => s.StageKey == "financial_sponsor");
        if (spStage != null)
        {
            var oldSpBtns = db.BotStageButtons.Where(b => b.StageId == spStage.Id).ToList();
            if (oldSpBtns.Count > 0) db.BotStageButtons.RemoveRange(oldSpBtns);
            db.BotStageButtons.AddRange(
                new BotStageButtonEntity { StageId = spStage.Id, TextFa = "📝 درخواست حمایت", TextEn = "📝 Request Sponsorship", ButtonType = "callback", TargetStageKey = "sp_request", Row = 0, Column = 0, IsEnabled = true },
                new BotStageButtonEntity { StageId = spStage.Id, TextFa = "💰 حمایت از پروژه", TextEn = "💰 Fund a Project", ButtonType = "callback", TargetStageKey = "sp_browse", Row = 0, Column = 1, IsEnabled = true },
                new BotStageButtonEntity { StageId = spStage.Id, TextFa = "📊 حمایت‌های من", TextEn = "📊 My Sponsorships", ButtonType = "callback", TargetStageKey = "sp_my", Row = 1, Column = 0, IsEnabled = true },
                new BotStageButtonEntity { StageId = spStage.Id, TextFa = "🔙 بازگشت", TextEn = "🔙 Back", ButtonType = "callback", CallbackData = "stage:new_request", Row = 2, Column = 0, IsEnabled = true }
            );
        }

        await db.SaveChangesAsync().ConfigureAwait(false);

        // Seed critical settings if missing
        var settingsToSeed = new (string key, string value)[]
        {
            ("bitpay_api_key", "adxcv-zzadq-polkjsad-oaboremn"), // BitPay test API key — replace with production key
            ("bitpay_test_mode", "true"),                          // Enable test mode for BitPay
            ("webhook_url", Environment.GetEnvironmentVariable("PUBLIC_WEBHOOK_URL") ?? "https://webhook.abroadqs.com/webhook"),
        };
        foreach (var (key, value) in settingsToSeed)
        {
            if (!db.Settings.Any(s => s.Key == key))
            {
                db.Settings.Add(new SettingEntity { Key = key, Value = value });
            }
        }
        await db.SaveChangesAsync().ConfigureAwait(false);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Seed failed (non-fatal): {ex.Message}");
    }
}

static async Task<string> ReadTokenAsync(IServiceProvider services, CancellationToken ct)
{
    try
    {
        using var scope = services.CreateScope();
        var settingsRepo = scope.ServiceProvider.GetService<ISettingsRepository>();
        if (settingsRepo != null)
        {
            var fromDb = await settingsRepo.GetValueAsync(TokenSettingKey, ct).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(fromDb)) return fromDb;
        }
    }
    catch { /* fallback to file */ }
    return ReadTokenFromFile(services);
}

static async Task<string> ReadUpdateModeAsync(IServiceProvider services, CancellationToken ct)
{
    try
    {
        using var scope = services.CreateScope();
        var settingsRepo = scope.ServiceProvider.GetService<ISettingsRepository>();
        if (settingsRepo != null)
        {
            var fromDb = await settingsRepo.GetValueAsync(UpdateModeSettingKey, ct).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(fromDb)) return fromDb.Trim();
        }
    }
    catch { /* fallback to config */ }
    var config = services.GetRequiredService<IConfiguration>();
    var fromConfig = config["Telegram:UpdateMode"]?.Trim();
    return string.Equals(fromConfig, "GetUpdates", StringComparison.OrdinalIgnoreCase) ? "GetUpdates" : (fromConfig ?? "Webhook");
}

/// <summary>بارگذاری توکن و حالت از دیتابیس هنگام استارت — اولویت با دیتابیس تا بعد از ذخیره در داشبورد با ریستارت بیاید.</summary>
static (string? token, string? mode) LoadSettingsFromDatabaseSync(string connectionString)
{
    try
    {
        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
        optionsBuilder.UseSqlServer(connectionString);
        using var db = new ApplicationDbContext(optionsBuilder.Options);
        var settings = db.Settings.AsNoTracking()
            .Where(x => x.Key == "Telegram.BotToken" || x.Key == "Telegram.UpdateMode")
            .Select(x => new { x.Key, x.Value })
            .ToList();
        var token = settings.FirstOrDefault(x => x.Key == "Telegram.BotToken")?.Value?.Trim();
        var mode = settings.FirstOrDefault(x => x.Key == "Telegram.UpdateMode")?.Value?.Trim();
        return (token, mode);
    }
    catch
    {
        return (null, null);
    }
}

/// <summary>بارگذاری توکن از فایل هنگام استارت (همان مسیر داشبورد).</summary>
static string? LoadTokenAtStartup()
{
    try
    {
        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.Token.json");
        if (!System.IO.File.Exists(path)) return null;
        var json = System.IO.File.ReadAllText(path);
        var doc = System.Text.Json.JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("Telegram", out var telegram) && telegram.TryGetProperty("BotToken", out var botToken))
            return botToken.GetString()?.Trim();
    }
    catch { }
    return null;
}

static bool IsValidTokenFormat(string? token)
{
    if (string.IsNullOrWhiteSpace(token) || token == "0:placeholder" || token == "<YOUR_BOT_TOKEN>")
        return false;
    var i = token.IndexOf(':');
    return i > 0 && i < token.Length - 1 && long.TryParse(token.AsSpan(0, i), out _);
}

/// <summary>مسیر ثابت فایل توکن: پوشهٔ اجرای اپ (همان جایی که ذخیره می‌کنیم).</summary>
static string GetTokenFilePath(IServiceProvider services)
{
    return Path.Combine(AppContext.BaseDirectory, "appsettings.Token.json");
}

static string ReadTokenFromFile(IServiceProvider services)
{
    try
    {
        var path = GetTokenFilePath(services);
        if (!System.IO.File.Exists(path))
        {
            var env = services.GetRequiredService<IWebHostEnvironment>();
            path = Path.Combine(env.ContentRootPath, "appsettings.Token.json");
            if (!System.IO.File.Exists(path)) return "";
        }
        var json = System.IO.File.ReadAllText(path);
        var doc = System.Text.Json.JsonDocument.Parse(json);
        var token = doc.RootElement.TryGetProperty("Telegram", out var telegram) && telegram.TryGetProperty("BotToken", out var botToken)
            ? botToken.GetString() ?? ""
            : "";
        return token ?? "";
    }
    catch { return services.GetRequiredService<IConfiguration>()["Telegram:BotToken"] ?? ""; }
}

static string GetUpdatePreview(Update u)
{
    var id = u.Id;
    if (u.Message != null) return $"UpdateId: {id}, Type: Message";
    if (u.CallbackQuery != null) return $"UpdateId: {id}, Type: CallbackQuery";
    if (u.InlineQuery != null) return $"UpdateId: {id}, Type: InlineQuery";
    if (u.ChosenInlineResult != null) return $"UpdateId: {id}, Type: ChosenInlineResult";
    return $"UpdateId: {id}, Type: Unknown";
}

// ═══════════════════════════════════════════════════════════════════
//  Phase 2-8: REST API Endpoints for admin dashboard / integrations
// ═══════════════════════════════════════════════════════════════════

// ── Wallet API ───────────────────────────────────────────────────
app.MapGet("/api/wallet/{userId:long}/transactions", async (long userId, int page, HttpContext ctx, CancellationToken ct) =>
{
    using var scope = app.Services.CreateScope();
    var walletRepo = scope.ServiceProvider.GetService<IWalletRepository>();
    if (walletRepo == null) return Results.Json(new { detail = "Wallet not configured." }, statusCode: 500);
    var txns = await walletRepo.GetTransactionsAsync(userId, page, 20, ct).ConfigureAwait(false);
    var balance = await walletRepo.GetBalanceAsync(userId, ct).ConfigureAwait(false);
    return Results.Json(new { balance, transactions = txns });
}).WithName("WalletTransactions");

app.MapGet("/api/payments/{userId:long}", async (long userId, int page, HttpContext ctx, CancellationToken ct) =>
{
    using var scope = app.Services.CreateScope();
    var walletRepo = scope.ServiceProvider.GetService<IWalletRepository>();
    if (walletRepo == null) return Results.Json(new { detail = "Wallet not configured." }, statusCode: 500);
    var payments = await walletRepo.GetPaymentsAsync(userId, page, 20, ct).ConfigureAwait(false);
    return Results.Json(new { payments });
}).WithName("UserPayments");

// ── Tickets API ──────────────────────────────────────────────────
app.MapGet("/api/tickets", async (string? status, int page, HttpContext ctx, CancellationToken ct) =>
{
    using var scope = app.Services.CreateScope();
    var ticketRepo = scope.ServiceProvider.GetService<ITicketRepository>();
    if (ticketRepo == null) return Results.Json(new { detail = "Tickets not configured." }, statusCode: 500);
    var tickets = await ticketRepo.ListTicketsAsync(status: status, page: page, pageSize: 20, ct: ct).ConfigureAwait(false);
    return Results.Json(new { tickets });
}).WithName("ListTickets");

app.MapGet("/api/tickets/{id:int}", async (int id, HttpContext ctx, CancellationToken ct) =>
{
    using var scope = app.Services.CreateScope();
    var ticketRepo = scope.ServiceProvider.GetService<ITicketRepository>();
    if (ticketRepo == null) return Results.Json(new { detail = "Tickets not configured." }, statusCode: 500);
    var ticket = await ticketRepo.GetTicketAsync(id, ct).ConfigureAwait(false);
    if (ticket == null) return Results.Json(new { detail = "Not found." }, statusCode: 404);
    var messages = await ticketRepo.GetMessagesAsync(id, ct).ConfigureAwait(false);
    return Results.Json(new { ticket, messages });
}).WithName("TicketDetail");

app.MapPost("/api/tickets/{id:int}/reply", async (int id, HttpContext ctx, CancellationToken ct) =>
{
    using var scope = app.Services.CreateScope();
    var ticketRepo = scope.ServiceProvider.GetService<ITicketRepository>();
    if (ticketRepo == null) return Results.Json(new { detail = "Tickets not configured." }, statusCode: 500);
    var body = await new StreamReader(ctx.Request.Body).ReadToEndAsync(ct).ConfigureAwait(false);
    var json = System.Text.Json.JsonDocument.Parse(body);
    var text = json.RootElement.GetProperty("text").GetString() ?? "";
    var senderName = json.RootElement.TryGetProperty("senderName", out var sn) ? sn.GetString() : "Admin";
    await ticketRepo.AddMessageAsync(new TicketMessageDto(0, id, "admin", senderName, text, null, default), ct).ConfigureAwait(false);
    await ticketRepo.UpdateTicketStatusAsync(id, "in_progress", ct).ConfigureAwait(false);
    return Results.Json(new { ok = true });
}).WithName("ReplyTicket");

app.MapPost("/api/tickets/{id:int}/close", async (int id, HttpContext ctx, CancellationToken ct) =>
{
    using var scope = app.Services.CreateScope();
    var ticketRepo = scope.ServiceProvider.GetService<ITicketRepository>();
    if (ticketRepo == null) return Results.Json(new { detail = "Tickets not configured." }, statusCode: 500);
    await ticketRepo.UpdateTicketStatusAsync(id, "closed", ct).ConfigureAwait(false);
    return Results.Json(new { ok = true });
}).WithName("CloseTicket");

// ── Projects API ─────────────────────────────────────────────────
app.MapGet("/api/projects", async (string? status, int page, HttpContext ctx, CancellationToken ct) =>
{
    using var scope = app.Services.CreateScope();
    var projRepo = scope.ServiceProvider.GetService<IStudentProjectRepository>();
    if (projRepo == null) return Results.Json(new { detail = "Projects not configured." }, statusCode: 500);
    var projects = await projRepo.ListAsync(status: status, page: page, pageSize: 20, ct: ct).ConfigureAwait(false);
    return Results.Json(new { projects });
}).WithName("ListProjects");

app.MapPost("/api/projects/{id:int}/approve", async (int id, HttpContext ctx, CancellationToken ct) =>
{
    using var scope = app.Services.CreateScope();
    var projRepo = scope.ServiceProvider.GetService<IStudentProjectRepository>();
    if (projRepo == null) return Results.Json(new { detail = "Projects not configured." }, statusCode: 500);
    await projRepo.UpdateStatusAsync(id, "approved", ct: ct).ConfigureAwait(false);
    return Results.Json(new { ok = true });
}).WithName("ApproveProject");

app.MapPost("/api/projects/{id:int}/reject", async (int id, HttpContext ctx, CancellationToken ct) =>
{
    using var scope = app.Services.CreateScope();
    var projRepo = scope.ServiceProvider.GetService<IStudentProjectRepository>();
    if (projRepo == null) return Results.Json(new { detail = "Projects not configured." }, statusCode: 500);
    await projRepo.UpdateStatusAsync(id, "rejected", ct: ct).ConfigureAwait(false);
    return Results.Json(new { ok = true });
}).WithName("RejectProject");

app.MapGet("/api/projects/{id:int}/bids", async (int id, HttpContext ctx, CancellationToken ct) =>
{
    using var scope = app.Services.CreateScope();
    var bidRepo = scope.ServiceProvider.GetService<IProjectBidRepository>();
    if (bidRepo == null) return Results.Json(new { detail = "Project bids not configured." }, statusCode: 500);
    var bids = await bidRepo.ListForProjectAsync(id, ct).ConfigureAwait(false);
    return Results.Json(new { bids });
}).WithName("ProjectBids");

// ── International Questions API ──────────────────────────────────
app.MapGet("/api/questions", async (string? status, int page, HttpContext ctx, CancellationToken ct) =>
{
    using var scope = app.Services.CreateScope();
    var qRepo = scope.ServiceProvider.GetService<IInternationalQuestionRepository>();
    if (qRepo == null) return Results.Json(new { detail = "Questions not configured." }, statusCode: 500);
    var questions = await qRepo.ListAsync(status: status, page: page, pageSize: 20, ct: ct).ConfigureAwait(false);
    return Results.Json(new { questions });
}).WithName("ListQuestions");

// ── Sponsorship API ──────────────────────────────────────────────
app.MapGet("/api/sponsorships", async (string? status, int page, HttpContext ctx, CancellationToken ct) =>
{
    using var scope = app.Services.CreateScope();
    var spRepo = scope.ServiceProvider.GetService<ISponsorshipRepository>();
    if (spRepo == null) return Results.Json(new { detail = "Sponsorships not configured." }, statusCode: 500);
    var requests = await spRepo.ListRequestsAsync(status: status, page: page, pageSize: 20, ct: ct).ConfigureAwait(false);
    return Results.Json(new { requests });
}).WithName("ListSponsorships");

app.MapGet("/", () => Results.Ok("AbroadQs Telegram Bot (Webhook) is running."))
    .WithName("Health");

// ═══════════════════════════════════════════════════════════════════
//  Helper: send main menu reply keyboard from API endpoints
// ═══════════════════════════════════════════════════════════════════
static async Task SendMainMenuFromApi(IServiceProvider sp, ITelegramBotClient botClient, long userId, CancellationToken ct)
{
    try
    {
        var stageRepo = sp.GetService<IBotStageRepository>();
        var permRepo = sp.GetService<IPermissionRepository>();
        var userRepo = sp.GetService<ITelegramUserRepository>();
        var stateStore = sp.GetService<IUserConversationStateStore>();
        if (stageRepo == null) return;

        var user = userRepo != null ? await userRepo.GetByTelegramUserIdAsync(userId, ct).ConfigureAwait(false) : null;
        var isFa = (user?.PreferredLanguage ?? "fa") == "fa";

        var stage = await stageRepo.GetByKeyAsync("main_menu", ct).ConfigureAwait(false);
        var menuText = stage != null && stage.IsEnabled
            ? (isFa ? (stage.TextFa ?? stage.TextEn ?? "منوی اصلی") : (stage.TextEn ?? stage.TextFa ?? "Main Menu"))
            : (isFa ? "منوی اصلی" : "Main Menu");

        var allButtons = await stageRepo.GetButtonsAsync("main_menu", ct).ConfigureAwait(false);
        var permSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (permRepo != null)
        {
            var userPerms = await permRepo.GetUserPermissionsAsync(userId, ct).ConfigureAwait(false);
            permSet = new HashSet<string>(userPerms, StringComparer.OrdinalIgnoreCase);
        }

        var rows = new List<Telegram.Bot.Types.ReplyMarkups.KeyboardButton[]>();
        foreach (var row in allButtons
            .Where(b => b.IsEnabled && (string.IsNullOrEmpty(b.RequiredPermission) || permSet.Contains(b.RequiredPermission)))
            .GroupBy(b => b.Row).OrderBy(g => g.Key))
        {
            rows.Add(row.OrderBy(b => b.Column)
                .Select(b => new Telegram.Bot.Types.ReplyMarkups.KeyboardButton(isFa ? (b.TextFa ?? b.TextEn ?? "?") : (b.TextEn ?? b.TextFa ?? "?")))
                .ToArray());
        }

        if (rows.Count > 0)
        {
            var menuKb = new Telegram.Bot.Types.ReplyMarkups.ReplyKeyboardMarkup(rows) { ResizeKeyboard = true };
            await botClient.SendMessage(userId, menuText, parseMode: Telegram.Bot.Types.Enums.ParseMode.Html, replyMarkup: menuKb, cancellationToken: ct).ConfigureAwait(false);
        }

        if (stateStore != null)
            await stateStore.SetReplyStageAsync(userId, "main_menu", ct).ConfigureAwait(false);
    }
    catch { /* swallow — best effort */ }
}

app.Run();

record SetWebhookRequest(string? Url);
record SetTokenRequest(string? Token);
record SetUpdateModeRequest(string? Mode);
record KycRejectRequest(Dictionary<string, string>? Reasons);
record SetSupportTelegramRequest(string? Username);
record ManualRateUpdate(string? CurrencyCode, string? CurrencyNameFa, string? CurrencyNameEn, decimal Rate);
record RejectRequest(string? Note);
record GroupRejectRequest(string? Note);
record SetChannelRequest(string? ChannelId);
record SetFeeRequest(string? FeePercent);
