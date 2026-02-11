import paramiko, sys
sys.stdout.reconfigure(encoding='utf-8')

ssh = paramiko.SSHClient()
ssh.set_missing_host_key_policy(paramiko.AutoAddPolicy())
ssh.connect('167.235.159.117', port=2200, username='root', password='Kia135724!', timeout=30)
print("Connected!")

cmds = [
    ("SMTP Port 465 test", 'timeout 5 bash -c "echo QUIT | openssl s_client -connect abroadqs.com:465 -quiet 2>&1 | head -5"'),
    ("SMTP Port 587 test", 'timeout 5 bash -c "echo QUIT | openssl s_client -connect abroadqs.com:587 -starttls smtp -quiet 2>&1 | head -5"'),
    ("NC port 465", 'timeout 3 nc -zv abroadqs.com 465 2>&1'),
    ("NC port 587", 'timeout 3 nc -zv abroadqs.com 587 2>&1'),
    ("Bot logs - email/otp", 'docker logs abroadqs-bot --tail 500 2>&1 | grep -iE "email|smtp|otp|mail|fail|error" | tail -30'),
    ("Bot logs - last 50", 'docker logs abroadqs-bot --tail 50 2>&1'),
]

for label, cmd in cmds:
    print(f"\n=== {label} ===")
    stdin, stdout, stderr = ssh.exec_command(cmd, timeout=15)
    out = stdout.read().decode('utf-8', errors='replace').strip()
    err = stderr.read().decode('utf-8', errors='replace').strip()
    if out: print(out)
    if err: print(f"STDERR: {err}")

ssh.close()
print("\nDone!")
