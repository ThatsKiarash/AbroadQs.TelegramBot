import paramiko, sys
sys.stdout.reconfigure(encoding='utf-8')
ssh = paramiko.SSHClient()
ssh.set_missing_host_key_policy(paramiko.AutoAddPolicy())
ssh.connect('167.235.159.117', port=2200, username='root', password='Kia135724!', timeout=30)

cmd = 'docker logs abroadqs-bot --tail 80 2>&1'
stdin, stdout, stderr = ssh.exec_command(cmd, timeout=30)
print(stdout.read().decode())

ssh.close()
