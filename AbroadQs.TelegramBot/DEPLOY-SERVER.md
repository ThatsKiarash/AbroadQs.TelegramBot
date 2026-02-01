# راهنمای اجرای ربات روی سرور

## پیش‌نیاز

- Docker و Docker Compose روی سرور نصب باشد
- دامنه **webhook.abroadqs.com** به IP سرور اشاره کند
- پورت SSH (مثلاً 2200) باز باشد

## روش ۱: اجرا با اسکریپت (ساده‌تر)

### مرحله ۱: اتصال به سرور

```bash
ssh root@167.235.159.117 -p 2200
```

### مرحله ۲: دانلود اسکریپت و اجرا

```bash
curl -sSL -o /root/deploy-server.sh https://raw.githubusercontent.com/ThatsKiarash/AbroadQs.TelegramBot/main/AbroadQs.TelegramBot/scripts/deploy-server.sh
chmod +x /root/deploy-server.sh
bash /root/deploy-server.sh
```

### مرحله ۳: تنظیم nginx (اگر هنوز تنظیم نشده)

اسکریپت SSL قبلی (`setup-webhook-ssl.sh`) را اجرا کنید تا nginx به پورت 5252 فوروارد کند:

```bash
export BACKEND_PORT=5252
bash /root/setup-webhook-ssl.sh
```

### مرحله ۴: تنظیم توکن و Webhook در داشبورد

1. به **https://webhook.abroadqs.com/dashboard** بروید
2. توکن ربات را وارد کنید و ذخیره کنید
3. Webhook URL را `https://webhook.abroadqs.com/webhook` قرار دهید
4. دکمه **Set webhook** را بزنید

---

## روش ۲: اجرا دستی

```bash
# 1. کلون
cd /root
git clone https://github.com/ThatsKiarash/AbroadQs.TelegramBot.git
cd AbroadQs.TelegramBot/AbroadQs.TelegramBot

# 2. اجرا با Docker
docker compose -f docker-compose.server.yml up -d --build

# 3. صبر کنید تا SQL Server بالا بیاید (حدود ۳۰ ثانیه)
sleep 30
```

---

## بروزرسانی ربات

```bash
cd /root/AbroadQs.TelegramBot
git pull origin main
cd AbroadQs.TelegramBot
docker compose -f docker-compose.server.yml up -d --build
```

---

## سرویس‌ها

| سرویس   | پورت | توضیح              |
|---------|------|--------------------|
| Bot     | 5252 | ربات و داشبورد     |
| RabbitMQ| 5672 | صف پیام‌ها         |
| RabbitMQ UI | 15672 | پنل مدیریت |
| Redis   | 6379 | ذخیره دستورات     |
| SQL Server | 1433 | دیتابیس کاربران |

---

## عیب‌یابی

- **ربات جواب نمیده:** توکن و Webhook را در داشبورد چک کنید
- **داشبورد باز نمیشه:** nginx را چک کنید؛ باید به 5252 فوروارد کند
- **لاگ‌ها:** `docker logs abroadqs-bot`
