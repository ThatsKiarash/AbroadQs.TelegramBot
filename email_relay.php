<?php
/**
 * Email relay script for AbroadQs bot.
 * Upload this to your abroadqs.com web hosting (e.g., public_html/api/email_relay.php)
 * It receives HTTP POST requests and sends emails via PHP mail().
 *
 * Security: Requires a secret token to prevent unauthorized use.
 */

header('Content-Type: application/json');

// ── Configuration ──────────────────────────────────────────────
$SECRET_TOKEN = 'AbroadQs_Email_Relay_2026_Secure';
$FROM_EMAIL   = 'info@abroadqs.com';
$FROM_NAME    = 'AbroadQs';
// ───────────────────────────────────────────────────────────────

// Only accept POST
if ($_SERVER['REQUEST_METHOD'] !== 'POST') {
    http_response_code(405);
    echo json_encode(['ok' => false, 'error' => 'Method not allowed']);
    exit;
}

// Parse input
$input = json_decode(file_get_contents('php://input'), true);
if (!$input) {
    http_response_code(400);
    echo json_encode(['ok' => false, 'error' => 'Invalid JSON']);
    exit;
}

// Verify token
$token = $input['token'] ?? '';
if ($token !== $SECRET_TOKEN) {
    http_response_code(403);
    echo json_encode(['ok' => false, 'error' => 'Unauthorized']);
    exit;
}

$to      = $input['to'] ?? '';
$subject = $input['subject'] ?? '';
$body    = $input['body'] ?? '';

if (empty($to) || empty($subject) || empty($body)) {
    http_response_code(400);
    echo json_encode(['ok' => false, 'error' => 'Missing to, subject, or body']);
    exit;
}

// Validate email
if (!filter_var($to, FILTER_VALIDATE_EMAIL)) {
    http_response_code(400);
    echo json_encode(['ok' => false, 'error' => 'Invalid email address']);
    exit;
}

// Send email
$headers  = "From: {$FROM_NAME} <{$FROM_EMAIL}>\r\n";
$headers .= "Reply-To: {$FROM_EMAIL}\r\n";
$headers .= "MIME-Version: 1.0\r\n";
$headers .= "Content-Type: text/html; charset=UTF-8\r\n";

$result = mail($to, $subject, $body, $headers);

if ($result) {
    echo json_encode(['ok' => true]);
} else {
    http_response_code(500);
    echo json_encode(['ok' => false, 'error' => 'mail() failed']);
}
