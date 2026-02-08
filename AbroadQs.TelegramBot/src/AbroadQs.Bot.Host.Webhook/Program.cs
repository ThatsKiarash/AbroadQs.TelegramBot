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
    builder.Services.AddScoped<IUserLastCommandStore, RedisUserLastCommandStore>();
    builder.Services.AddScoped<IUserConversationStateStore, RedisUserConversationStateStore>();
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
}
else
{
    builder.Services.AddSingleton<ITelegramUserRepository, NoOpTelegramUserRepository>();
    builder.Services.AddSingleton<IBotStageRepository, NoOpBotStageRepository>();
    builder.Services.AddSingleton<IPermissionRepository, NoOpPermissionRepository>();
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
                "<b>سلام {name}!</b>\n\nبه ربات <b>AbroadQs</b> خوش آمدید.\n\nلطفاً زبان مورد نظر خود را انتخاب کنید.",
                "<b>Hello {name}!</b>\n\nWelcome to <b>AbroadQs</b> bot.\n\nPlease select your preferred language.",
                true, null, null, 0),
            ("main_menu",
                "<b>منوی اصلی</b>\n\nیکی از گزینه‌های زیر را انتخاب کنید:",
                "<b>Main Menu</b>\n\nSelect an option below:",
                true, null, null, 1),
            ("settings",
                "<b>تنظیمات</b>\n\nتنظیمات پروفایل و زبان:",
                "<b>Settings</b>\n\nProfile and language settings:",
                true, null, "main_menu", 2),
            ("lang_select",
                "زبان مورد نظر خود را انتخاب کنید:",
                "Select your preferred language:",
                true, null, "settings", 3),
            ("profile",
                "نام و نام خانوادگی خود را در یک خط بفرستید.\nمثلاً: <b>علی احمدی</b>",
                "Send your first and last name in one line.\nFor example: <b>John Smith</b>",
                true, null, "settings", 4),
            ("new_request",
                "<b>📋 ثبت درخواست</b>\n\nدرخواست جدید خود را ثبت کنید:",
                "<b>📋 Submit Request</b>\n\nSubmit your new request:",
                true, null, "main_menu", 5),
            ("finance",
                "<b>💰 امور مالی</b>\n\nوضعیت مالی شما:",
                "<b>💰 Finance</b>\n\nYour financial status:",
                true, null, "main_menu", 6),
            ("my_suggestions",
                "<b>💡 پیشنهادات من</b>\n\nپیشنهادات شما:",
                "<b>💡 My Suggestions</b>\n\nYour suggestions:",
                true, null, "main_menu", 7),
            ("my_messages",
                "<b>✉️ پیام های من</b>\n\nپیام‌های شما:",
                "<b>✉️ My Messages</b>\n\nYour messages:",
                true, null, "main_menu", 8),
            ("about_us",
                "<b>ℹ️ درباره ما</b>\n\nاطلاعات درباره AbroadQs:",
                "<b>ℹ️ About Us</b>\n\nAbout AbroadQs:",
                true, null, "main_menu", 9),
            ("tickets",
                "<b>🎫 تیکت ها</b>\n\nتیکت‌های پشتیبانی شما:",
                "<b>🎫 Tickets</b>\n\nYour support tickets:",
                true, null, "main_menu", 10),
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
                // Row 0: ثبت درخواست
                new BotStageButtonEntity { StageId = mainMenuStage.Id, TextFa = "📋 ثبت درخواست", TextEn = "📋 Submit Request", ButtonType = "callback", CallbackData = "stage:new_request", Row = 0, Column = 0, IsEnabled = true },
                // Row 1: امور مالی | پیشنهادات من | پیام های من
                new BotStageButtonEntity { StageId = mainMenuStage.Id, TextFa = "💰 امور مالی", TextEn = "💰 Finance", ButtonType = "callback", CallbackData = "stage:finance", Row = 1, Column = 0, IsEnabled = true },
                new BotStageButtonEntity { StageId = mainMenuStage.Id, TextFa = "💡 پیشنهادات من", TextEn = "💡 My Suggestions", ButtonType = "callback", CallbackData = "stage:my_suggestions", Row = 1, Column = 1, IsEnabled = true },
                new BotStageButtonEntity { StageId = mainMenuStage.Id, TextFa = "✉️ پیام های من", TextEn = "✉️ My Messages", ButtonType = "callback", CallbackData = "stage:my_messages", Row = 1, Column = 2, IsEnabled = true },
                // Row 2: پروفایل من | درباره ما | تیکت ها
                new BotStageButtonEntity { StageId = mainMenuStage.Id, TextFa = "👤 پروفایل من", TextEn = "👤 My Profile", ButtonType = "callback", CallbackData = "stage:profile", Row = 2, Column = 0, IsEnabled = true },
                new BotStageButtonEntity { StageId = mainMenuStage.Id, TextFa = "ℹ️ درباره ما", TextEn = "ℹ️ About Us", ButtonType = "callback", CallbackData = "stage:about_us", Row = 2, Column = 1, IsEnabled = true },
                new BotStageButtonEntity { StageId = mainMenuStage.Id, TextFa = "🎫 تیکت ها", TextEn = "🎫 Tickets", ButtonType = "callback", CallbackData = "stage:tickets", Row = 2, Column = 2, IsEnabled = true },
                // Row 3: تنظیمات
                new BotStageButtonEntity { StageId = mainMenuStage.Id, TextFa = "⚙️ تنظیمات", TextEn = "⚙️ Settings", ButtonType = "callback", TargetStageKey = "settings", Row = 3, Column = 0, IsEnabled = true }
            );
        }

        var settingsStage = db.BotStages.FirstOrDefault(s => s.StageKey == "settings");
        if (settingsStage != null && !db.BotStageButtons.Any(b => b.StageId == settingsStage.Id))
        {
            db.BotStageButtons.AddRange(
                new BotStageButtonEntity { StageId = settingsStage.Id, TextFa = "🌐 زبان", TextEn = "🌐 Language", ButtonType = "callback", TargetStageKey = "lang_select", Row = 0, Column = 0, IsEnabled = true },
                new BotStageButtonEntity { StageId = settingsStage.Id, TextFa = "👤 نام و نام‌خانوادگی", TextEn = "👤 Name & Family", ButtonType = "callback", TargetStageKey = "profile", Row = 1, Column = 0, IsEnabled = true }
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

app.MapGet("/", () => Results.Ok("AbroadQs Telegram Bot (Webhook) is running."))
    .WithName("Health");

app.Run();

record SetWebhookRequest(string? Url);
record SetTokenRequest(string? Token);
record SetUpdateModeRequest(string? Mode);
