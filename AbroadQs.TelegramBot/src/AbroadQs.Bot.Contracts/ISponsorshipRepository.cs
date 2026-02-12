namespace AbroadQs.Bot.Contracts;

public interface ISponsorshipRepository
{
    Task<SponsorshipRequestDto> CreateRequestAsync(SponsorshipRequestDto dto, CancellationToken ct = default);
    Task<SponsorshipRequestDto?> GetRequestAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<SponsorshipRequestDto>> ListRequestsAsync(string? status = null, long? userId = null, int page = 0, int pageSize = 10, CancellationToken ct = default);
    Task UpdateRequestStatusAsync(int id, string status, CancellationToken ct = default);

    Task<SponsorshipDto> CreateSponsorshipAsync(SponsorshipDto dto, CancellationToken ct = default);
    Task<IReadOnlyList<SponsorshipDto>> ListForRequestAsync(int requestId, CancellationToken ct = default);
    Task UpdateSponsorshipStatusAsync(int id, string status, CancellationToken ct = default);
}

public sealed record SponsorshipRequestDto(
    int Id, int? ProjectId, long RequesterTelegramUserId, decimal RequestedAmount,
    decimal ProfitSharePercent, DateTimeOffset? Deadline, string? Description,
    string Status, DateTimeOffset CreatedAt);

public sealed record SponsorshipDto(
    int Id, int RequestId, long SponsorTelegramUserId, decimal Amount,
    string Status, DateTimeOffset CreatedAt);
