namespace AbroadQs.Bot.Contracts;

public interface ITicketRepository
{
    Task<TicketDto> CreateTicketAsync(TicketDto dto, CancellationToken ct = default);
    Task<TicketDto?> GetTicketAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<TicketDto>> ListTicketsAsync(long? userId = null, string? status = null, int page = 0, int pageSize = 10, CancellationToken ct = default);
    Task UpdateTicketStatusAsync(int id, string status, CancellationToken ct = default);

    Task<TicketMessageDto> AddMessageAsync(TicketMessageDto dto, CancellationToken ct = default);
    Task<IReadOnlyList<TicketMessageDto>> GetMessagesAsync(int ticketId, CancellationToken ct = default);
}

public sealed record TicketDto(
    int Id, long TelegramUserId, string Subject, string Status, string Priority,
    string? Category, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, DateTimeOffset? ClosedAt);

public sealed record TicketMessageDto(
    int Id, int TicketId, string SenderType, string? SenderName, string? Text,
    string? FileId, DateTimeOffset CreatedAt);
