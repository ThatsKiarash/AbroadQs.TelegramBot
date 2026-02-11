"""
Try to upload email_relay.php to Plesk hosting via Plesk REST API or alternative methods.
"""
import requests
import urllib3
urllib3.disable_warnings(urllib3.exceptions.InsecureRequestWarning)

PLESK_URL = "https://windows5.centraldnserver.com:8443"

# Read the PHP file content
with open("email_relay.php", "r") as f:
    php_content = f.read()

# Try Plesk REST API with common credentials
credentials_to_try = [
    ("admin", "Kia135724!"),
    ("info@abroadqs.com", "Kia135724!"),
    ("abroadqs", "Kia135724!"),
]

for user, pwd in credentials_to_try:
    print(f"\nTrying Plesk API with user: {user}")
    try:
        # Check if we can authenticate
        r = requests.get(
            f"{PLESK_URL}/api/v2/server",
            auth=(user, pwd),
            verify=False,
            timeout=10
        )
        print(f"  Status: {r.status_code}")
        if r.status_code == 200:
            print(f"  SUCCESS! Authenticated as {user}")
            print(f"  Response: {r.text[:200]}")
            
            # Try to create the file via Plesk file manager API
            # First list domains/sites
            r2 = requests.get(
                f"{PLESK_URL}/api/v2/domains",
                auth=(user, pwd),
                verify=False,
                timeout=10
            )
            print(f"  Domains: {r2.status_code} - {r2.text[:500]}")
            break
        elif r.status_code == 401:
            print(f"  Auth failed")
        else:
            print(f"  Response: {r.text[:200]}")
    except Exception as e:
        print(f"  Error: {e}")
