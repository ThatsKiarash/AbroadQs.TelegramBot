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

// بارگذاری توکن از همان فایلی که داشبورد ذخیره می‌کند (پوشهٔ اجرای اپ) تا استارت بدون خطا باشد
var botToken = LoadTokenAtStartup();
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
    builder.Services.AddSingleton<IResponseSender, TelegramResponseSender>();
}

// Application + modules (add more modules here)
builder.Services.AddBotApplication();
builder.Services.AddCommonModule();
builder.Services.AddExampleModule();

// RabbitMQ (optional: if HostName is set, webhook publishes to queue and consumer dispatches)
builder.Services.Configure<RabbitMqOptions>(builder.Configuration.GetSection(RabbitMqOptions.SectionName));
var rabbitHost = builder.Configuration["RabbitMQ:HostName"];
if (!string.IsNullOrWhiteSpace(rabbitHost))
{
    builder.Services.AddSingleton<IRabbitMqPublisher, RabbitMqPublisher>();
    builder.Services.AddHostedService<RabbitMqConsumerService>();
}

// حالت به‌روزرسانی: Webhook یا GetUpdates (long polling)
builder.Services.Configure<GetUpdatesPollingOptions>(builder.Configuration.GetSection(GetUpdatesPollingOptions.SectionName));
builder.Services.AddHostedService<GetUpdatesPollingService>();

// Redis (optional: last command store; if not configured, no-op is used)
builder.Services.Configure<RedisOptions>(builder.Configuration.GetSection(RedisOptions.SectionName));
var redisConfig = builder.Configuration["Redis:Configuration"];
if (!string.IsNullOrWhiteSpace(redisConfig))
    builder.Services.AddSingleton<IUserLastCommandStore, RedisUserLastCommandStore>();
else
    builder.Services.AddSingleton<IUserLastCommandStore, NoOpUserLastCommandStore>();

// SQL Server (optional: if connection string is set, users are persisted)
var connStr = builder.Configuration.GetConnectionString("DefaultConnection");
if (!string.IsNullOrWhiteSpace(connStr))
{
    builder.Services.AddDbContext<ApplicationDbContext>(o => o.UseSqlServer(connStr));
    builder.Services.AddScoped<ITelegramUserRepository, TelegramUserRepository>();
    builder.Services.AddScoped<ISettingsRepository, SettingsRepository>();
}

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// برای داشبورد: زمان آخرین ست Webhook و لاگ آخرین پیام‌های وب‌هوک
var webhookSyncedAt = (DateTime?)null;
var webhookLogs = new System.Collections.Concurrent.ConcurrentQueue<WebhookLogEntry>();
const int MaxWebhookLogs = 50;

// Optional: apply EF migrations at startup when SQL Server is configured
if (!string.IsNullOrWhiteSpace(connStr))
{
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.MigrateAsync().ConfigureAwait(false);
        app.Logger.LogInformation("Database migrations applied.");
    }
}

// Optional: set webhook at startup only when mode is Webhook and URL is configured
var updateMode = builder.Configuration["Telegram:UpdateMode"] ?? "Webhook";
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

// API: لیست آخرین پیام‌های وب‌هوک (برای جدول داشبورد)
app.MapGet("/api/webhook/logs", () =>
{
    var list = webhookLogs.ToArray();
    var ordered = list.OrderByDescending(x => x.Time).Take(MaxWebhookLogs).Select(x => new
    {
        time = x.Time,
        status = x.Status,
        payloadPreview = x.PayloadPreview,
        error = x.Error
    });
    return Results.Json(ordered);
}).WithName("WebhookLogs");

// API: وضعیت داشبورد (ثبت شده/فعال)
app.MapGet("/api/dashboard/status", async (HttpContext ctx) =>
{
    var token = await ReadTokenAsync(ctx.RequestServices, ctx.RequestAborted).ConfigureAwait(false);
    var tokenSet = token.Length > 0 && token != "<YOUR_BOT_TOKEN>" && token != "0:placeholder";
    var updateMode = await ReadUpdateModeAsync(ctx.RequestServices, ctx.RequestAborted).ConfigureAwait(false);

    var lastLog = webhookLogs.ToArray().OrderByDescending(x => x.Time).FirstOrDefault();
    return Results.Json(new
    {
        tokenSet,
        updateMode,
        webhookSyncedAt,
        lastEventAt = lastLog?.Time,
        lastEventStatus = lastLog?.Status,
        lastEventError = lastLog?.Error
    });
}).WithName("DashboardStatus");

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

    return Results.Json(new { success = true, message = "توکن ذخیره شد. رفرش کن؛ اگر پاک شد، SQL Server را چک کن (Docker)." });
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

    return Results.Json(new { success = true, mode, message = "حالت ذخیره شد. برای اعمال، برنامه را یک بار ریستارت کنید." });
}).WithName("UpdateModeSet");

// Telegram sends POST with Update JSON body (و لاگ برای داشبورد)
app.MapPost("/webhook", async (HttpRequest request, HttpContext ctx, UpdateDispatcher dispatcher, CancellationToken ct) =>
{
    var update = await request.ReadFromJsonAsync<Update>(ct).ConfigureAwait(false);
    if (update == null)
        return Results.BadRequest();

    var preview = GetUpdatePreview(update);
    var rabbitPublisher = ctx.RequestServices.GetService<IRabbitMqPublisher>();
    try
    {
        if (rabbitPublisher != null)
        {
            await rabbitPublisher.PublishAsync(update, ct).ConfigureAwait(false);
            EnqueueLog(webhookLogs, MaxWebhookLogs, new WebhookLogEntry(DateTime.UtcNow, "Received", preview, null));
            return Results.Ok();
        }
        await dispatcher.DispatchAsync(update, ct).ConfigureAwait(false);
        EnqueueLog(webhookLogs, MaxWebhookLogs, new WebhookLogEntry(DateTime.UtcNow, "Received", preview, null));
        return Results.Ok();
    }
    catch (Exception ex)
    {
        EnqueueLog(webhookLogs, MaxWebhookLogs, new WebhookLogEntry(DateTime.UtcNow, "Error", preview, ex.Message));
        return Results.Json(new { error = ex.Message }, statusCode: 500);
    }
})
.WithName("TelegramWebhook");

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

static void EnqueueLog(System.Collections.Concurrent.ConcurrentQueue<WebhookLogEntry> queue, int max, WebhookLogEntry entry)
{
    queue.Enqueue(entry);
    while (queue.Count > max && queue.TryDequeue(out _)) { }
}

app.MapGet("/", () => Results.Ok("AbroadQs Telegram Bot (Webhook) is running."))
    .WithName("Health");

app.Run();

record SetWebhookRequest(string? Url);
record SetTokenRequest(string? Token);
record SetUpdateModeRequest(string? Mode);
record WebhookLogEntry(DateTime Time, string Status, string PayloadPreview, string? Error);
