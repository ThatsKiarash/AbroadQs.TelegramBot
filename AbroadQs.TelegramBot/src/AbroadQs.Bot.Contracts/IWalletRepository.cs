namespace AbroadQs.Bot.Contracts;

public interface IWalletRepository
{
    Task<WalletDto> GetOrCreateWalletAsync(long telegramUserId, CancellationToken ct = default);
    Task<decimal> GetBalanceAsync(long telegramUserId, CancellationToken ct = default);
    Task<WalletDto> CreditAsync(long telegramUserId, decimal amount, string description, string? referenceId = null, CancellationToken ct = default);
    Task<WalletDto> DebitAsync(long telegramUserId, decimal amount, string description, string? referenceId = null, CancellationToken ct = default);
    Task<IReadOnlyList<WalletTransactionDto>> GetTransactionsAsync(long telegramUserId, int page = 0, int pageSize = 20, CancellationToken ct = default);

    // Payments
    Task<PaymentDto> CreatePaymentAsync(PaymentDto payment, CancellationToken ct = default);
    Task<PaymentDto?> GetPaymentByIdGetAsync(long gatewayIdGet, CancellationToken ct = default);
    Task UpdatePaymentStatusAsync(int id, string status, string? gatewayTransactionId = null, CancellationToken ct = default);
    Task<IReadOnlyList<PaymentDto>> GetPaymentsAsync(long telegramUserId, int page = 0, int pageSize = 20, CancellationToken ct = default);
}

public sealed record WalletDto(int Id, long TelegramUserId, decimal Balance, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record WalletTransactionDto(int Id, int WalletId, decimal Amount, string Type, string? Description, string? ReferenceId, DateTimeOffset CreatedAt);
public sealed record PaymentDto(int Id, long TelegramUserId, decimal Amount, string GatewayName, string? GatewayTransactionId, long? GatewayIdGet, string Status, string? Purpose, string? ReferenceId, DateTimeOffset CreatedAt, DateTimeOffset? VerifiedAt);
