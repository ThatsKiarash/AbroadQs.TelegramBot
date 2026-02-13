import paramiko
ssh = paramiko.SSHClient()
ssh.set_missing_host_key_policy(paramiko.AutoAddPolicy())
ssh.connect('167.235.159.117', port=2200, username='root', password='Kia135724!', timeout=30)

DB = "AbroadQsBot"
SA = "YourStrong@Pass123"
CTR = "abroadqs-sqlserver"

def sql(label, query):
    cmd = f'''docker exec {CTR} /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "{SA}" -d {DB} -C -Q "{query}"'''
    print(f"\n{'='*60}")
    print(f">>> {label}")
    print(f"{'='*60}")
    stdin, stdout, stderr = ssh.exec_command(cmd, timeout=60)
    out = stdout.read().decode()
    err = stderr.read().decode()
    if out.strip(): print(out)
    if err.strip(): print(f"STDERR: {err}")
    if not out.strip() and not err.strip(): print("(empty)")

def sh(label, cmd):
    print(f"\n{'='*60}")
    print(f">>> {label}")
    print(f"{'='*60}")
    stdin, stdout, stderr = ssh.exec_command(cmd, timeout=60)
    out = stdout.read().decode()
    err = stderr.read().decode()
    if out.strip(): print(out)
    if err.strip(): print(f"STDERR: {err}")
    if not out.strip() and not err.strip(): print("(empty)")

# Check settings
sql("All settings", "SELECT SettingKey, LEFT(SettingValue,80) as Val FROM Settings")

# Check tables related to exchange/groups
sql("Tables like Group/Exchange", "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME LIKE '%Group%' OR TABLE_NAME LIKE '%Exchange%'")

# Check exchange groups data
sql("ExchangeGroups data", "SELECT TOP 5 * FROM ExchangeGroups")

# Check BotStageButtons for exchange
sql("Buttons for exchange stage", "SELECT StageId, TextFa, TargetStage FROM BotStageButtons WHERE StageId LIKE '%exchange%' OR TargetStage LIKE '%group%' OR TargetStage LIKE '%exchange%' ORDER BY StageId, Row, [Column]")

# Check finance reply-kb buttons
sql("Finance reply-kb buttons", "SELECT StageId, TextFa, TargetStage FROM BotStageButtons WHERE StageId='finance' ORDER BY Row, [Column]")

# Check bot stages 
sql("Bot stages", "SELECT [Key], LEFT(TextFa,50) as TextFa, IsEnabled FROM BotStages WHERE [Key] IN ('finance','exchange','exchange_groups','tickets','student_project','international_question','financial_sponsor','new_request')")

# Recent error logs
sh("Recent errors", "docker logs abroadqs-bot --tail 300 2>&1 | grep -iE 'error|exception|fail|bitpay|payment' | tail -25")

# Very recent logs
sh("Last 30 logs", "docker logs abroadqs-bot --tail 30 2>&1")

ssh.close()
