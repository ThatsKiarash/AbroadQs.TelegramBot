namespace AbroadQs.Bot.Contracts;

public interface IGroupRepository
{
    Task<ExchangeGroupDto> CreateGroupAsync(ExchangeGroupDto group, CancellationToken ct = default);
    Task<ExchangeGroupDto?> GetGroupAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<ExchangeGroupDto>> ListGroupsAsync(string? status = null, string? groupType = null, string? currencyCode = null, string? countryCode = null, CancellationToken ct = default);
    Task UpdateGroupStatusAsync(int id, string status, string? adminNote = null, CancellationToken ct = default);
    Task UpdateGroupAsync(int id, string? name = null, string? description = null, bool? isOfficial = null, CancellationToken ct = default);
    Task DeleteGroupAsync(int id, CancellationToken ct = default);
}

public sealed record ExchangeGroupDto(
    int Id,
    string Name,
    long? TelegramGroupId,
    string TelegramGroupLink,
    string GroupType,
    string? CurrencyCode,
    string? CountryCode,
    string? Description,
    int MemberCount,
    long? SubmittedByUserId,
    string Status,
    string? AdminNote,
    bool IsOfficial,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);
