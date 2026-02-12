# AbroadQs Telegram Bot — Git & Deployment Guide

> A comprehensive, step-by-step guide for any developer or AI agent to perform Git operations and deploy the AbroadQs Telegram Bot to the production server.

---

## Table of Contents

1. [Project Overview](#1-project-overview)
2. [Prerequisites](#2-prerequisites)
3. [Repository Information](#3-repository-information)
4. [Architecture & Services](#4-architecture--services)
5. [Server Information](#5-server-information)
6. [Git Workflow](#6-git-workflow)
7. [Deployment Process](#7-deployment-process)
8. [Automated Deployment Script](#8-automated-deployment-script)
9. [Manual Deployment (SSH)](#9-manual-deployment-ssh)
10. [Rollback Procedure](#10-rollback-procedure)
11. [Monitoring & Health Checks](#11-monitoring--health-checks)
12. [Troubleshooting](#12-troubleshooting)
13. [Important Notes & Warnings](#13-important-notes--warnings)

---

## 1. Project Overview

| Field | Value |
|---|---|
| **Project** | AbroadQs Telegram Bot |
| **Framework** | .NET 8.0 (ASP.NET Core) |
| **Architecture** | Modular Monolith (Clean Architecture) |
| **Containerization** | Docker + Docker Compose |
| **Database** | SQL Server 2022 |
| **Message Broker** | RabbitMQ 3 |
| **Cache** | Redis 7 |
| **Bot Framework** | Telegram.Bot (Webhook mode) |

### Project Structure

```
AbroadQs.TelegramBot/
├── src/
│   ├── AbroadQs.Bot.Host.Webhook/     # Main entry point (Webhook host)
│   ├── AbroadQs.Bot.Application/      # Business logic & handlers
│   ├── AbroadQs.Bot.Contracts/        # Shared contracts & interfaces
│   ├── AbroadQs.Bot.Data/             # EF Core data layer & migrations
│   ├── AbroadQs.Bot.Telegram/         # Telegram integration layer
│   ├── AbroadQs.Bot.Modules.Common/   # Common bot modules (KYC, Exchange, etc.)
│   ├── AbroadQs.Bot.Modules.Example/  # Example module
│   ├── AbroadQs.TunnelServer/         # Webhook tunnel server
│   ├── AbroadQs.TunnelClient/         # Webhook tunnel client
│   └── AbroadQs.ReverseProxy/         # Reverse proxy
├── docker-compose.yml                  # Local development compose
├── docker-compose.server.yml           # Production server compose
├── _deploy.py                          # Automated deployment script
└── .gitignore
```

---

## 2. Prerequisites

### On Your Local Machine
- **Git** (any recent version)
- **Python 3.x** with `paramiko` package (for automated deployment)
  ```bash
  pip install paramiko
  ```
- **.NET 8.0 SDK** (only if you need to build/test locally)

### On the Production Server (already installed)
- **Docker Engine** (v20+)
- **Docker Compose** (v2+)
- **Git**

---

## 3. Repository Information

| Field | Value |
|---|---|
| **Remote URL** | `https://github.com/ThatsKiarash/AbroadQs.TelegramBot.git` |
| **Default Branch** | `main` |
| **Local Path** | `C:\Users\kiara\Desktop\TelegramProj\AbroadQs.TelegramBot` |
| **Server Path** | `/root/AbroadQs.TelegramBot/AbroadQs.TelegramBot` |

---

## 4. Architecture & Services

The production environment runs **5 Docker containers**:

| Container | Image | Port | Role |
|---|---|---|---|
| `abroadqs-bot` | Custom build | `5252` | Main bot application (Webhook) |
| `abroadqs-tunnel` | Custom build | `9080` (localhost only) | Webhook tunnel server |
| `abroadqs-sqlserver` | `mssql/server:2022-latest` | `1433` | Database |
| `abroadqs-rabbitmq` | `rabbitmq:3-management-alpine` | `5672`, `15672` | Message broker |
| `abroadqs-redis` | `redis:7-alpine` | `6379` | Cache |

### Key Environment Variables (set in docker-compose.server.yml)

| Variable | Value |
|---|---|
| `ConnectionStrings__DefaultConnection` | `Server=sqlserver,1433;Database=AbroadQsBot;User Id=sa;Password=YourStrong@Pass123;TrustServerCertificate=True;` |
| `RabbitMQ__HostName` | `rabbitmq` |
| `Redis__Configuration` | `redis:6379` |
| `PUBLIC_WEBHOOK_URL` | `https://webhook.abroadqs.com/webhook` |

---

## 5. Server Information

| Field | Value |
|---|---|
| **Host** | `167.235.159.117` |
| **SSH Port** | `2200` |
| **User** | `root` |
| **Password** | `Kia135724!` |
| **Provider** | Hetzner |
| **OS** | Linux (Ubuntu) |
| **Project Directory** | `/root/AbroadQs.TelegramBot/AbroadQs.TelegramBot` |

---

## 6. Git Workflow

### Step 1: Check Status

```bash
cd C:\Users\kiara\Desktop\TelegramProj\AbroadQs.TelegramBot
git status
```

### Step 2: Stage Changes

```bash
# Stage all changes
git add -A

# Or stage specific files
git add src/path/to/file.cs
```

### Step 3: Commit

```bash
git commit -m "Short description of changes"
```

**Commit message conventions used in this project:**
- `Add ...` — New feature
- `Fix ...` — Bug fix
- `Update ...` — Enhancement to existing feature
- `Redesign ...` — Major refactor of a feature

### Step 4: Push to GitHub

```bash
git push origin main
```

> **Note:** If push fails due to authentication, make sure your GitHub credentials/token are configured.

### Complete One-Liner (for agents)

```bash
git add -A && git commit -m "Description of changes" && git push origin main
```

---

## 7. Deployment Process

The deployment flow is:

```
Local Changes → git push → Server: git pull → Docker Build → Docker Up
```

### Full Deployment Sequence

```
1. Local:  git add -A
2. Local:  git commit -m "message"
3. Local:  git push origin main
4. Server: cd /root/AbroadQs.TelegramBot/AbroadQs.TelegramBot
5. Server: git pull origin main
6. Server: docker compose -f docker-compose.server.yml build bot tunnel
7. Server: docker compose -f docker-compose.server.yml up -d bot tunnel
8. Server: docker compose -f docker-compose.server.yml ps   # Verify
```

> **Important:** Only `bot` and `tunnel` services need to be rebuilt. Infrastructure services (`sqlserver`, `rabbitmq`, `redis`) use standard images and don't need rebuilding unless their configuration changes.

---

## 8. Automated Deployment Script

The project includes `_deploy.py` which automates the entire server-side deployment via SSH.

### Usage

```bash
cd C:\Users\kiara\Desktop\TelegramProj
python _deploy.py
```

### What it does (in order):
1. Connects to the server via SSH (`167.235.159.117:2200`)
2. Runs `git pull origin main`
3. Runs `docker compose -f docker-compose.server.yml build bot tunnel`
4. Runs `docker compose -f docker-compose.server.yml up -d bot tunnel`
5. Runs `docker compose -f docker-compose.server.yml ps` (status check)

### Expected Output on Success:
```
Connecting to 167.235.159.117:2200...
Connected!
>>> git pull origin main          → Shows updated files
>>> docker compose build bot tunnel → Build succeeded. 0 Error(s)
>>> docker compose up -d bot tunnel → Container abroadqs-bot Started
>>> docker compose ps              → All containers show "Up"
Done!
```

### For AI Agents — Complete Push + Deploy:

```bash
# Step 1: Push code (run in AbroadQs.TelegramBot directory)
cd C:\Users\kiara\Desktop\TelegramProj\AbroadQs.TelegramBot
git add -A && git commit -m "Description" && git push origin main

# Step 2: Deploy (run in TelegramProj directory)
cd C:\Users\kiara\Desktop\TelegramProj
python _deploy.py
```

---

## 9. Manual Deployment (SSH)

If the automated script is unavailable, you can deploy manually:

### Connect to Server

```bash
ssh -p 2200 root@167.235.159.117
# Password: Kia135724!
```

### Pull & Deploy

```bash
cd /root/AbroadQs.TelegramBot/AbroadQs.TelegramBot

# Pull latest code
git pull origin main

# Build only the app containers (not infrastructure)
docker compose -f docker-compose.server.yml build bot tunnel

# Restart with new build
docker compose -f docker-compose.server.yml up -d bot tunnel

# Verify all services are running
docker compose -f docker-compose.server.yml ps
```

### Using Python/Paramiko (for AI agents without SSH binary)

```python
import paramiko

ssh = paramiko.SSHClient()
ssh.set_missing_host_key_policy(paramiko.AutoAddPolicy())
ssh.connect('167.235.159.117', port=2200, username='root', password='Kia135724!', timeout=30)

commands = [
    "cd /root/AbroadQs.TelegramBot/AbroadQs.TelegramBot && git pull origin main",
    "cd /root/AbroadQs.TelegramBot/AbroadQs.TelegramBot && docker compose -f docker-compose.server.yml build bot tunnel",
    "cd /root/AbroadQs.TelegramBot/AbroadQs.TelegramBot && docker compose -f docker-compose.server.yml up -d bot tunnel",
    "cd /root/AbroadQs.TelegramBot/AbroadQs.TelegramBot && docker compose -f docker-compose.server.yml ps"
]

for cmd in commands:
    stdin, stdout, stderr = ssh.exec_command(cmd, timeout=120)
    print(stdout.read().decode())
    print(stderr.read().decode())
    exit_code = stdout.channel.recv_exit_status()
    if exit_code != 0:
        print(f"FAILED with exit code {exit_code}")
        break

ssh.close()
```

---

## 10. Rollback Procedure

If a deployment causes issues, roll back to the previous version:

### Quick Rollback

```bash
# On server (SSH):
cd /root/AbroadQs.TelegramBot/AbroadQs.TelegramBot

# Find the previous good commit
git log --oneline -10

# Reset to previous commit
git reset --hard <commit-hash>

# Rebuild and restart
docker compose -f docker-compose.server.yml build bot tunnel
docker compose -f docker-compose.server.yml up -d bot tunnel
```

### Via Paramiko (for agents)

```python
import paramiko

ssh = paramiko.SSHClient()
ssh.set_missing_host_key_policy(paramiko.AutoAddPolicy())
ssh.connect('167.235.159.117', port=2200, username='root', password='Kia135724!', timeout=30)

# Replace COMMIT_HASH with the target commit
rollback_commands = [
    "cd /root/AbroadQs.TelegramBot/AbroadQs.TelegramBot && git log --oneline -10",
    "cd /root/AbroadQs.TelegramBot/AbroadQs.TelegramBot && git reset --hard COMMIT_HASH",
    "cd /root/AbroadQs.TelegramBot/AbroadQs.TelegramBot && docker compose -f docker-compose.server.yml build bot tunnel",
    "cd /root/AbroadQs.TelegramBot/AbroadQs.TelegramBot && docker compose -f docker-compose.server.yml up -d bot tunnel",
]

for cmd in rollback_commands:
    stdin, stdout, stderr = ssh.exec_command(cmd, timeout=120)
    print(stdout.read().decode())
    exit_code = stdout.channel.recv_exit_status()

ssh.close()
```

---

## 11. Monitoring & Health Checks

### Check Container Status

```bash
# SSH into server, then:
cd /root/AbroadQs.TelegramBot/AbroadQs.TelegramBot
docker compose -f docker-compose.server.yml ps
```

Expected: All containers should show `Up` status.

### View Bot Logs

```bash
# Last 100 lines of bot logs
docker logs abroadqs-bot --tail 100

# Follow logs in real-time
docker logs abroadqs-bot -f

# Filter for errors
docker logs abroadqs-bot --tail 500 2>&1 | grep -iE "error|exception|fail"
```

### View Specific Service Logs

```bash
docker logs abroadqs-tunnel --tail 50
docker logs abroadqs-rabbitmq --tail 50
docker logs abroadqs-redis --tail 50
docker logs abroadqs-sqlserver --tail 50
```

### Check Resource Usage

```bash
docker stats --no-stream
```

### Health Check URLs

| Service | Health Check |
|---|---|
| **RabbitMQ Management** | `http://167.235.159.117:15672` (guest/guest) |
| **Bot Webhook** | `https://webhook.abroadqs.com/webhook` |

---

## 12. Troubleshooting

### Problem: `git pull` fails on server

```bash
# Check for local changes on server
cd /root/AbroadQs.TelegramBot/AbroadQs.TelegramBot
git status

# If there are untracked/modified files, reset:
git checkout -- .
git clean -fd
git pull origin main
```

### Problem: Docker build fails

```bash
# Check disk space
df -h

# Clean old Docker resources
docker system prune -f
docker builder prune -f

# Retry build
docker compose -f docker-compose.server.yml build --no-cache bot tunnel
```

### Problem: Bot container keeps restarting

```bash
# Check logs for the error
docker logs abroadqs-bot --tail 200

# Common causes:
# 1. Database not ready → wait for sqlserver to fully start
# 2. Wrong connection string → check docker-compose.server.yml
# 3. Code error → check the latest commit, consider rollback
```

### Problem: Database migration needed

```bash
# The bot auto-applies migrations on startup via EF Core.
# If you need to manually run migrations:
docker exec -it abroadqs-bot dotnet ef database update
```

### Problem: Webhook not receiving messages

```bash
# Check tunnel is running
docker logs abroadqs-tunnel --tail 50

# Verify webhook URL is set
docker exec abroadqs-bot printenv PUBLIC_WEBHOOK_URL

# The webhook URL should be: https://webhook.abroadqs.com/webhook
# Tunnel forwards from Telegram → Cloudflare → tunnel (9080) → bot (5252)
```

### Problem: SSH connection timeout

```bash
# Verify server is reachable
ping 167.235.159.117

# Try connecting with verbose output
ssh -v -p 2200 root@167.235.159.117
```

---

## 13. Important Notes & Warnings

### DO:
- **Always push to `main` branch** before deploying
- **Always verify** containers are running after deployment (`docker compose ps`)
- **Check bot logs** after deployment to confirm it started correctly
- **Build only `bot` and `tunnel`** — infrastructure containers use pre-built images
- **Use `_deploy.py`** for standard deployments (safest and easiest)

### DON'T:
- **Don't restart infrastructure containers** (`sqlserver`, `rabbitmq`, `redis`) unless absolutely necessary — they hold persistent data
- **Don't run `docker compose down`** unless you intend to stop all services — use `docker compose up -d bot tunnel` to restart only app services
- **Don't modify files directly on the server** — all changes should go through Git
- **Don't force-push to main** — it will cause conflicts on the server
- **Don't delete Docker volumes** (`sqlserver-data`, `rabbitmq-data`, `redis-data`) — they contain production data

### Networking:
- All containers are on the `abroadqs` Docker network
- Containers reference each other by service name (e.g., `sqlserver`, `rabbitmq`, `redis`)
- The tunnel server listens on `127.0.0.1:9080` (localhost only, Cloudflare connects to it)
- The bot listens on `0.0.0.0:5252`

### Webhook Flow:
```
Telegram → Cloudflare → Server:9080 (tunnel) → bot:5252 → Application
```

---

## Quick Reference Card (Copy-Paste Ready)

### Full Deploy from Local Machine:

```bash
# 1. Commit & Push
cd C:\Users\kiara\Desktop\TelegramProj\AbroadQs.TelegramBot
git add -A && git commit -m "Your commit message" && git push origin main

# 2. Deploy to Server
cd C:\Users\kiara\Desktop\TelegramProj
python _deploy.py
```

### Check Server Health:

```python
import paramiko
ssh = paramiko.SSHClient()
ssh.set_missing_host_key_policy(paramiko.AutoAddPolicy())
ssh.connect('167.235.159.117', port=2200, username='root', password='Kia135724!', timeout=30)
stdin, stdout, stderr = ssh.exec_command('cd /root/AbroadQs.TelegramBot/AbroadQs.TelegramBot && docker compose -f docker-compose.server.yml ps && echo "---" && docker logs abroadqs-bot --tail 20 2>&1', timeout=30)
print(stdout.read().decode())
ssh.close()
```

---

*Last updated: February 11, 2026*
*Maintained by: AbroadQs Development Team*
