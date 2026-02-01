# AbroadQs Telegram Bot (Webhook)

ربات تلگرام مقیاس‌پذیر با **Webhook** و معماری **ماژولار** در C# (.NET 8).

## معماری

```
┌─────────────────────────────────────────────────────────────────┐
│  AbroadQs.Bot.Host.Webhook (ASP.NET Core)                       │
│  - POST /webhook ← Telegram sends Update JSON                    │
│  - UpdateDispatcher.DispatchAsync(update)                        │
└───────────────────────────────┬─────────────────────────────────┘
                                │
┌───────────────────────────────▼─────────────────────────────────┐
│  AbroadQs.Bot.Application                                       │
│  - UpdateDispatcher: maps Update → BotUpdateContext             │
│  - Runs IUpdateHandler pipeline (first CanHandle + Handle wins)  │
└───────────────────────────────┬─────────────────────────────────┘
                                │
        ┌───────────────────────┼───────────────────────┐
        ▼                       ▼                       ▼
┌───────────────┐     ┌─────────────────┐     ┌─────────────────┐
│ Modules.Common│     │ Modules.Example  │     │ Modules.* (you)  │
│ /start, /help │     │ /echo            │     │ هر ماژول جدید    │
└───────┬───────┘     └────────┬─────────┘     └────────┬─────────┘
        │                      │                         │
        └──────────────────────┼─────────────────────────┘
                               ▼
┌─────────────────────────────────────────────────────────────────┐
│  AbroadQs.Bot.Contracts                                          │
│  - BotUpdateContext, IUpdateHandler, IResponseSender             │
│  - بدون وابستگی به Telegram.Bot → قابل استفاده در پروژه‌های دیگر │
└───────────────────────────────┬─────────────────────────────────┘
                                │
┌───────────────────────────────▼─────────────────────────────────┐
│  AbroadQs.Bot.Telegram                                           │
│  - TelegramResponseSender : IResponseSender                      │
│  - AddTelegramBot(token) → قابل استفاده در هر پروژه             │
└─────────────────────────────────────────────────────────────────┘
```

## پروژه‌ها

| پروژه | نقش |
|--------|-----|
| **AbroadQs.Bot.Contracts** | قراردادها و DTOها (بدون وابستگی به تلگرام). قابل استفاده در هر پروژه. |
| **AbroadQs.Bot.Telegram** | پیاده‌سازی ارسال پاسخ با Telegram Bot API. قابل استفاده در هر پروژه. |
| **AbroadQs.Bot.Application** | دیسپچر آپدیت و تبدیل Update به BotUpdateContext. |
| **AbroadQs.Bot.Modules.Common** | ماژول مشترک: /start, /help. |
| **AbroadQs.Bot.Modules.Example** | ماژول نمونه: /echo. الگو برای ماژول‌های جدید. |
| **AbroadQs.Bot.Host.Webhook** | میزبان ASP.NET Core؛ دریافت Webhook و راه‌اندازی pipeline. |

## اضافه کردن ماژول جدید

1. پروژه کلاس‌لایبری جدید بسازید، مثلاً `AbroadQs.Bot.Modules.Ads`.
2. رفرنس به `AbroadQs.Bot.Application` و `AbroadQs.Bot.Telegram` اضافه کنید.
3. کلاس‌هایی که `IUpdateHandler` را پیاده می‌کنند بنویسید و در یک کلاس استاتیک با متد `AddXxxModule(this IServiceCollection)` ثبت کنید.
4. در Host (Program.cs) بعد از ماژول‌های دیگر فراخوانی کنید:
   ```csharp
   builder.Services.AddAdsModule(); // یا هر نام ماژول
   ```

## تنظیمات

در `appsettings.json`:

```json
{
  "Telegram": {
    "BotToken": "YOUR_BOT_TOKEN",
    "WebhookUrl": "https://api.abroadqs.com/webhook"
  }
}
```

- **BotToken**: توکن ربات از @BotFather.
- **WebhookUrl**: اگر مقدار داشته باشد، در استارت اپلیکیشن با `SetWebhook` روی تلگرام ست می‌شود. برای محیط لوکال خالی بگذارید و با ngrok یا ابزار دیگر تست کنید.

