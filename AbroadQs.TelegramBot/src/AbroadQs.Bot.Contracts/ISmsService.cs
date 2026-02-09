namespace AbroadQs.Bot.Contracts;

/// <summary>
/// Abstraction for sending OTP verification codes via SMS.
/// </summary>
public interface ISmsService
{
    /// <summary>Send a verification OTP code to the given phone number.</summary>
    Task<bool> SendVerificationCodeAsync(string phoneNumber, string code, CancellationToken cancellationToken = default);
}
