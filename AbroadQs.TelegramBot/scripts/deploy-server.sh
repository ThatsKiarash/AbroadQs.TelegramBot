#!/bin/bash
# Deploy AbroadQs Telegram Bot on server
# Run on server: bash deploy-server.sh

set -e
REPO_DIR="${REPO_DIR:-/root/AbroadQs.TelegramBot}"
REPO_URL="${REPO_URL:-https://github.com/ThatsKiarash/AbroadQs.TelegramBot.git}"

echo "=== AbroadQs Bot Server Deploy ==="

# 1. Clone or pull
if [ -d "$REPO_DIR" ]; then
  echo "Pulling latest..."
  cd "$REPO_DIR"
  git pull origin main || true
else
  echo "Cloning repo..."
  git clone "$REPO_URL" "$REPO_DIR"
  cd "$REPO_DIR"
fi

# 2. Stop old containers
echo "Stopping old containers..."
docker compose -f docker-compose.server.yml down 2>/dev/null || true

# 3. Build and run
echo "Building and starting..."
docker compose -f docker-compose.server.yml up -d --build

# 4. Wait for SQL Server
echo "Waiting for SQL Server (30s)..."
sleep 30

# Migrations are applied by the bot at startup.

echo ""
echo "=== Done ==="
echo "Bot: http://localhost:5252"
echo "Dashboard: http://localhost:5252/dashboard"
echo ""
echo "If nginx proxies webhook.abroadqs.com -> 5252, use:"
echo "  https://webhook.abroadqs.com/dashboard"
echo ""
echo "Set token and webhook in dashboard after first start."
