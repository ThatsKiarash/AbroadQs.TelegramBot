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
    if out.strip(): print(out[:2000])
    if err.strip(): print(f"STDERR: {err[:1000]}")
    if not out.strip() and not err.strip(): print("(empty)")

# Check Settings columns
sql("Settings schema", "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Settings'")

# Check BotStageButtons columns
sql("BotStageButtons schema", "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='BotStageButtons'")

# Check BotStages columns
sql("BotStages schema", "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='BotStages'")

# Get all settings
sql("All settings", "SELECT [Key], LEFT([Value],80) as Val FROM Settings")

# Get BotStages for modules
sql("Bot stages", "SELECT Id, [Key], LEFT(TextFa,40) as TextFa, IsEnabled FROM BotStages")

# Get BotStageButtons
sql("Stage buttons", "SELECT TOP 30 Id, StageId, TextFa, CallbackData, [Row], [Column] FROM BotStageButtons ORDER BY StageId, [Row], [Column]")

ssh.close()
