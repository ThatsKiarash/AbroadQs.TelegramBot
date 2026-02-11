"""Test email relay from bot server."""
import paramiko, sys
sys.stdout.reconfigure(encoding='utf-8')

ssh = paramiko.SSHClient()
ssh.set_missing_host_key_policy(paramiko.AutoAddPolicy())
ssh.connect("167.235.159.117", port=2200, username="root", password="Kia135724!", timeout=30)

print("Testing email relay from bot server...")
stdin, stdout, stderr = ssh.exec_command(
    """curl -s -w '\\nHTTP:%{http_code} TIME:%{time_total}s' --max-time 30 -X POST https://abroadqs.com/emailrelay -H 'Content-Type: application/json' -d '{"token":"AbroadQs_Email_Relay_2026_Secure","to":"kiarash.zavare1@gmail.com","subject":"AbroadQs - Test from Bot Server","body":"<h2>AbroadQs</h2><p>Test email from bot server. Code: <b>99887</b></p>"}' 2>&1""",
    timeout=35
)
print(stdout.read().decode().strip())
ssh.close()
