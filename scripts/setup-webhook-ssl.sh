#!/bin/bash
# نصب Nginx + Certbot و ریورس‌پروکسی با SSL برای webhook.abroadqs.com
# روی سرور اجرا کنید: bash setup-webhook-ssl.sh
# پیش‌نیاز: دامنه webhook.abroadqs.com باید به IP سرور اشاره کند.

set -e
DOMAIN="webhook.abroadqs.com"
# برای تانل (مثل ngrok): nginx به Tunnel Server (9080) می‌رود؛ اگر USE_TUNNEL_SERVER=1 بگذارید.
BACKEND_PORT="${BACKEND_PORT:-5252}"
[ -n "$USE_TUNNEL_SERVER" ] && BACKEND_PORT="${TUNNEL_SERVER_PORT:-9080}"

echo "=== نصب Nginx و Certbot ==="
if command -v apt-get &>/dev/null; then
    apt-get update -qq
    apt-get install -y -qq nginx certbot python3-certbot-nginx
elif command -v dnf &>/dev/null; then
    dnf install -y nginx certbot python3-certbot-nginx
else
    echo "لطفاً دستی nginx و certbot نصب کنید (apt یا dnf)."
    exit 1
fi

echo "=== ایجاد تنظیمات Nginx برای $DOMAIN ==="
NGINX_BLOCK="server {
    listen 80;
    server_name $DOMAIN;
    location / {
        proxy_pass http://127.0.0.1:$BACKEND_PORT;
        proxy_http_version 1.1;
        proxy_set_header Host \$host;
        proxy_set_header X-Real-IP \$remote_addr;
        proxy_set_header X-Forwarded-For \$proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto \$scheme;
    }
}"
if [ -d /etc/nginx/sites-available ]; then
    echo "$NGINX_BLOCK" > /etc/nginx/sites-available/webhook-abroadqs
    ln -sf /etc/nginx/sites-available/webhook-abroadqs /etc/nginx/sites-enabled/ 2>/dev/null || true
else
    mkdir -p /etc/nginx/conf.d
    echo "$NGINX_BLOCK" > /etc/nginx/conf.d/webhook-abroadqs.conf
fi
nginx -t && systemctl reload nginx

echo "=== دریافت گواهی SSL با Certbot ==="
# اگر متغیر EMAIL ست باشد (مثلاً export EMAIL=you@example.com) از آن استفاده می‌شود
if [ -n "$EMAIL" ]; then
    certbot --nginx -d "$DOMAIN" --non-interactive --agree-tos -m "$EMAIL"
else
    certbot --nginx -d "$DOMAIN" --non-interactive --agree-tos --register-unsafely-without-email 2>/dev/null || \
    { echo "لطفاً یک بار دستی اجرا کنید و ایمیل بدهید: certbot --nginx -d $DOMAIN"; }
fi
systemctl reload nginx 2>/dev/null || true

echo "=== تمام. آدرس Webhook: https://$DOMAIN/webhook ==="
