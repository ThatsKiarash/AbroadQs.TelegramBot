import paramiko
import sys

ssh = paramiko.SSHClient()
ssh.set_missing_host_key_policy(paramiko.AutoAddPolicy())
ssh.connect('167.235.159.117', port=2200, username='root', password='Kia135724!', timeout=30)

commands = [
    ("Git pull", "cd /root/AbroadQs.TelegramBot/AbroadQs.TelegramBot && git pull origin main"),
    ("Git log", "cd /root/AbroadQs.TelegramBot/AbroadQs.TelegramBot && git log --oneline -5"),
    ("Docker build", "cd /root/AbroadQs.TelegramBot/AbroadQs.TelegramBot && docker compose -f docker-compose.server.yml build bot tunnel"),
    ("Docker up", "cd /root/AbroadQs.TelegramBot/AbroadQs.TelegramBot && docker compose -f docker-compose.server.yml up -d bot tunnel"),
    ("Wait for startup", "sleep 10"),
    ("Apply migrations", "docker exec abroadqs-bot dotnet ef database update --project /src/src/AbroadQs.Bot.Data/AbroadQs.Bot.Data.csproj --startup-project /src/src/AbroadQs.Bot.Host.Webhook/AbroadQs.Bot.Host.Webhook.csproj 2>&1 || echo 'EF CLI not available, trying SQL...'"),
    ("Container status", "cd /root/AbroadQs.TelegramBot/AbroadQs.TelegramBot && docker compose -f docker-compose.server.yml ps"),
    ("Recent logs", "docker logs abroadqs-bot --tail 30 2>&1"),
]

for label, cmd in commands:
    print(f"\n{'='*60}")
    print(f">>> {label}")
    print(f"{'='*60}")
    stdin, stdout, stderr = ssh.exec_command(cmd, timeout=300)
    out = stdout.read().decode()
    err = stderr.read().decode()
    if out.strip():
        print(out)
    if err.strip():
        print(f"STDERR: {err}")

ssh.close()
print("\n>>> DONE")
