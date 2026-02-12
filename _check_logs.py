import paramiko
ssh = paramiko.SSHClient()
ssh.set_missing_host_key_policy(paramiko.AutoAddPolicy())
ssh.connect('167.235.159.117', port=2200, username='root', password='Kia135724!', timeout=30)

# Check for errors
print("=== ERRORS IN LOGS ===")
stdin, stdout, stderr = ssh.exec_command('docker logs abroadqs-bot --tail 2000 2>&1 | grep -iE "error|exception|fail|Handler.*failed" | tail -30', timeout=30)
print(stdout.read().decode())

# Check what version is running
print("=== GIT LOG ON SERVER ===")
stdin, stdout, stderr = ssh.exec_command('cd /root/AbroadQs.TelegramBot/AbroadQs.TelegramBot && git log --oneline -5', timeout=15)
print(stdout.read().decode())

# Check container status
print("=== CONTAINER STATUS ===")
stdin, stdout, stderr = ssh.exec_command('cd /root/AbroadQs.TelegramBot/AbroadQs.TelegramBot && docker compose -f docker-compose.server.yml ps', timeout=15)
print(stdout.read().decode())

# Get the last 30 lines specifically looking for callback handling
print("=== LAST CALLBACK HANDLING ===")
stdin, stdout, stderr = ssh.exec_command('docker logs abroadqs-bot --tail 2000 2>&1 | grep -iE "exc_confirm|exc_cancel|callback|No handler claimed" | tail -20', timeout=30)
print(stdout.read().decode())

ssh.close()
