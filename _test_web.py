import paramiko, sys
sys.stdout.reconfigure(encoding='utf-8')

ssh = paramiko.SSHClient()
ssh.set_missing_host_key_policy(paramiko.AutoAddPolicy())
ssh.connect('167.235.159.117', port=2200, username='root', password='Kia135724!', timeout=30)

cmds = [
    # Check if we can reach abroadqs.com on HTTP/HTTPS
    "curl -s -o /dev/null -w '%{http_code}' --connect-timeout 5 https://abroadqs.com/ 2>&1",
    # Check if port 80/443 is open to abroadqs.com
    "timeout 5 bash -c 'cat < /dev/null > /dev/tcp/abroadqs.com/443' 2>&1 && echo 'PORT 443 OPEN' || echo 'PORT 443 CLOSED'",
    "timeout 5 bash -c 'cat < /dev/null > /dev/tcp/abroadqs.com/80' 2>&1 && echo 'PORT 80 OPEN' || echo 'PORT 80 CLOSED'",
    # Test if we can also reach smtp port on a non-standard port (2525)
    "timeout 3 bash -c 'cat < /dev/null > /dev/tcp/abroadqs.com/2525' 2>&1 && echo 'PORT 2525 OPEN' || echo 'PORT 2525 CLOSED'",
]

for cmd in cmds:
    print(f"=== Test: {cmd[:50]}... ===")
    stdin, stdout, stderr = ssh.exec_command(cmd, timeout=15)
    out = stdout.read().decode('utf-8', 'replace').strip()
    err = stderr.read().decode('utf-8', 'replace').strip()
    print(out if out else "(no output)")
    if err:
        print("ERR:", err[:300])
    print()

ssh.close()
