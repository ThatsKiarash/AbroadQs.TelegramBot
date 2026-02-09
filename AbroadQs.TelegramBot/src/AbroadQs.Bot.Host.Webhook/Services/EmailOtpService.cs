using System.Net;
using System.Net.Mail;
using AbroadQs.Bot.Contracts;

namespace AbroadQs.Bot.Host.Webhook.Services;

public sealed class EmailOtpService : IEmailService
{
    private readonly string _smtpHost;
    private readonly int _smtpPort;
    private readonly string _username;
    private readonly string _password;
    private readonly ILogger<EmailOtpService> _logger;

    public EmailOtpService(string smtpHost, int smtpPort, string username, string password, ILogger<EmailOtpService> logger)
    {
        _smtpHost = smtpHost;
        _smtpPort = smtpPort;
        _username = username;
        _password = password;
        _logger = logger;
    }

    public async Task<bool> SendVerificationCodeAsync(string toEmail, string code, CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = new SmtpClient(_smtpHost, _smtpPort)
            {
                Credentials = new NetworkCredential(_username, _password),
                EnableSsl = true,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                Timeout = 15000
            };

            var from = new MailAddress(_username, "AbroadQs");
            var to = new MailAddress(toEmail);
            using var message = new MailMessage(from, to)
            {
                Subject = "AbroadQs - Email Verification Code",
                IsBodyHtml = true,
                Body = $@"
<div style='font-family: Arial, sans-serif; max-width: 480px; margin: 0 auto; padding: 20px;'>
    <h2 style='color: #2563eb; text-align: center;'>AbroadQs</h2>
    <p style='text-align: center; font-size: 16px;'>Your email verification code is:</p>
    <div style='text-align: center; margin: 24px 0;'>
        <span style='font-size: 32px; font-weight: bold; letter-spacing: 8px; color: #1e293b; background: #f1f5f9; padding: 12px 24px; border-radius: 8px;'>{code}</span>
    </div>
    <p style='text-align: center; color: #64748b; font-size: 14px;'>This code will expire in 5 minutes.</p>
    <p style='text-align: center; color: #94a3b8; font-size: 12px; margin-top: 24px;'>If you did not request this, please ignore this email.</p>
</div>"
            };

            await client.SendMailAsync(message, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Email OTP sent to {Email}", toEmail);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email OTP to {Email}", toEmail);
            return false;
        }
    }
}
