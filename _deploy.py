import paramiko, sys, time
sys.stdout.reconfigure(encoding='utf-8')

host = "167.235.159.117"
port = 2200
user = "root"
password = "Kia135724!"

ssh = paramiko.SSHClient()
ssh.set_missing_host_key_policy(paramiko.AutoAddPolicy())
print(f"Connecting to {host}:{port}...")
ssh.connect(host, port=port, username=user, password=password, timeout=30)
print("Connected!")

commands = [
    "cd /root/AbroadQs.TelegramBot/AbroadQs.TelegramBot && git pull origin main",
    "cd /root/AbroadQs.TelegramBot/AbroadQs.TelegramBot && docker compose -f docker-compose.server.yml build bot tunnel",
    "cd /root/AbroadQs.TelegramBot/AbroadQs.TelegramBot && docker compose -f docker-compose.server.yml up -d bot tunnel",
    "cd /root/AbroadQs.TelegramBot/AbroadQs.TelegramBot && docker compose -f docker-compose.server.yml ps"
]

for cmd in commands:
    print(f"\n>>> {cmd}")
    stdin, stdout, stderr = ssh.exec_command(cmd, timeout=120)
    out = stdout.read().decode('utf-8', errors='replace')
    err = stderr.read().decode('utf-8', errors='replace')
    exit_code = stdout.channel.recv_exit_status()
    if out.strip():
        print(out.strip())
    if err.strip():
        print(err.strip())
    print(f"Exit code: {exit_code}")
    if exit_code != 0 and "build" not in cmd and "ps" not in cmd:
        print("FAILED - stopping")
        break

ssh.close()
print("\nDone!")
