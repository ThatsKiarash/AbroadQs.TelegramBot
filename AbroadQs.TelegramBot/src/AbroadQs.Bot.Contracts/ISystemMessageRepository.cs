namespace AbroadQs.Bot.Contracts;

public interface ISystemMessageRepository
{
    Task<SystemMessageDto> CreateAsync(SystemMessageDto dto, CancellationToken ct = default);
    Task<IReadOnlyList<SystemMessageDto>> ListAsync(long telegramUserId, bool unreadOnly = false, int page = 0, int pageSize = 10, CancellationToken ct = default);
    Task<int> UnreadCountAsync(long telegramUserId, CancellationToken ct = default);
    Task MarkAsReadAsync(int id, CancellationToken ct = default);
    Task<SystemMessageDto?> GetAsync(int id, CancellationToken ct = default);
}

public sealed record SystemMessageDto(
    int Id, long TelegramUserId, string? TitleFa, string? TitleEn, string? BodyFa, string? BodyEn,
    string Category, bool IsRead, string? RelatedEntityType, int? RelatedEntityId, DateTimeOffset CreatedAt);
