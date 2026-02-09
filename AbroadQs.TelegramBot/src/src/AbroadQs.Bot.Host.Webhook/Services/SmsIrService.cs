using AbroadQs.Bot.Contracts;
using IPE.SmsIrClient;
using IPE.SmsIrClient.Models.Requests;

namespace AbroadQs.Bot.Host.Webhook.Services;

/// <summary>
/// Sends OTP verification codes via sms.ir using the official SDK.
/// </summary>
public sealed class SmsIrService : ISmsService
{
    private readonly string _apiKey;
    private readonly int _templateId;
    private readonly ILogger<SmsIrService> _logger;

    public SmsIrService(string apiKey, int templateId, ILogger<SmsIrService> logger)
    {
        _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
        _templateId = templateId;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<bool> SendVerificationCodeAsync(string phoneNumber, string code, CancellationToken cancellationToken = default)
    {
        try
        {
            // Normalize phone number: remove + and leading 0
            var mobile = phoneNumber.Replace("+", "").Replace(" ", "");
            if (mobile.StartsWith("98") && mobile.Length > 10)
                mobile = "0" + mobile[2..]; // Convert 98912... to 0912...
            if (!mobile.StartsWith("0"))
                mobile = "0" + mobile;

            _logger.LogInformation("Sending OTP to {Phone} via sms.ir (template {TemplateId})", mobile, _templateId);

            var smsIr = new SmsIr(_apiKey);
            var parameters = new VerifySendParameter[]
            {
                new("CODE", code)
            };

            var response = await smsIr.VerifySendAsync(mobile, _templateId, parameters).ConfigureAwait(false);
            _logger.LogInformation("OTP sent successfully to {Phone}, messageId: {MessageId}", mobile, response?.Data?.MessageId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send OTP to {Phone}", phoneNumber);
            return false;
        }
    }
}
