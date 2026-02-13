import paramiko
ssh = paramiko.SSHClient()
ssh.set_missing_host_key_policy(paramiko.AutoAddPolicy())
ssh.connect('167.235.159.117', port=2200, username='root', password='Kia135724!', timeout=30)

cmds = [
    ("Check settings table", """docker exec sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "Kia135724!" -d AbroadQs -C -Q "SELECT SettingKey, LEFT(SettingValue,80) as Val FROM Settings WHERE SettingKey IN ('base_url','webhook_url','bitpay_api_key','bitpay_test_mode')" """),
    ("Check all settings", """docker exec sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "Kia135724!" -d AbroadQs -C -Q "SELECT SettingKey, LEFT(SettingValue,80) as Val FROM Settings" """),
    ("Check exchange groups", """docker exec sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "Kia135724!" -d AbroadQs -C -Q "SELECT TOP 5 Id, Title, GroupType FROM ExchangeGroups" """),
    ("Check ExchangeGroups table exists", """docker exec sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "Kia135724!" -d AbroadQs -C -Q "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME LIKE '%Group%' OR TABLE_NAME LIKE '%Exchange%'" """),
    ("Check BotStageButtons for exchange_groups", """docker exec sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "Kia135724!" -d AbroadQs -C -Q "SELECT TOP 10 StageId, TextFa, TargetStage FROM BotStageButtons WHERE StageId='exchange' OR StageId='new_request' OR TargetStage LIKE '%group%' OR TargetStage LIKE '%exchange%'" """),
    ("Recent error logs", "docker logs abroadqs-bot --tail 200 2>&1 | grep -iE 'error|exception|fail|bitpay|payment' | tail -20"),
]

for label, cmd in cmds:
    print(f"\n{'='*60}")
    print(f">>> {label}")
    print(f"{'='*60}")
    stdin, stdout, stderr = ssh.exec_command(cmd, timeout=60)
    out = stdout.read().decode()
    err = stderr.read().decode()
    if out.strip(): print(out)
    if err.strip(): print(f"STDERR: {err}")
    if not out.strip() and not err.strip(): print("(empty)")

ssh.close()
