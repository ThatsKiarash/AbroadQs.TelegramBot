using AbroadQs.Bot.Contracts;
using Microsoft.EntityFrameworkCore;

namespace AbroadQs.Bot.Data;

public sealed class ExchangeRepository : IExchangeRepository
{
    private readonly ApplicationDbContext _db;

    public ExchangeRepository(ApplicationDbContext db) => _db = db;

    // ── Requests ─────────────────────────────────────────────────────

    public async Task<int> GetNextRequestNumberAsync(CancellationToken ct = default)
    {
        var max = await _db.ExchangeRequests
            .MaxAsync(r => (int?)r.RequestNumber, ct)
            .ConfigureAwait(false);
        return (max ?? 0) + 1;
    }

    public async Task<ExchangeRequestDto> CreateRequestAsync(ExchangeRequestDto dto, CancellationToken ct = default)
    {
        var entity = new ExchangeRequestEntity
        {
            RequestNumber = dto.RequestNumber,
            TelegramUserId = dto.TelegramUserId,
            Currency = dto.Currency,
            TransactionType = dto.TransactionType,
            DeliveryMethod = dto.DeliveryMethod,
            AccountType = dto.AccountType,
            Country = dto.Country,
            Amount = dto.Amount,
            ProposedRate = dto.ProposedRate,
            Description = dto.Description,
            FeePercent = dto.FeePercent,
            FeeAmount = dto.FeeAmount,
            TotalAmount = dto.TotalAmount,
            Status = dto.Status,
            UserDisplayName = dto.UserDisplayName,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _db.ExchangeRequests.Add(entity);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToDto(entity);
    }

    public async Task<ExchangeRequestDto?> GetRequestAsync(int id, CancellationToken ct = default)
    {
        var e = await _db.ExchangeRequests.FindAsync(new object[] { id }, ct).ConfigureAwait(false);
        return e == null ? null : ToDto(e);
    }

    public async Task<ExchangeRequestDto?> GetRequestByNumberAsync(int number, CancellationToken ct = default)
    {
        var e = await _db.ExchangeRequests
            .FirstOrDefaultAsync(r => r.RequestNumber == number, ct)
            .ConfigureAwait(false);
        return e == null ? null : ToDto(e);
    }

    public async Task<IReadOnlyList<ExchangeRequestDto>> ListRequestsAsync(string? status = null, long? userId = null, CancellationToken ct = default)
    {
        var q = _db.ExchangeRequests.AsQueryable();
        if (!string.IsNullOrEmpty(status))
            q = q.Where(r => r.Status == status);
        if (userId.HasValue)
            q = q.Where(r => r.TelegramUserId == userId.Value);
        var list = await q.OrderByDescending(r => r.CreatedAt).ToListAsync(ct).ConfigureAwait(false);
        return list.Select(ToDto).ToList();
    }

    public async Task UpdateStatusAsync(int id, string status, string? adminNote = null, int? channelMsgId = null, CancellationToken ct = default)
    {
        var e = await _db.ExchangeRequests.FindAsync(new object[] { id }, ct).ConfigureAwait(false);
        if (e == null) return;
        e.Status = status;
        e.UpdatedAt = DateTimeOffset.UtcNow;
        if (adminNote != null) e.AdminNote = adminNote;
        if (channelMsgId.HasValue) e.ChannelMessageId = channelMsgId;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    // ── Rates ────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<ExchangeRateDto>> GetRatesAsync(CancellationToken ct = default)
    {
        var list = await _db.ExchangeRates.OrderBy(r => r.CurrencyCode).ToListAsync(ct).ConfigureAwait(false);
        return list.Select(ToDto).ToList();
    }

    public async Task<ExchangeRateDto?> GetRateAsync(string currencyCode, CancellationToken ct = default)
    {
        var e = await _db.ExchangeRates
            .FirstOrDefaultAsync(r => r.CurrencyCode == currencyCode, ct)
            .ConfigureAwait(false);
        return e == null ? null : ToDto(e);
    }

    public async Task SaveRatesAsync(IEnumerable<ExchangeRateDto> rates, CancellationToken ct = default)
    {
        foreach (var dto in rates)
            await SaveRateAsync(dto, ct).ConfigureAwait(false);
    }

    public async Task SaveRateAsync(ExchangeRateDto dto, CancellationToken ct = default)
    {
        var existing = await _db.ExchangeRates
            .FirstOrDefaultAsync(r => r.CurrencyCode == dto.CurrencyCode, ct)
            .ConfigureAwait(false);

        if (existing != null)
        {
            existing.Rate = dto.Rate;
            existing.Change = dto.Change;
            existing.Source = dto.Source;
            existing.CurrencyNameFa = dto.CurrencyNameFa ?? existing.CurrencyNameFa;
            existing.CurrencyNameEn = dto.CurrencyNameEn ?? existing.CurrencyNameEn;
            existing.LastUpdatedAt = DateTimeOffset.UtcNow;
        }
        else
        {
            _db.ExchangeRates.Add(new ExchangeRateEntity
            {
                CurrencyCode = dto.CurrencyCode,
                CurrencyNameFa = dto.CurrencyNameFa,
                CurrencyNameEn = dto.CurrencyNameEn,
                Rate = dto.Rate,
                Change = dto.Change,
                Source = dto.Source,
                LastUpdatedAt = DateTimeOffset.UtcNow,
            });
        }
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    // ── Mapping ──────────────────────────────────────────────────────

    private static ExchangeRequestDto ToDto(ExchangeRequestEntity e) => new(
        e.Id, e.RequestNumber, e.TelegramUserId, e.Currency, e.TransactionType,
        e.DeliveryMethod, e.AccountType, e.Country, e.Amount, e.ProposedRate,
        e.Description, e.FeePercent, e.FeeAmount, e.TotalAmount, e.Status,
        e.ChannelMessageId, e.AdminNote, e.UserDisplayName, e.CreatedAt, e.UpdatedAt);

    private static ExchangeRateDto ToDto(ExchangeRateEntity e) => new(
        e.Id, e.CurrencyCode, e.CurrencyNameFa, e.CurrencyNameEn,
        e.Rate, e.Change, e.Source, e.LastUpdatedAt);
}
