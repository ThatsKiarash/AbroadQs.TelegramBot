# AbroadQs Reverse Proxy

ریورس پروکسی سبک برای فوروارد کردن درخواست‌های عمومی به بک‌اند (مثلاً ربات تلگرام). می‌توانید به‌جای ngrok روی سرور خود این سرویس را اجرا کنید و آدرس HTTPS دامنهٔ خود را به عنوان Webhook در تلگرام ست کنید.

## تنظیمات

در `appsettings.json` (یا متغیر محیطی) آدرس بک‌اند را مشخص کنید:

```json
{
  "ReverseProxy": {
    "BackendUrl": "http://localhost:5252"
  }
}
```

- **روی سرور:** اگر ربات روی همان ماشین روی پورت 5252 اجرا می‌شود، همان `http://localhost:5252` کافی است.
- **روی ماشین دیگر:** آدرس واقعی بک‌اند را بگذارید، مثلاً `http://192.168.1.10:5252`.

## اجرا

```bash
cd src/AbroadQs.ReverseProxy
dotnet run
```

به‌صورت پیش‌فرض روی **http://localhost:8080** گوش می‌دهد.

### اجرا با Docker (روی سرور)

از پوشهٔ پروژه:

```bash
docker build -t abroadqs-reverse-proxy -f src/AbroadQs.ReverseProxy/Dockerfile src/AbroadQs.ReverseProxy
docker run -d -p 8080:8080 -e ReverseProxy__BackendUrl=http://host.docker.internal:5252 --name proxy abroadqs-reverse-proxy
```

اگر بک‌اند روی همان سرور است، به‌جای `host.docker.internal` می‌توانید IP سرویس/کانتینر بک‌اند یا `localhost` (در لینوکس گاهی `--add-host=host.docker.internal:host-gateway` لازم است) استفاده کنید. همهٔ مسیرها (مثل `/webhook`) به بک‌اند فوروارد می‌شوند.

## HTTPS روی سرور (برای Webhook تلگرام)

تلگرام فقط به آدرس **HTTPS** برای Webhook درخواست می‌فرستد. دو روش معمول:

### ۱. Caddy جلوی پروکسی

Caddy به‌صورت خودکار گواهی SSL می‌گیرد. مثال `Caddyfile`:

```
yourdomain.com {
    reverse_proxy localhost:8080
}
```

سپس Caddy را اجرا کنید و در داشبورد ربات، Webhook URL را بگذارید: `https://yourdomain.com/webhook`.

### ۲. Nginx جلوی پروکسی

یک سرور مجازی با SSL (مثلاً با Let's Encrypt) تعریف کنید و `proxy_pass` به `http://127.0.0.1:8080`.

## استفاده در داشبورد ربات

1. ریورس پروکسی و برنامهٔ ربات را روی سرور اجرا کنید.
2. در داشبورد، گزینهٔ Webhook (server) را انتخاب کنید.
3. در فیلد Webhook URL بنویسید: `https://YOUR_DOMAIN/webhook` (همان دامنه‌ای که Caddy/nginx با HTTPS سرو می‌کند).
4. «Set webhook» را بزنید.

از این به بعد به‌جای ngrok از دامنه و ریورس پروکسی خودتان استفاده می‌کنید.
