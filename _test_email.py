import paramiko, sys
sys.stdout.reconfigure(encoding='utf-8')

ssh = paramiko.SSHClient()
ssh.set_missing_host_key_policy(paramiko.AutoAddPolicy())
ssh.connect('167.235.159.117', port=2200, username='root', password='Kia135724!', timeout=30)

# Test if port 465 is reachable from the server
cmds = [
    # Test DNS resolution
    'nslookup abroadqs.com 2>&1 | head -10',
    # Test port 465 connectivity
    'timeout 5 bash -c "echo | openssl s_client -connect abroadqs.com:465 -brief" 2>&1 | head -10',
    # Full bot logs (last 200 lines)
    'docker logs abroadqs-bot --tail 200 2>&1 | tail -80',
]

for cmd in cmds:
    print(f"\n=== {cmd[:60]}... ===")
    stdin, stdout, stderr = ssh.exec_command(cmd, timeout=15)
    out = stdout.read().decode('utf-8', 'replace')
    err = stderr.read().decode('utf-8', 'replace')
    print(out[:2000] if out.strip() else "(no output)")
    if err.strip():
        print("ERR:", err[:1000])

ssh.close()
