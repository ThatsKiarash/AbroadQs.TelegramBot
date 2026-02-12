using AbroadQs.Bot.Contracts;
using Microsoft.EntityFrameworkCore;

namespace AbroadQs.Bot.Data;

public sealed class SponsorshipRepository : ISponsorshipRepository
{
    private readonly ApplicationDbContext _db;
    public SponsorshipRepository(ApplicationDbContext db) => _db = db;

    public async Task<SponsorshipRequestDto> CreateRequestAsync(SponsorshipRequestDto dto, CancellationToken ct = default)
    {
        var entity = new SponsorshipRequestEntity
        {
            ProjectId = dto.ProjectId, RequesterTelegramUserId = dto.RequesterTelegramUserId,
            RequestedAmount = dto.RequestedAmount, ProfitSharePercent = dto.ProfitSharePercent,
            Deadline = dto.Deadline, Description = dto.Description, Status = "open",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _db.SponsorshipRequests.Add(entity);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToReqDto(entity);
    }

    public async Task<SponsorshipRequestDto?> GetRequestAsync(int id, CancellationToken ct = default)
    {
        var e = await _db.SponsorshipRequests.FindAsync(new object[] { id }, ct).ConfigureAwait(false);
        return e == null ? null : ToReqDto(e);
    }

    public async Task<IReadOnlyList<SponsorshipRequestDto>> ListRequestsAsync(string? status = null, long? userId = null, int page = 0, int pageSize = 10, CancellationToken ct = default)
    {
        var q = _db.SponsorshipRequests.AsQueryable();
        if (!string.IsNullOrEmpty(status)) q = q.Where(x => x.Status == status);
        if (userId.HasValue) q = q.Where(x => x.RequesterTelegramUserId == userId.Value);
        var items = await q.OrderByDescending(x => x.CreatedAt).Skip(page * pageSize).Take(pageSize).ToListAsync(ct).ConfigureAwait(false);
        return items.Select(ToReqDto).ToList();
    }

    public async Task UpdateRequestStatusAsync(int id, string status, CancellationToken ct = default)
    {
        var e = await _db.SponsorshipRequests.FindAsync(new object[] { id }, ct).ConfigureAwait(false);
        if (e != null) { e.Status = status; await _db.SaveChangesAsync(ct).ConfigureAwait(false); }
    }

    public async Task<SponsorshipDto> CreateSponsorshipAsync(SponsorshipDto dto, CancellationToken ct = default)
    {
        var entity = new SponsorshipEntity
        {
            RequestId = dto.RequestId, SponsorTelegramUserId = dto.SponsorTelegramUserId,
            Amount = dto.Amount, Status = "active", CreatedAt = DateTimeOffset.UtcNow,
        };
        _db.Sponsorships.Add(entity);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToDto(entity);
    }

    public async Task<IReadOnlyList<SponsorshipDto>> ListForRequestAsync(int requestId, CancellationToken ct = default)
    {
        var items = await _db.Sponsorships.Where(s => s.RequestId == requestId).OrderByDescending(s => s.CreatedAt).ToListAsync(ct).ConfigureAwait(false);
        return items.Select(ToDto).ToList();
    }

    public async Task UpdateSponsorshipStatusAsync(int id, string status, CancellationToken ct = default)
    {
        var e = await _db.Sponsorships.FindAsync(new object[] { id }, ct).ConfigureAwait(false);
        if (e != null) { e.Status = status; await _db.SaveChangesAsync(ct).ConfigureAwait(false); }
    }

    private static SponsorshipRequestDto ToReqDto(SponsorshipRequestEntity e) => new(e.Id, e.ProjectId, e.RequesterTelegramUserId, e.RequestedAmount, e.ProfitSharePercent, e.Deadline, e.Description, e.Status, e.CreatedAt);
    private static SponsorshipDto ToDto(SponsorshipEntity e) => new(e.Id, e.RequestId, e.SponsorTelegramUserId, e.Amount, e.Status, e.CreatedAt);
}
