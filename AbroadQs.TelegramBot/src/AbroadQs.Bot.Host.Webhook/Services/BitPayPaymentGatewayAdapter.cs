using AbroadQs.Bot.Contracts;

namespace AbroadQs.Bot.Host.Webhook.Services;

/// <summary>
/// Adapts the concrete BitPayService to the IPaymentGatewayService abstraction
/// so that module-layer handlers can request payments without referencing the Host project.
/// </summary>
public sealed class BitPayPaymentGatewayAdapter : IPaymentGatewayService
{
    private readonly BitPayService _bitPay;
    private readonly IWalletRepository _walletRepo;

    public BitPayPaymentGatewayAdapter(BitPayService bitPay, IWalletRepository walletRepo)
    {
        _bitPay = bitPay;
        _walletRepo = walletRepo;
    }

    public async Task<PaymentGatewayResult> CreatePaymentAsync(
        long telegramUserId, decimal amountRials, string purpose, string? referenceId, string redirectUrl, CancellationToken ct = default)
    {
        var result = await _bitPay.CreatePaymentAsync(
            (long)amountRials,
            redirectUrl,
            description: purpose,
            orderId: referenceId,
            ct: ct).ConfigureAwait(false);

        return new PaymentGatewayResult(result.Success, result.PaymentUrl, result.IdGet, result.Error);
    }
}
