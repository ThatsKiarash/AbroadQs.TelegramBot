import paramiko
ssh = paramiko.SSHClient()
ssh.set_missing_host_key_policy(paramiko.AutoAddPolicy())
ssh.connect('167.235.159.117', port=2200, username='root', password='Kia135724!')
stdin, stdout, stderr = ssh.exec_command('curl -s -o /dev/null -w "%{http_code}" https://abroadqs.com/api/email_relay.php')
print('Current HTTP status for email_relay.php:', stdout.read().decode())
ssh.close()
