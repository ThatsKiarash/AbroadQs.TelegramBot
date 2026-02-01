#!/bin/bash
# به‌روزرسانی nginx برای فوروارد به Tunnel Server (9080) و پشتیبانی WebSocket
set -e
CONF="/etc/nginx/sites-available/webhook-abroadqs"
[ -f "$CONF" ] || CONF="/etc/nginx/conf.d/webhook-abroadqs.conf"
if [ ! -f "$CONF" ]; then
  echo "Config not found"; exit 1
fi
# تغییر پورت به 9080 و اضافه کردن WebSocket
sed -i 's|proxy_pass http://127.0.0.1:5252;|proxy_pass http://127.0.0.1:9080;|g' "$CONF"
if ! grep -q "Upgrade" "$CONF"; then
  sed -i '/proxy_set_header X-Forwarded-Proto/a\        proxy_set_header Upgrade $http_upgrade;\n        proxy_set_header Connection "upgrade";\n        proxy_read_timeout 86400;\n        proxy_send_timeout 86400;' "$CONF"
fi
nginx -t && systemctl reload nginx
echo "Nginx updated: proxy_pass -> 9080, WebSocket headers added."
