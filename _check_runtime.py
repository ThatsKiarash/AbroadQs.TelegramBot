"""Read AbroadWeb.runtimeconfig.json from Plesk to determine .NET version."""
import ftplib, io

ftp = ftplib.FTP(timeout=30)
ftp.connect("abroadqs.com", 21)
ftp.set_pasv(True)
ftp.login("MyPlesk", "Kia135724!")
ftp.cwd("httpdocs")

buf = io.BytesIO()
ftp.retrbinary("RETR AbroadWeb.runtimeconfig.json", buf.write)
print("=== AbroadWeb.runtimeconfig.json ===")
print(buf.getvalue().decode("utf-8-sig"))

ftp.quit()
