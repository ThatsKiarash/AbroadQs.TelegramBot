namespace AbroadQs.Bot.Contracts;

public interface ICryptoWalletRepository
{
    Task<CryptoWalletDto> CreateWalletAsync(CryptoWalletDto dto, CancellationToken ct = default);
    Task<IReadOnlyList<CryptoWalletDto>> ListWalletsAsync(long telegramUserId, CancellationToken ct = default);
    Task DeleteWalletAsync(int id, CancellationToken ct = default);

    Task<CurrencyPurchaseDto> CreatePurchaseAsync(CurrencyPurchaseDto dto, CancellationToken ct = default);
    Task<IReadOnlyList<CurrencyPurchaseDto>> ListPurchasesAsync(long telegramUserId, int page = 0, int pageSize = 10, CancellationToken ct = default);
    Task UpdatePurchaseStatusAsync(int id, string status, string? txHash = null, CancellationToken ct = default);
}

public sealed record CryptoWalletDto(
    int Id, long TelegramUserId, string CurrencySymbol, string Network, string WalletAddress,
    string? Label, DateTimeOffset CreatedAt);

public sealed record CurrencyPurchaseDto(
    int Id, long TelegramUserId, string CurrencySymbol, decimal Amount,
    decimal TotalPrice, string Status, string? TxHash, DateTimeOffset CreatedAt);
