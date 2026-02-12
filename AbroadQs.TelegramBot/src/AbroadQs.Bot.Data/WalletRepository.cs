using AbroadQs.Bot.Contracts;
using Microsoft.EntityFrameworkCore;

namespace AbroadQs.Bot.Data;

public sealed class WalletRepository : IWalletRepository
{
    private readonly ApplicationDbContext _db;
    public WalletRepository(ApplicationDbContext db) => _db = db;

    public async Task<WalletDto> GetOrCreateWalletAsync(long telegramUserId, CancellationToken ct = default)
    {
        var wallet = await _db.Wallets.FirstOrDefaultAsync(w => w.TelegramUserId == telegramUserId, ct).ConfigureAwait(false);
        if (wallet == null)
        {
            wallet = new WalletEntity
            {
                TelegramUserId = telegramUserId,
                Balance = 0,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            _db.Wallets.Add(wallet);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        return ToDto(wallet);
    }

    public async Task<decimal> GetBalanceAsync(long telegramUserId, CancellationToken ct = default)
    {
        var wallet = await _db.Wallets.FirstOrDefaultAsync(w => w.TelegramUserId == telegramUserId, ct).ConfigureAwait(false);
        return wallet?.Balance ?? 0;
    }

    public async Task<WalletDto> CreditAsync(long telegramUserId, decimal amount, string description, string? referenceId = null, CancellationToken ct = default)
    {
        var wallet = await EnsureWalletAsync(telegramUserId, ct);
        wallet.Balance += amount;
        wallet.UpdatedAt = DateTimeOffset.UtcNow;

        _db.WalletTransactions.Add(new WalletTransactionEntity
        {
            WalletId = wallet.Id,
            Amount = amount,
            Type = "credit",
            Description = description,
            ReferenceId = referenceId,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToDto(wallet);
    }

    public async Task<WalletDto> DebitAsync(long telegramUserId, decimal amount, string description, string? referenceId = null, CancellationToken ct = default)
    {
        var wallet = await EnsureWalletAsync(telegramUserId, ct);
        wallet.Balance -= amount;
        wallet.UpdatedAt = DateTimeOffset.UtcNow;

        _db.WalletTransactions.Add(new WalletTransactionEntity
        {
            WalletId = wallet.Id,
            Amount = -amount,
            Type = "debit",
            Description = description,
            ReferenceId = referenceId,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToDto(wallet);
    }

    public async Task<IReadOnlyList<WalletTransactionDto>> GetTransactionsAsync(long telegramUserId, int page = 0, int pageSize = 20, CancellationToken ct = default)
    {
        var wallet = await _db.Wallets.FirstOrDefaultAsync(w => w.TelegramUserId == telegramUserId, ct).ConfigureAwait(false);
        if (wallet == null) return Array.Empty<WalletTransactionDto>();

        var txns = await _db.WalletTransactions
            .Where(t => t.WalletId == wallet.Id)
            .OrderByDescending(t => t.CreatedAt)
            .Skip(page * pageSize)
            .Take(pageSize)
            .ToListAsync(ct).ConfigureAwait(false);

        return txns.Select(t => new WalletTransactionDto(t.Id, t.WalletId, t.Amount, t.Type, t.Description, t.ReferenceId, t.CreatedAt)).ToList();
    }

    // ── Payments ──

    public async Task<PaymentDto> CreatePaymentAsync(PaymentDto dto, CancellationToken ct = default)
    {
        var entity = new PaymentEntity
        {
            TelegramUserId = dto.TelegramUserId,
            Amount = dto.Amount,
            GatewayName = dto.GatewayName,
            GatewayTransactionId = dto.GatewayTransactionId,
            GatewayIdGet = dto.GatewayIdGet,
            Status = dto.Status,
            Purpose = dto.Purpose,
            ReferenceId = dto.ReferenceId,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _db.Payments.Add(entity);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToPaymentDto(entity);
    }

    public async Task<PaymentDto?> GetPaymentByIdGetAsync(long gatewayIdGet, CancellationToken ct = default)
    {
        var e = await _db.Payments.FirstOrDefaultAsync(p => p.GatewayIdGet == gatewayIdGet, ct).ConfigureAwait(false);
        return e == null ? null : ToPaymentDto(e);
    }

    public async Task UpdatePaymentStatusAsync(int id, string status, string? gatewayTransactionId = null, CancellationToken ct = default)
    {
        var e = await _db.Payments.FindAsync(new object[] { id }, ct).ConfigureAwait(false);
        if (e == null) return;
        e.Status = status;
        if (gatewayTransactionId != null) e.GatewayTransactionId = gatewayTransactionId;
        if (status == "success") e.VerifiedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    // ── Helpers ──

    private async Task<WalletEntity> EnsureWalletAsync(long telegramUserId, CancellationToken ct)
    {
        var wallet = await _db.Wallets.FirstOrDefaultAsync(w => w.TelegramUserId == telegramUserId, ct).ConfigureAwait(false);
        if (wallet == null)
        {
            wallet = new WalletEntity
            {
                TelegramUserId = telegramUserId,
                Balance = 0,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            _db.Wallets.Add(wallet);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        return wallet;
    }

    private static WalletDto ToDto(WalletEntity e) => new(e.Id, e.TelegramUserId, e.Balance, e.CreatedAt, e.UpdatedAt);
    private static PaymentDto ToPaymentDto(PaymentEntity e) => new(e.Id, e.TelegramUserId, e.Amount, e.GatewayName, e.GatewayTransactionId, e.GatewayIdGet, e.Status, e.Purpose, e.ReferenceId, e.CreatedAt, e.VerifiedAt);
}
