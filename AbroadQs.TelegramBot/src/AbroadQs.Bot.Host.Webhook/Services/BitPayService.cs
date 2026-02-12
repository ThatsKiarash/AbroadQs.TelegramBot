using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace AbroadQs.Bot.Host.Webhook.Services;

/// <summary>
/// BitPay.ir payment gateway integration.
/// Production: https://bitpay.ir/payment/gateway-send  |  gateway-result-second
/// Test:       https://bitpay.ir/payment-test/gateway-send  |  gateway-result-second
/// </summary>
public sealed class BitPayService
{
    private readonly HttpClient _http;
    private readonly ILogger<BitPayService> _logger;
    private readonly string _apiKey;
    private readonly bool _testMode;

    private string BaseUrl => _testMode
        ? "https://bitpay.ir/payment-test"
        : "https://bitpay.ir/payment";

    public string PaymentPageUrl(long idGet) => $"{BaseUrl}/gateway-{idGet}-get";

    public BitPayService(HttpClient http, ILogger<BitPayService> logger, string apiKey, bool testMode = false)
    {
        _http = http;
        _logger = logger;
        _apiKey = apiKey;
        _testMode = testMode;
    }

    /// <summary>
    /// Creates a payment request and returns the id_get used to redirect user.
    /// Amount is in Rials.
    /// </summary>
    public async Task<BitPayCreateResult> CreatePaymentAsync(long amountRials, string redirectUrl, string? name = null, string? email = null, string? description = null, string? orderId = null, CancellationToken ct = default)
    {
        var form = new Dictionary<string, string>
        {
            ["api"] = _apiKey,
            ["amount"] = amountRials.ToString(),
            ["redirect"] = redirectUrl,
        };
        if (!string.IsNullOrEmpty(name)) form["name"] = name;
        if (!string.IsNullOrEmpty(email)) form["email"] = email;
        if (!string.IsNullOrEmpty(description)) form["description"] = description;
        if (!string.IsNullOrEmpty(orderId)) form["factorId"] = orderId;

        try
        {
            var response = await _http.PostAsync($"{BaseUrl}/gateway-send", new FormUrlEncodedContent(form), ct).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            _logger.LogInformation("BitPay create response: {Body}", body);

            if (long.TryParse(body.Trim(), out var idGet) && idGet > 0)
            {
                return new BitPayCreateResult(true, idGet, $"{BaseUrl}/gateway-{idGet}-get", null);
            }

            return new BitPayCreateResult(false, 0, null, $"BitPay error: {body.Trim()}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create BitPay payment");
            return new BitPayCreateResult(false, 0, null, ex.Message);
        }
    }

    /// <summary>
    /// Verifies a payment after user returns from the payment page.
    /// Returns transaction details if successful.
    /// </summary>
    public async Task<BitPayVerifyResult> VerifyPaymentAsync(long idGet, string transId, CancellationToken ct = default)
    {
        var form = new Dictionary<string, string>
        {
            ["api"] = _apiKey,
            ["id_get"] = idGet.ToString(),
            ["trans_id"] = transId,
            ["json"] = "1",
        };

        try
        {
            var response = await _http.PostAsync($"{BaseUrl}/gateway-result-second", new FormUrlEncodedContent(form), ct).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            _logger.LogInformation("BitPay verify response: {Body}", body);

            // Try parse as JSON
            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

                if (root.TryGetProperty("status", out var statusProp))
                {
                    var status = statusProp.GetInt32();
                    if (status == 1) // success
                    {
                        var amount = root.TryGetProperty("amount", out var amtProp) ? amtProp.GetInt64() : 0;
                        var factorId = root.TryGetProperty("factorId", out var fProp) ? fProp.GetString() : null;
                        var cardNum = root.TryGetProperty("cardNum", out var cProp) ? cProp.GetString() : null;
                        return new BitPayVerifyResult(true, amount, transId, factorId, cardNum, null);
                    }
                    else
                    {
                        return new BitPayVerifyResult(false, 0, transId, null, null, $"Status: {status}");
                    }
                }
            }
            catch
            {
                // Not JSON — try as plain text error code
            }

            // Plain text response — negative numbers are errors
            if (long.TryParse(body.Trim(), out var code) && code < 0)
            {
                return new BitPayVerifyResult(false, 0, transId, null, null, $"Error code: {code}");
            }

            return new BitPayVerifyResult(false, 0, transId, null, null, $"Unknown response: {body.Trim()}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to verify BitPay payment");
            return new BitPayVerifyResult(false, 0, transId, null, null, ex.Message);
        }
    }
}

public sealed record BitPayCreateResult(bool Success, long IdGet, string? PaymentUrl, string? Error);
public sealed record BitPayVerifyResult(bool Success, long Amount, string TransId, string? FactorId, string? CardNumber, string? Error);
