"""Read web.config from Plesk server via FTP."""
import ftplib
import io

FTP_HOST = "abroadqs.com"
FTP_USER = "MyPlesk"
FTP_PASS = "Kia135724!"

ftp = ftplib.FTP(timeout=30)
ftp.connect(FTP_HOST, 21)
ftp.set_pasv(True)
ftp.login(FTP_USER, FTP_PASS)
ftp.cwd("httpdocs")

# Read web.config
buf = io.BytesIO()
ftp.retrbinary("RETR web.config", buf.write)
print("=== web.config ===")
print(buf.getvalue().decode("utf-8-sig"))

ftp.quit()
