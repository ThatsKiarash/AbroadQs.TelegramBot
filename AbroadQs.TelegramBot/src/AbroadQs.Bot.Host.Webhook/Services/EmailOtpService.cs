using System.Net.Http.Json;
using AbroadQs.Bot.Contracts;

namespace AbroadQs.Bot.Host.Webhook.Services;

/// <summary>
/// Sends email OTP via an HTTP relay endpoint hosted on abroadqs.com.
/// This bypasses Hetzner's outgoing SMTP port block by using HTTPS instead.
/// Fallback: if relay URL is not configured, attempts direct SMTP via MailKit.
/// </summary>
public sealed class EmailOtpService : IEmailService
{
    private readonly string? _relayUrl;
    private readonly string? _relayToken;
    private readonly string? _smtpHost;
    private readonly int _smtpPort;
    private readonly string? _username;
    private readonly string? _password;
    private readonly ILogger<EmailOtpService> _logger;
    private readonly IHttpClientFactory? _httpClientFactory;

    /// <summary>
    /// Primary constructor — HTTP relay mode (recommended).
    /// </summary>
    public EmailOtpService(
        string relayUrl,
        string relayToken,
        ILogger<EmailOtpService> logger,
        IHttpClientFactory? httpClientFactory = null)
    {
        _relayUrl = relayUrl;
        _relayToken = relayToken;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>
    /// Fallback constructor — direct SMTP via MailKit.
    /// </summary>
    public EmailOtpService(
        string smtpHost, int smtpPort, string username, string password,
        ILogger<EmailOtpService> logger,
        string? relayUrl = null, string? relayToken = null,
        IHttpClientFactory? httpClientFactory = null)
    {
        _smtpHost = smtpHost;
        _smtpPort = smtpPort;
        _username = username;
        _password = password;
        _logger = logger;
        _relayUrl = relayUrl;
        _relayToken = relayToken;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<bool> SendVerificationCodeAsync(string toEmail, string code, CancellationToken cancellationToken = default)
    {
        // Hard timeout — the bot must NEVER hang
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(15));
        var ct = cts.Token;

        var htmlBody = BuildHtmlBody(code);

        // Try HTTP relay first (bypasses SMTP port block)
        if (!string.IsNullOrEmpty(_relayUrl) && !string.IsNullOrEmpty(_relayToken))
        {
            try
            {
                var result = await SendViaRelayAsync(toEmail, htmlBody, ct);
                if (result) return true;
                _logger.LogWarning("HTTP relay failed for {Email}, will try SMTP fallback", toEmail);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("HTTP relay timed out for {Email}", toEmail);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "HTTP relay error for {Email}, will try SMTP fallback", toEmail);
            }
        }

        // Fallback: direct SMTP via MailKit
        if (!string.IsNullOrEmpty(_smtpHost) && !string.IsNullOrEmpty(_username))
        {
            try
            {
                return await SendViaSmtpAsync(toEmail, htmlBody, ct);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("SMTP timed out for {Email}", toEmail);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SMTP failed for {Email}", toEmail);
                return false;
            }
        }

        _logger.LogError("No email transport configured — cannot send OTP to {Email}", toEmail);
        return false;
    }

    // ═══════════════════════════════════════════════════════════════
    //  HTTP Relay
    // ═══════════════════════════════════════════════════════════════

    private async Task<bool> SendViaRelayAsync(string toEmail, string htmlBody, CancellationToken ct)
    {
        var client = _httpClientFactory?.CreateClient("EmailRelay") ?? new HttpClient();
        client.Timeout = TimeSpan.FromSeconds(12);

        var payload = new
        {
            token = _relayToken,
            to = toEmail,
            subject = "AbroadQs - Email Verification Code",
            body = htmlBody
        };

        var response = await client.PostAsJsonAsync(_relayUrl, payload, ct).ConfigureAwait(false);

        if (response.IsSuccessStatusCode)
        {
            _logger.LogInformation("Email OTP sent via relay to {Email}", toEmail);
            return true;
        }

        var errorBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        _logger.LogWarning("Email relay returned {Status}: {Body}", response.StatusCode, errorBody);
        return false;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Direct SMTP via MailKit
    // ═══════════════════════════════════════════════════════════════

    private async Task<bool> SendViaSmtpAsync(string toEmail, string htmlBody, CancellationToken ct)
    {
        using var message = new MimeKit.MimeMessage();
        message.From.Add(new MimeKit.MailboxAddress("AbroadQs", _username));
        message.To.Add(MimeKit.MailboxAddress.Parse(toEmail));
        message.Subject = "AbroadQs - Email Verification Code";
        message.Body = new MimeKit.BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();

        using var smtp = new MailKit.Net.Smtp.SmtpClient();
        smtp.Timeout = 10000;

        var secureOption = _smtpPort == 465
            ? MailKit.Security.SecureSocketOptions.SslOnConnect
            : MailKit.Security.SecureSocketOptions.StartTls;

        await smtp.ConnectAsync(_smtpHost, _smtpPort, secureOption, ct).ConfigureAwait(false);
        await smtp.AuthenticateAsync(_username, _password, ct).ConfigureAwait(false);
        await smtp.SendAsync(message, ct).ConfigureAwait(false);
        await smtp.DisconnectAsync(true, ct).ConfigureAwait(false);

        _logger.LogInformation("Email OTP sent via SMTP to {Email}", toEmail);
        return true;
    }

    // ═══════════════════════════════════════════════════════════════
    //  HTML template
    // ═══════════════════════════════════════════════════════════════

    private static string BuildHtmlBody(string code) => $@"
<div style='font-family: Arial, sans-serif; max-width: 480px; margin: 0 auto; padding: 20px;'>
    <h2 style='color: #2563eb; text-align: center;'>AbroadQs</h2>
    <p style='text-align: center; font-size: 16px;'>Your email verification code is:</p>
    <div style='text-align: center; margin: 24px 0;'>
        <span style='font-size: 32px; font-weight: bold; letter-spacing: 8px; color: #1e293b; background: #f1f5f9; padding: 12px 24px; border-radius: 8px;'>{code}</span>
    </div>
    <p style='text-align: center; color: #64748b; font-size: 14px;'>This code will expire in 5 minutes.</p>
    <p style='text-align: center; color: #94a3b8; font-size: 12px; margin-top: 24px;'>If you did not request this, please ignore this email.</p>
</div>";
}
