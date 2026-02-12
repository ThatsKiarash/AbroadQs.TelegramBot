"""Add missing columns directly via SQL and restart bot."""
import paramiko, sys, time
sys.stdout.reconfigure(encoding='utf-8')

ssh = paramiko.SSHClient()
ssh.set_missing_host_key_policy(paramiko.AutoAddPolicy())
ssh.connect("167.235.159.117", port=2200, username="root", password="Kia135724!", timeout=30)

# Add columns via SQL - use the correct password
print("=== Adding missing columns via SQL ===")
sql_commands = [
    "IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('TelegramUsers') AND name = 'PhoneVerified') ALTER TABLE TelegramUsers ADD PhoneVerified BIT NOT NULL DEFAULT 0; ELSE PRINT 'PhoneVerified exists';",
    "IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('TelegramUsers') AND name = 'PhoneVerificationMethod') ALTER TABLE TelegramUsers ADD PhoneVerificationMethod NVARCHAR(30) NULL; ELSE PRINT 'PhoneVerificationMethod exists';",
    "IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE MigrationId = '20260211100000_AddPhoneVerification') INSERT INTO [__EFMigrationsHistory] (MigrationId, ProductVersion) VALUES ('20260211100000_AddPhoneVerification', '8.0.11'); ELSE PRINT 'Migration exists';"
]

for sql in sql_commands:
    cmd = f"""docker exec abroadqs-sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P 'YourStrong@Pass123' -d AbroadQsBot -C -Q "{sql}" 2>&1"""
    stdin, stdout, stderr = ssh.exec_command(cmd, timeout=15)
    out = stdout.read().decode('utf-8', errors='replace').strip()
    err = stderr.read().decode('utf-8', errors='replace').strip()
    print(out or err or "(success)")

# Restart bot
print("\n=== Restarting bot ===")
stdin, stdout, stderr = ssh.exec_command(
    "cd /root/AbroadQs.TelegramBot/AbroadQs.TelegramBot && docker compose -f docker-compose.server.yml restart bot 2>&1",
    timeout=30
)
print(stdout.read().decode('utf-8', errors='replace').strip())

time.sleep(6)

# Verify
print("\n=== Testing API ===")
stdin, stdout, stderr = ssh.exec_command(
    "curl -s --max-time 5 http://localhost:5252/api/users 2>&1 | head -200",
    timeout=10
)
result = stdout.read().decode('utf-8', errors='replace').strip()
if '"error"' in result:
    print("ERROR:", result[:500])
else:
    print("SUCCESS - Users loaded:", result[:300])

ssh.close()
print("\nDone!")