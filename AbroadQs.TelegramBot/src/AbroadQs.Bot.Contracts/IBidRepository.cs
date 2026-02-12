namespace AbroadQs.Bot.Contracts;

public interface IBidRepository
{
    Task<AdBidDto> CreateBidAsync(AdBidDto bid, CancellationToken ct = default);
    Task<AdBidDto?> GetBidAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<AdBidDto>> GetBidsForRequestAsync(int exchangeRequestId, CancellationToken ct = default);
    Task<int> GetBidCountForRequestAsync(int exchangeRequestId, CancellationToken ct = default);
    Task UpdateBidStatusAsync(int id, string status, CancellationToken ct = default);
    Task SetChannelReplyMessageIdAsync(int id, int channelReplyMessageId, CancellationToken ct = default);
    /// <summary>Phase 4: List bids placed by a specific user (for My Proposals).</summary>
    Task<IReadOnlyList<AdBidDto>> ListBidsByUserAsync(long userId, int page = 0, int pageSize = 10, CancellationToken ct = default);
}

public sealed record AdBidDto(
    int Id,
    int ExchangeRequestId,
    long BidderTelegramUserId,
    string? BidderDisplayName,
    decimal BidAmount,
    decimal BidRate,
    string? Message,
    string Status,
    int? ChannelReplyMessageId,
    DateTimeOffset CreatedAt);
