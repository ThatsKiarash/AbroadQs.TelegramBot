import paramiko
ssh = paramiko.SSHClient()
ssh.set_missing_host_key_policy(paramiko.AutoAddPolicy())
ssh.connect('167.235.159.117', port=2200, username='root', password='Kia135724!', timeout=30)

# Check for errors after new deployment
print("=== ERRORS AFTER DEPLOY ===")
stdin, stdout, stderr = ssh.exec_command('docker logs abroadqs-bot --tail 500 2>&1 | grep -iE "error|exception|fail|BankName|Invalid column|migration" | tail -30', timeout=30)
print(stdout.read().decode())

ssh.close()
