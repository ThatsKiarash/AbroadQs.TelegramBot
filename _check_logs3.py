import paramiko, sys
sys.stdout.reconfigure(encoding='utf-8')

ssh = paramiko.SSHClient()
ssh.set_missing_host_key_policy(paramiko.AutoAddPolicy())
ssh.connect('167.235.159.117', port=2200, username='root', password='Kia135724!', timeout=30)

cmds = [
    # Check if the PHP relay is reachable
    'curl -s -o /dev/null -w "%{http_code}" --connect-timeout 5 https://abroadqs.com/api/email_relay.php 2>&1',
    # Check email-related logs
    'docker logs abroadqs-bot 2>&1 | grep -iE "email|relay|smtp|mail|otp|failed|error|warn" | tail -30',
    # All recent bot handler logs
    'docker logs abroadqs-bot 2>&1 | grep -iE "handled by|KycState|email" | tail -20',
]

for cmd in cmds:
    print(f"\n=== {cmd[:60]}... ===")
    stdin, stdout, stderr = ssh.exec_command(cmd, timeout=15)
    out = stdout.read().decode('utf-8', 'replace').strip()
    print(out if out else "(no output)")

ssh.close()
