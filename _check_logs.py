import paramiko
ssh = paramiko.SSHClient()
ssh.set_missing_host_key_policy(paramiko.AutoAddPolicy())
ssh.connect('167.235.159.117', port=2200, username='root', password='Kia135724!', timeout=30)
stdin, stdout, stderr = ssh.exec_command('docker logs abroadqs-bot 2>&1 | grep -iE "Seed|BotStage|finance|ticket|INSERT"', timeout=30)
print(stdout.read().decode()[:3000])
print("---STDERR---")
print(stderr.read().decode()[:1000])
ssh.close()