## اجرا

```bash
cd src/AbroadQs.Bot.Host.Webhook
dotnet run
```

برای دریافت آپدیت از تلگرام باید دامنه شما HTTPS و در دسترس اینترنت باشد (مثلاً api.abroadqs.com با Nginx + Certbot). بعد از deploy، مقدار `WebhookUrl` را در تنظیمات سرور قرار دهید.

---

## Local testing (Docker + Redis + RabbitMQ)

برای تست روی سیستم خودت قبل از رفتن روی سرور:

### 1. Start Redis and RabbitMQ (Docker)

From the solution root:

```bash
docker-compose up -d
```

- **Redis:** `localhost:6379`
- **RabbitMQ:** AMQP `localhost:5672`, Management UI `http://localhost:15672` (user/pass: `guest`/`guest`)

Check:

```bash
docker-compose ps
```

### 2. Run the bot

```bash
cd src/AbroadQs.Bot.Host.Webhook
dotnet run
```

Dashboard: **http://localhost:5252/dashboard**

### 3. Expose localhost for Telegram (ngrok)

Telegram needs a public HTTPS URL. Use ngrok:

```bash
ngrok http 5252
```

Copy the HTTPS URL (e.g. `https://xxxx.ngrok-free.app`).

### 4. Set webhook in dashboard

1. Open **http://localhost:5252/dashboard**
2. In Credentials: enter **Bot Token** (from @BotFather) → **Save configuration**
3. Select **Ngrok** and paste the ngrok URL + `/webhook`, e.g. `https://xxxx.ngrok-free.app/webhook`
4. Click **Set webhook**
5. Send a message to your bot in Telegram → you should see it in **Recent Webhook Messages** and **Connection Status** should show Connected

### 5. When everything is OK → deploy to server

- Deploy the app to the server (e.g. `api.abroadqs.com`)
- On the server: set **Webhook (server)** and URL `https://api.abroadqs.com/webhook`, then **Set webhook**
- Optionally run Redis/RabbitMQ on the server with the same `docker-compose` or your server setup

### Stop local services

```bash
docker-compose down
```

## عیب‌یابی: ربات جواب نمی‌دهد

اگر در هر دو حالت **Webhook** و **GetUpdates** ربات پاسخی نمی‌دهد:

1. **ریستارت بعد از ذخیرهٔ توکن و حالت**  
   توکن و حالت به‌روزرسانی فقط **موقع استارت** از فایل/دیتابیس خوانده می‌شوند. بعد از ذخیره در داشبورد حتماً برنامه را یک بار ریستارت کنید.

2. **دستورات شناخته‌شده**  
   ربات به دستورات `/start`، `/help` و `/echo متن` پاسخ می‌دهد. برای هر پیام دیگر یک پاسخ پیش‌فرض («این دستور شناخته نشد…») ارسال می‌شود. اگر هیچ پاسخی نمی‌بینید، احتمالاً ربات با توکن واقعی بالا نیامده (بند ۱).

3. **حالت Webhook**  
   آدرس Webhook باید از اینترنت در دسترس باشد (HTTPS). اگر روی لوکال تست می‌کنید، از Tunnel (مثل ngrok) یا Tunnel Client پروژه استفاده کنید و در داشبورد **Set webhook** را بزنید.

4. **حالت GetUpdates**  
   بعد از تغییر به GetUpdates، برنامه را ریستارت کنید تا long polling شروع شود. در لاگ باید خط «Webhook removed; starting GetUpdates long polling» دیده شود.

## وابستگی‌ها

- .NET 8
- Telegram.Bot 22.x
- Microsoft.Extensions.\* (DI, Logging)

## استفاده از Contracts و Telegram در پروژه‌های دیگر

- **Contracts**: فقط به پروژه `AbroadQs.Bot.Contracts` رفرنس بدهید؛ هیچ وابستگی به تلگرام ندارد.
- **Telegram**: برای ارسال پیام از طریق همان ربات در یک سرویس دیگر، به `AbroadQs.Bot.Telegram` و `AbroadQs.Bot.Contracts` رفرنس بدهید و در DI از `AddTelegramBot(token)` و `IResponseSender` استفاده کنید.
