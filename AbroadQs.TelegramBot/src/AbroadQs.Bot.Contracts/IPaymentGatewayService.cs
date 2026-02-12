namespace AbroadQs.Bot.Contracts;

/// <summary>
/// Abstraction for payment gateway (BitPay, etc.). Keeps handlers independent of the gateway implementation.
/// </summary>
public interface IPaymentGatewayService
{
    /// <summary>Creates a payment and returns the payment URL. Amount is in Rials.</summary>
    Task<PaymentGatewayResult> CreatePaymentAsync(long telegramUserId, decimal amountRials, string purpose, string? referenceId, string redirectUrl, CancellationToken ct = default);
}

public sealed record PaymentGatewayResult(bool Success, string? PaymentUrl, long? GatewayIdGet, string? Error);
