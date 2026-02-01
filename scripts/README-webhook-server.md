# راهنمای نصب SSL و ریورس‌پروکسی برای webhook.abroadqs.com روی سرور

## پیش‌نیاز

- دامنه **webhook.abroadqs.com** باید به IP سرور شما (مثلاً 167.235.159.117) اشاره کند (A record).
- پورت **2200** برای SSH باز باشد.

## ۱) اتصال به سرور از CMD/PowerShell

```bash
ssh root@167.235.159.117 -p 2200
```

وقتی خواست پسورد بدهد، پسورد را وارد کنید.

## ۲) کپی اسکریپت به سرور (از همین ویندوز)

در یک ترمینال **جدید** (قبل از SSH یا بعد از قطع اتصال):

```bash
scp -P 2200 "C:\Users\pc\Desktop\ServerSetup\AbroadQs.TelegramBot\scripts\setup-webhook-ssl.sh" root@167.235.159.117:/root/
```

دوباره پسورد را وارد کنید.

## ۳) روی سرور اجرا

بعد از اتصال با SSH (مرحله ۱)، روی سرور اجرا کنید:

```bash
chmod +x /root/setup-webhook-ssl.sh
bash /root/setup-webhook-ssl.sh
```

اگر Certbot خواست ایمیل بدهد، یک بار دستی اجرا کنید:

```bash
certbot --nginx -d webhook.abroadqs.com
```

و ایمیل خود را وارد کنید.

## ۴) پورت بک‌اند

اسکریپت به‌صورت پیش‌فرض به **پورت 5252** (برنامهٔ ربات) فوروارد می‌کند. اگر ربات روی پورت دیگری یا پشت ریورس‌پروکسی YARP روی 8080 است، قبل از اجرا بگذارید:

```bash
export BACKEND_PORT=8080
bash /root/setup-webhook-ssl.sh
```

## ۵) بعد از نصب

- آدرس Webhook در تلگرام: **https://webhook.abroadqs.com/webhook**
- در داشبورد ربات، Webhook URL را روی همین آدرس بگذارید و «Set webhook» بزنید.

---

## تانل (مثل ngrok) — وقتی ربات روی سرور پابلیش نیست

اگر می‌خواهید **هم روی سرور پابلیش** و **هم لوکال با تانل** از همان آدرس استفاده کنید:

1. **روی سرور:** nginx باید به **Tunnel Server (پورت 9080)** فوروارد کند، نه مستقیم به 5252. یک بار اسکریپت را با تانل اجرا کنید:
   ```bash
   USE_TUNNEL_SERVER=1 bash /root/setup-webhook-ssl.sh
   ```
   (اگر قبلاً nginx را با 5252 ست کرده‌اید، فایل تنظیمات nginx را ویرایش کنید و `proxy_pass` را به `http://127.0.0.1:9080` تغییر دهید.)

2. **روی سرور دو سرویس اجرا کنید:**
   - **Bot Host** روی پورت **5252** (وقتی روی سرور پابلیش است).
   - **Tunnel Server** روی پورت **9080** (مثلاً: `dotnet run --project src/AbroadQs.TunnelServer` یا سرویس/داکر).

3. **رفتار:**
   - اگر **هیچ Tunnel Client** وصل نباشد، درخواست‌ها از nginx → 9080 → 5252 (همان ربات روی سرور) می‌روند.
   - اگر از **لوکال** Tunnel Client را اجرا کنی و به سرور وصل شوی، درخواست‌ها از nginx → 9080 → تانل → ربات لوکال تو می‌روند.

4. **لوکال (توسعه):** در داشبورد گزینه «تانل (مثل ngrok)» را بزن، آدرس همان **https://webhook.abroadqs.com/webhook** می‌ماند؛ بعد Tunnel Client را اجرا کن:
   ```bash
   dotnet run --project src/AbroadQs.TunnelClient -- --url https://webhook.abroadqs.com --local 5252
   ```

---

**توجه:** پسورد را در هیچ فایل یا ریپو ذخیره نکنید. فقط هنگام درخواست SSH/SCP وارد کنید.
