import paramiko, sys
sys.stdout.reconfigure(encoding='utf-8')

ssh = paramiko.SSHClient()
ssh.set_missing_host_key_policy(paramiko.AutoAddPolicy())
ssh.connect('167.235.159.117', port=2200, username='root', password='Kia135724!', timeout=30)

DB = "AbroadQsBot"
SA = "YourStrong@Pass123"
CTR = "abroadqs-sqlserver"

# 1. Set the actual BitPay API key
api_key = "b6bd9-13a11-bbd6f-1a5d3-2bbc1c01ef10f9c838f4586277a7"
cmd = f'''docker exec {CTR} /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "{SA}" -d {DB} -C -Q "UPDATE Settings SET [Value]='{api_key}' WHERE [Key]='bitpay_api_key'; UPDATE Settings SET [Value]='false' WHERE [Key]='bitpay_test_mode'; SELECT [Key], [Value] FROM Settings WHERE [Key] LIKE 'bitpay%'"'''
print(">>> Setting BitPay API key (production mode)")
stdin, stdout, stderr = ssh.exec_command(cmd, timeout=60)
print(stdout.read().decode(errors='replace'))
err = stderr.read().decode(errors='replace')
if err.strip(): print(f"STDERR: {err}")

# 2. Check recent error logs for group-related issues
print("\n>>> Recent group-related logs")
cmd2 = "docker logs abroadqs-bot --tail 500 2>&1 | grep -iE 'grp_|group|exchange_group|ShowGroup|SafeSend' | tail -20"
stdin, stdout, stderr = ssh.exec_command(cmd2, timeout=60)
out = stdout.read().decode(errors='replace')
print(out if out.strip() else "(empty)")

# 3. Check ALL recent errors
print("\n>>> All recent errors")
cmd3 = "docker logs abroadqs-bot --tail 500 2>&1 | grep -iE 'error|exception|fail|warn' | tail -20"
stdin, stdout, stderr = ssh.exec_command(cmd3, timeout=60)
out = stdout.read().decode(errors='replace')
print(out if out.strip() else "(empty)")

# 4. Restart bot to pick up new API key (BitPayService reads key at DI time)
print("\n>>> Restarting bot container to pick up new API key...")
cmd4 = "cd /root/AbroadQs.TelegramBot/AbroadQs.TelegramBot && docker compose -f docker-compose.server.yml restart bot"
stdin, stdout, stderr = ssh.exec_command(cmd4, timeout=120)
print(stdout.read().decode(errors='replace'))
err = stderr.read().decode(errors='replace')
if err.strip(): print(f"STDERR: {err}")

import time
time.sleep(10)

# 5. Verify restart
print("\n>>> Bot status after restart")
cmd5 = "docker logs abroadqs-bot --tail 15 2>&1"
stdin, stdout, stderr = ssh.exec_command(cmd5, timeout=60)
out = stdout.read().decode(errors='replace')
safe = ''.join(c if ord(c) < 0xFFFF else '?' for c in out)
print(safe)

ssh.close()
print("\n>>> DONE")
