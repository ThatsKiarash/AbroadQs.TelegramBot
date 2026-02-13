using AbroadQs.Bot.Contracts;

namespace AbroadQs.Bot.Host.Webhook.Services;

/// <summary>
/// Adapts the concrete BitPayService to the IPaymentGatewayService abstraction
/// so that module-layer handlers can request payments without referencing the Host project.
/// Also creates a payment record in the database so the callback can find and verify it.
/// </summary>
public sealed class BitPayPaymentGatewayAdapter : IPaymentGatewayService
{
    private readonly BitPayService _bitPay;
    private readonly IWalletRepository _walletRepo;
    private readonly ISettingsRepository? _settingsRepo;

    public BitPayPaymentGatewayAdapter(BitPayService bitPay, IWalletRepository walletRepo, ISettingsRepository? settingsRepo = null)
    {
        _bitPay = bitPay;
        _walletRepo = walletRepo;
        _settingsRepo = settingsRepo;
    }

    public async Task<PaymentGatewayResult> CreatePaymentAsync(
        long telegramUserId, decimal amountRials, string purpose, string? referenceId, string redirectUrl, CancellationToken ct = default)
    {
        // Build full redirect URL from base_url setting if redirectUrl is relative
        if (redirectUrl.StartsWith("/"))
        {
            var baseUrl = _settingsRepo != null
                ? await _settingsRepo.GetValueAsync("base_url", ct).ConfigureAwait(false)
                : null;
            if (string.IsNullOrEmpty(baseUrl))
            {
                // Fallback: try webhook_url setting and extract origin
                var webhookUrl = _settingsRepo != null
                    ? await _settingsRepo.GetValueAsync("webhook_url", ct).ConfigureAwait(false)
                    : null;
                if (!string.IsNullOrEmpty(webhookUrl))
                {
                    // Extract base: "https://webhook.abroadqs.com/webhook" → "https://webhook.abroadqs.com"
                    if (Uri.TryCreate(webhookUrl, UriKind.Absolute, out var uri))
                        baseUrl = $"{uri.Scheme}://{uri.Host}{(uri.Port != 80 && uri.Port != 443 ? $":{uri.Port}" : "")}";
                    else
                        baseUrl = webhookUrl.TrimEnd('/');
                }
            }
            if (!string.IsNullOrEmpty(baseUrl))
            {
                baseUrl = baseUrl.TrimEnd('/');
                redirectUrl = baseUrl + redirectUrl;
            }
        }

        var result = await _bitPay.CreatePaymentAsync(
            (long)amountRials,
            redirectUrl,
            description: purpose,
            orderId: referenceId,
            ct: ct).ConfigureAwait(false);

        if (!result.Success)
            return new PaymentGatewayResult(false, null, null, result.Error);

        // Create payment record in DB so the callback endpoint can find it by IdGet
        try
        {
            var payment = new PaymentDto(
                Id: 0,
                TelegramUserId: telegramUserId,
                Amount: amountRials,
                GatewayName: "bitpay",
                GatewayTransactionId: null,
                GatewayIdGet: result.IdGet,
                Status: "pending",
                Purpose: purpose,
                ReferenceId: referenceId,
                CreatedAt: DateTimeOffset.UtcNow,
                VerifiedAt: null
            );
            await _walletRepo.CreatePaymentAsync(payment, ct).ConfigureAwait(false);
        }
        catch { /* log but don't fail — payment link was already created */ }

        return new PaymentGatewayResult(true, result.PaymentUrl, result.IdGet, null);
    }
}
