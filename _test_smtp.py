"""Test SMTP connectivity from the bot server to different hosts/ports."""
import paramiko, sys
sys.stdout.reconfigure(encoding='utf-8')

host = "167.235.159.117"
port = 2200
user = "root"
password = "Kia135724!"

ssh = paramiko.SSHClient()
ssh.set_missing_host_key_policy(paramiko.AutoAddPolicy())
print(f"Connecting to bot server {host}:{port}...")
ssh.connect(host, port=port, username=user, password=password, timeout=30)
print("Connected!\n")

# Test SMTP connectivity to various hosts and ports
tests = [
    # Direct Plesk server hostname (bypassing Cloudflare)
    ("windows5.centraldnserver.com", 25),
    ("windows5.centraldnserver.com", 465),
    ("windows5.centraldnserver.com", 587),
    ("windows5.centraldnserver.com", 2525),
    # abroadqs.com (goes through Cloudflare - likely won't work for SMTP)
    ("abroadqs.com", 465),
    ("abroadqs.com", 587),
    # Check MX records
]

# First check MX records
print("=== MX Records for abroadqs.com ===")
stdin, stdout, stderr = ssh.exec_command("dig MX abroadqs.com +short 2>/dev/null || nslookup -type=MX abroadqs.com 2>/dev/null || host -t MX abroadqs.com 2>/dev/null", timeout=10)
out = stdout.read().decode('utf-8', errors='replace').strip()
err = stderr.read().decode('utf-8', errors='replace').strip()
print(out or err or "(no output)")

# Check DNS resolution
print("\n=== DNS Resolution ===")
for h in ["windows5.centraldnserver.com", "abroadqs.com"]:
    stdin, stdout, stderr = ssh.exec_command(f"dig +short {h} A 2>/dev/null || nslookup {h} 2>/dev/null", timeout=10)
    out = stdout.read().decode('utf-8', errors='replace').strip()
    print(f"{h} -> {out}")

# Test SMTP port connectivity
print("\n=== SMTP Port Tests (3 second timeout each) ===")
for smtp_host, smtp_port in tests:
    cmd = f"timeout 3 bash -c 'echo QUIT | openssl s_client -connect {smtp_host}:{smtp_port} -quiet 2>&1 | head -5' 2>/dev/null; echo EXIT:$?"
    if smtp_port == 25 or smtp_port == 587 or smtp_port == 2525:
        cmd = f"timeout 3 bash -c '</dev/tcp/{smtp_host}/{smtp_port} && echo OPEN || echo CLOSED' 2>&1; echo EXIT:$?"
    
    print(f"\n--- {smtp_host}:{smtp_port} ---")
    stdin, stdout, stderr = ssh.exec_command(cmd, timeout=10)
    out = stdout.read().decode('utf-8', errors='replace').strip()
    err = stderr.read().decode('utf-8', errors='replace').strip()
    print(out or err or "(timeout/no response)")

# Simple Python test for each host:port
print("\n\n=== Python socket test (most reliable) ===")
for smtp_host, smtp_port in tests:
    cmd = f"""python3 -c "
import socket
s = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
s.settimeout(3)
try:
    s.connect(('{smtp_host}', {smtp_port}))
    print('OPEN - connected')
    try:
        banner = s.recv(1024).decode(errors='replace')[:100]
        print(f'Banner: {{banner}}')
    except:
        print('(no banner)')
except socket.timeout:
    print('TIMEOUT')
except Exception as e:
    print(f'FAILED: {{e}}')
finally:
    s.close()
" 2>&1"""
    print(f"\n--- {smtp_host}:{smtp_port} ---")
    stdin, stdout, stderr = ssh.exec_command(cmd, timeout=10)
    out = stdout.read().decode('utf-8', errors='replace').strip()
    print(out)

ssh.close()
print("\nDone!")
