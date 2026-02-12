import paramiko, sys
sys.stdout.reconfigure(encoding='utf-8')

ssh = paramiko.SSHClient()
ssh.set_missing_host_key_policy(paramiko.AutoAddPolicy())
print("Connecting...")
ssh.connect('167.235.159.117', port=2200, username='root', password='Kia135724!', timeout=30)
print("Connected!")

sa_password = "YourStrong@Pass123"

# Insert migration record so EF Core doesn't try to re-create tables
sql = "IF NOT EXISTS (SELECT 1 FROM __EFMigrationsHistory WHERE MigrationId = '20260212100000_AddExchangeTables') INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES ('20260212100000_AddExchangeTables', '8.0.0'); SELECT 'OK' AS Result;"
cmd = f"""docker exec abroadqs-sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U SA -P '{sa_password}' -d AbroadQsBot -C -Q "{sql}" """

print("Adding migration record...")
stdin, stdout, stderr = ssh.exec_command(cmd, timeout=30)
out = stdout.read().decode('utf-8', errors='replace')
err = stderr.read().decode('utf-8', errors='replace')
print(out.strip())
if err.strip():
    print(f"STDERR: {err.strip()}")
print(f"Exit: {stdout.channel.recv_exit_status()}")

# Restart bot
print("\nRestarting bot...")
cmd2 = "cd /root/AbroadQs.TelegramBot/AbroadQs.TelegramBot && docker compose -f docker-compose.server.yml restart bot"
stdin, stdout, stderr = ssh.exec_command(cmd2, timeout=60)
out = stdout.read().decode('utf-8', errors='replace')
err = stderr.read().decode('utf-8', errors='replace')
if out.strip():
    print(out.strip())
if err.strip():
    print(err.strip())
print(f"Exit: {stdout.channel.recv_exit_status()}")

# Check logs after restart
import time
time.sleep(5)
print("\nChecking logs...")
cmd3 = "docker logs abroadqs-bot --tail 20 2>&1"
stdin, stdout, stderr = ssh.exec_command(cmd3, timeout=30)
print(stdout.read().decode('utf-8', errors='replace'))

ssh.close()
print("Done!")
