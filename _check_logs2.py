import paramiko, sys
sys.stdout.reconfigure(encoding='utf-8')

ssh = paramiko.SSHClient()
ssh.set_missing_host_key_policy(paramiko.AutoAddPolicy())
ssh.connect('167.235.159.117', port=2200, username='root', password='Kia135724!', timeout=30)

cmds = [
    # Full bot logs since restart
    'docker logs abroadqs-bot --tail 300 2>&1',
]

for cmd in cmds:
    print(f"\n=== {cmd[:60]} ===")
    stdin, stdout, stderr = ssh.exec_command(cmd, timeout=30)
    out = stdout.read().decode('utf-8', 'replace')
    print(out[-5000:] if len(out) > 5000 else out)

ssh.close()
