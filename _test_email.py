"""Test sending email via the updated relay."""
import requests
import time

time.sleep(2)

r = requests.post('https://abroadqs.com/emailrelay', json={
    'token': 'AbroadQs_Email_Relay_2026_Secure',
    'to': 'kiarash.zavare1@gmail.com',
    'subject': 'AbroadQs - Email Verification Code',
    'body': '<h2>AbroadQs</h2><p>Your verification code is: <b>48291</b></p>'
}, timeout=30)

print(f'Status: {r.status_code}')
print(f'Body: {r.text}')
