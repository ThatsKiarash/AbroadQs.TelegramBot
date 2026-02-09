import paramiko, sys
sys.stdout.reconfigure(encoding='utf-8')

ssh = paramiko.SSHClient()
ssh.set_missing_host_key_policy(paramiko.AutoAddPolicy())
ssh.connect('167.235.159.117', port=2200, username='root', password='Kia135724!', timeout=30)

cmd = 'docker logs abroadqs-bot 2>&1 | grep -iE "email|smtp|mail|error|fail|warn|exception|otp|timeout" | tail -50'
stdin, stdout, stderr = ssh.exec_command(cmd, timeout=30)
out = stdout.read().decode('utf-8', 'replace')
err = stderr.read().decode('utf-8', 'replace')
print(out)
if err.strip():
    print("STDERR:", err)
ssh.close()
