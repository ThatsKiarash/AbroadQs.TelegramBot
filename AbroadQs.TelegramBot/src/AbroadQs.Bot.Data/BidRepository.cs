using AbroadQs.Bot.Contracts;
using Microsoft.EntityFrameworkCore;

namespace AbroadQs.Bot.Data;

public sealed class BidRepository : IBidRepository
{
    private readonly ApplicationDbContext _db;
    public BidRepository(ApplicationDbContext db) => _db = db;

    public async Task<AdBidDto> CreateBidAsync(AdBidDto dto, CancellationToken ct = default)
    {
        var entity = new AdBidEntity
        {
            ExchangeRequestId = dto.ExchangeRequestId,
            BidderTelegramUserId = dto.BidderTelegramUserId,
            BidderDisplayName = dto.BidderDisplayName,
            BidAmount = dto.BidAmount,
            BidRate = dto.BidRate,
            Message = dto.Message,
            Status = dto.Status,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _db.AdBids.Add(entity);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToDto(entity);
    }

    public async Task<AdBidDto?> GetBidAsync(int id, CancellationToken ct = default)
    {
        var e = await _db.AdBids.FindAsync(new object[] { id }, ct).ConfigureAwait(false);
        return e == null ? null : ToDto(e);
    }

    public async Task<IReadOnlyList<AdBidDto>> GetBidsForRequestAsync(int exchangeRequestId, CancellationToken ct = default)
    {
        var list = await _db.AdBids
            .Where(b => b.ExchangeRequestId == exchangeRequestId)
            .OrderByDescending(b => b.CreatedAt)
            .ToListAsync(ct).ConfigureAwait(false);
        return list.Select(ToDto).ToList();
    }

    public async Task<int> GetBidCountForRequestAsync(int exchangeRequestId, CancellationToken ct = default)
    {
        return await _db.AdBids.CountAsync(b => b.ExchangeRequestId == exchangeRequestId, ct).ConfigureAwait(false);
    }

    public async Task UpdateBidStatusAsync(int id, string status, CancellationToken ct = default)
    {
        var e = await _db.AdBids.FindAsync(new object[] { id }, ct).ConfigureAwait(false);
        if (e == null) return;
        e.Status = status;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task SetChannelReplyMessageIdAsync(int id, int channelReplyMessageId, CancellationToken ct = default)
    {
        var e = await _db.AdBids.FindAsync(new object[] { id }, ct).ConfigureAwait(false);
        if (e == null) return;
        e.ChannelReplyMessageId = channelReplyMessageId;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static AdBidDto ToDto(AdBidEntity e) => new(
        e.Id, e.ExchangeRequestId, e.BidderTelegramUserId, e.BidderDisplayName,
        e.BidAmount, e.BidRate, e.Message, e.Status, e.ChannelReplyMessageId, e.CreatedAt);
}
