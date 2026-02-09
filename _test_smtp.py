import paramiko, sys
sys.stdout.reconfigure(encoding='utf-8')

ssh = paramiko.SSHClient()
ssh.set_missing_host_key_policy(paramiko.AutoAddPolicy())
ssh.connect('167.235.159.117', port=2200, username='root', password='Kia135724!', timeout=30)

# Try multiple SMTP tests
cmds = [
    # Test basic TCP connectivity to port 465
    "timeout 5 bash -c 'cat < /dev/null > /dev/tcp/abroadqs.com/465' 2>&1 && echo 'PORT 465 OPEN' || echo 'PORT 465 CLOSED'",
    # Test port 587 
    "timeout 5 bash -c 'cat < /dev/null > /dev/tcp/abroadqs.com/587' 2>&1 && echo 'PORT 587 OPEN' || echo 'PORT 587 CLOSED'",
    # Test port 25
    "timeout 5 bash -c 'cat < /dev/null > /dev/tcp/abroadqs.com/25' 2>&1 && echo 'PORT 25 OPEN' || echo 'PORT 25 CLOSED'",
    # Try sending a test email using Python from the server
    """python3 -c "
import smtplib, ssl, socket
socket.setdefaulttimeout(10)
try:
    ctx = ssl.create_default_context()
    with smtplib.SMTP_SSL('abroadqs.com', 465, context=ctx, timeout=10) as s:
        s.login('info@abroadqs.com', 'Kia135724!')
        s.sendmail('info@abroadqs.com', 'info@abroadqs.com', 'Subject: Test\\n\\nTest from server')
        print('EMAIL SENT OK')
except Exception as e:
    print(f'EMAIL FAILED: {type(e).__name__}: {e}')
" 2>&1""",
]

for cmd in cmds:
    print(f"\n=== Test ===")
    stdin, stdout, stderr = ssh.exec_command(cmd, timeout=20)
    out = stdout.read().decode('utf-8', 'replace')
    err = stderr.read().decode('utf-8', 'replace')
    print(out.strip() if out.strip() else "(no output)")
    if err.strip():
        print("ERR:", err.strip()[:500])

ssh.close()
