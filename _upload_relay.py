"""Upload .NET EmailRelay app to Plesk server via FTP."""
import ftplib
import os

FTP_HOST = "abroadqs.com"
FTP_USER = "MyPlesk"
FTP_PASS = "Kia135724!"
PUBLISH_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "EmailRelay", "publish")

print(f"Connecting to {FTP_HOST}...")
ftp = ftplib.FTP(timeout=30)
ftp.connect(FTP_HOST, 21)
ftp.set_pasv(True)
ftp.login(FTP_USER, FTP_PASS)
ftp.cwd("httpdocs")

# Create emailrelay directory
try:
    ftp.mkd("emailrelay")
    print("Created emailrelay/ directory")
except:
    print("emailrelay/ exists")

ftp.cwd("emailrelay")
print(f"In: {ftp.pwd()}")

# Upload all files from publish directory
for filename in os.listdir(PUBLISH_DIR):
    filepath = os.path.join(PUBLISH_DIR, filename)
    if os.path.isfile(filepath):
        size = os.path.getsize(filepath)
        print(f"Uploading {filename} ({size:,} bytes)...")
        with open(filepath, "rb") as f:
            ftp.storbinary(f"STOR {filename}", f)
        print(f"  Done!")

# Verify
print(f"\nFiles in emailrelay/: {ftp.nlst()}")
ftp.quit()
print("\nAll files uploaded!")
print("\nNext step: Configure 'emailrelay' as an IIS sub-application in Plesk")
