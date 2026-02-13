import paramiko
ssh = paramiko.SSHClient()
ssh.set_missing_host_key_policy(paramiko.AutoAddPolicy())
ssh.connect('167.235.159.117', port=2200, username='root', password='Kia135724!', timeout=30)

cmds = [
    ("Payment logs", 'docker logs abroadqs-bot --tail 1000 2>&1 | grep -iE "bitpay|payment|charge|fin_charge|CreatePayment|base_url|webhook_url" | tail -30'),
    ("Error logs", 'docker logs abroadqs-bot --tail 1000 2>&1 | grep -iE "error|exception|fail" | tail -30'),
    ("Group logs", 'docker logs abroadqs-bot --tail 1000 2>&1 | grep -iE "grp_|group|exchange_group" | tail -20'),
    ("Settings DB", """docker exec abroadqs-bot sh -c 'dotnet tool install -g dotnet-script 2>/dev/null; echo "SELECT TOP 5 SettingKey, SettingValue FROM Settings WHERE SettingKey IN ('"'"'base_url'"'"','"'"'webhook_url'"'"','"'"'bitpay_api_key'"'"','"'"'bitpay_test_mode'"'"')" | /opt/mssql-tools18/bin/sqlcmd -S sqlserver -U sa -P "Kia135724!" -d AbroadQs -C -h -1 2>/dev/null || echo "sqlcmd not available"'"""),
    ("Recent bot logs", "docker logs abroadqs-bot --tail 40 2>&1"),
]

for label, cmd in cmds:
    print(f"\n{'='*50}")
    print(f">>> {label}")
    print(f"{'='*50}")
    stdin, stdout, stderr = ssh.exec_command(cmd, timeout=60)
    out = stdout.read().decode()
    err = stderr.read().decode()
    if out.strip(): print(out)
    if err.strip(): print(f"STDERR: {err}")
    if not out.strip() and not err.strip(): print("(empty)")

ssh.close()
