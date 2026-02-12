using AbroadQs.Bot.Contracts;
using Microsoft.EntityFrameworkCore;

namespace AbroadQs.Bot.Data;

public sealed class CryptoWalletRepository : ICryptoWalletRepository
{
    private readonly ApplicationDbContext _db;
    public CryptoWalletRepository(ApplicationDbContext db) => _db = db;

    public async Task<CryptoWalletDto> CreateWalletAsync(CryptoWalletDto dto, CancellationToken ct = default)
    {
        var entity = new CryptoWalletEntity
        {
            TelegramUserId = dto.TelegramUserId,
            CurrencySymbol = dto.CurrencySymbol,
            Network = dto.Network,
            WalletAddress = dto.WalletAddress,
            Label = dto.Label,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _db.CryptoWallets.Add(entity);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToDto(entity);
    }

    public async Task<IReadOnlyList<CryptoWalletDto>> ListWalletsAsync(long telegramUserId, CancellationToken ct = default)
    {
        var items = await _db.CryptoWallets
            .Where(w => w.TelegramUserId == telegramUserId)
            .OrderByDescending(w => w.CreatedAt)
            .ToListAsync(ct).ConfigureAwait(false);
        return items.Select(ToDto).ToList();
    }

    public async Task DeleteWalletAsync(int id, CancellationToken ct = default)
    {
        var entity = await _db.CryptoWallets.FindAsync(new object[] { id }, ct).ConfigureAwait(false);
        if (entity == null) return;
        _db.CryptoWallets.Remove(entity);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<CurrencyPurchaseDto> CreatePurchaseAsync(CurrencyPurchaseDto dto, CancellationToken ct = default)
    {
        var entity = new CurrencyPurchaseEntity
        {
            TelegramUserId = dto.TelegramUserId,
            CurrencySymbol = dto.CurrencySymbol,
            Amount = dto.Amount,
            TotalPrice = dto.TotalPrice,
            Status = "pending",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _db.CurrencyPurchases.Add(entity);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToPurchaseDto(entity);
    }

    public async Task<IReadOnlyList<CurrencyPurchaseDto>> ListPurchasesAsync(long telegramUserId, int page = 0, int pageSize = 10, CancellationToken ct = default)
    {
        var items = await _db.CurrencyPurchases.Where(p => p.TelegramUserId == telegramUserId)
            .OrderByDescending(p => p.CreatedAt).Skip(page * pageSize).Take(pageSize).ToListAsync(ct).ConfigureAwait(false);
        return items.Select(ToPurchaseDto).ToList();
    }

    public async Task UpdatePurchaseStatusAsync(int id, string status, string? txHash = null, CancellationToken ct = default)
    {
        var e = await _db.CurrencyPurchases.FindAsync(new object[] { id }, ct).ConfigureAwait(false);
        if (e == null) return;
        e.Status = status;
        if (txHash != null) e.TxHash = txHash;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static CryptoWalletDto ToDto(CryptoWalletEntity e) =>
        new(e.Id, e.TelegramUserId, e.CurrencySymbol, e.Network, e.WalletAddress, e.Label, e.CreatedAt);

    private static CurrencyPurchaseDto ToPurchaseDto(CurrencyPurchaseEntity e) =>
        new(e.Id, e.TelegramUserId, e.CurrencySymbol, e.Amount, e.TotalPrice, e.Status, e.TxHash, e.CreatedAt);
}
