"""Upload updated EmailRelay using app_offline.htm trick to release locks."""
import ftplib, os, io, time

PUBLISH_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "EmailRelay", "publish")

ftp = ftplib.FTP(timeout=30)
ftp.connect("abroadqs.com", 21)
ftp.set_pasv(True)
ftp.login("MyPlesk", "Kia135724!")
ftp.cwd("httpdocs/emailrelay")
print(f"In: {ftp.pwd()}")

# Step 1: Upload app_offline.htm to stop the app and release file locks
print("Step 1: Taking app offline...")
offline_html = b"<html><body>Updating email relay, please wait...</body></html>"
ftp.storbinary("STOR app_offline.htm", io.BytesIO(offline_html))
print("  app_offline.htm uploaded - IIS will stop the app")

# Wait a moment for IIS to release the files
time.sleep(3)

# Step 2: Upload updated files
print("Step 2: Uploading updated files...")
for filename in ["EmailRelay.dll", "EmailRelay.exe", "EmailRelay.pdb"]:
    filepath = os.path.join(PUBLISH_DIR, filename)
    size = os.path.getsize(filepath)
    print(f"  Uploading {filename} ({size:,} bytes)...")
    with open(filepath, "rb") as f:
        ftp.storbinary(f"STOR {filename}", f)

print("Step 3: Bringing app back online...")
ftp.delete("app_offline.htm")
print("  app_offline.htm deleted - IIS will restart the app")

ftp.quit()
print("\nDone! EmailRelay updated and restarted.")
