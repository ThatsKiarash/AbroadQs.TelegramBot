"""Test the .NET EmailRelay endpoint on Plesk."""
import requests

BASE_URL = "https://abroadqs.com/emailrelay"

# Test 1: GET (health check)
print("Test 1: GET health check...")
try:
    r = requests.get(BASE_URL, timeout=15)
    print(f"  Status: {r.status_code}")
    print(f"  Body: {r.text[:300]}")
except Exception as e:
    print(f"  Error: {e}")

# Test 2: POST with correct token (send test email)
print("\nTest 2: POST - send test email...")
try:
    r = requests.post(BASE_URL, json={
        "token": "AbroadQs_Email_Relay_2026_Secure",
        "to": "info@abroadqs.com",
        "subject": "AbroadQs - Email Relay Test",
        "body": "<h2>Test</h2><p>Email relay is working!</p>"
    }, timeout=20)
    print(f"  Status: {r.status_code}")
    print(f"  Body: {r.text[:500]}")
except Exception as e:
    print(f"  Error: {e}")

# Test 3: POST with wrong token
print("\nTest 3: POST - wrong token (should be 403)...")
try:
    r = requests.post(BASE_URL, json={
        "token": "wrong_token",
        "to": "test@test.com",
        "subject": "Test",
        "body": "Test"
    }, timeout=15)
    print(f"  Status: {r.status_code}")
    print(f"  Body: {r.text[:300]}")
except Exception as e:
    print(f"  Error: {e}")
