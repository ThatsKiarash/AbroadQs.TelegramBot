namespace AbroadQs.Bot.Contracts;

/// <summary>
/// Abstraction for sending OTP verification codes via Email.
/// </summary>
public interface IEmailService
{
    /// <summary>Send a verification OTP code to the given email address.</summary>
    Task<bool> SendVerificationCodeAsync(string toEmail, string code, CancellationToken cancellationToken = default);
}
