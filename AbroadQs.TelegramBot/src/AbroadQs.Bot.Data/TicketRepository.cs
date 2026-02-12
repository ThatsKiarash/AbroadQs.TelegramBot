using AbroadQs.Bot.Contracts;
using Microsoft.EntityFrameworkCore;

namespace AbroadQs.Bot.Data;

public sealed class TicketRepository : ITicketRepository
{
    private readonly ApplicationDbContext _db;
    public TicketRepository(ApplicationDbContext db) => _db = db;

    public async Task<TicketDto> CreateTicketAsync(TicketDto dto, CancellationToken ct = default)
    {
        var entity = new TicketEntity
        {
            TelegramUserId = dto.TelegramUserId, Subject = dto.Subject,
            Status = "open", Priority = dto.Priority, Category = dto.Category,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        _db.Tickets.Add(entity);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToDto(entity);
    }

    public async Task<TicketDto?> GetTicketAsync(int id, CancellationToken ct = default)
    {
        var e = await _db.Tickets.FindAsync(new object[] { id }, ct).ConfigureAwait(false);
        return e == null ? null : ToDto(e);
    }

    public async Task<IReadOnlyList<TicketDto>> ListTicketsAsync(long? userId = null, string? status = null, int page = 0, int pageSize = 10, CancellationToken ct = default)
    {
        var q = _db.Tickets.AsQueryable();
        if (userId.HasValue) q = q.Where(t => t.TelegramUserId == userId.Value);
        if (!string.IsNullOrEmpty(status)) q = q.Where(t => t.Status == status);
        var items = await q.OrderByDescending(t => t.UpdatedAt).Skip(page * pageSize).Take(pageSize).ToListAsync(ct).ConfigureAwait(false);
        return items.Select(ToDto).ToList();
    }

    public async Task UpdateTicketStatusAsync(int id, string status, CancellationToken ct = default)
    {
        var e = await _db.Tickets.FindAsync(new object[] { id }, ct).ConfigureAwait(false);
        if (e == null) return;
        e.Status = status; e.UpdatedAt = DateTimeOffset.UtcNow;
        if (status == "closed") e.ClosedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<TicketMessageDto> AddMessageAsync(TicketMessageDto dto, CancellationToken ct = default)
    {
        var entity = new TicketMessageEntity
        {
            TicketId = dto.TicketId, SenderType = dto.SenderType,
            SenderName = dto.SenderName, Text = dto.Text, FileId = dto.FileId,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _db.TicketMessages.Add(entity);
        // Update ticket UpdatedAt
        var ticket = await _db.Tickets.FindAsync(new object[] { dto.TicketId }, ct).ConfigureAwait(false);
        if (ticket != null) ticket.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToMsgDto(entity);
    }

    public async Task<IReadOnlyList<TicketMessageDto>> GetMessagesAsync(int ticketId, CancellationToken ct = default)
    {
        var items = await _db.TicketMessages.Where(m => m.TicketId == ticketId).OrderBy(m => m.CreatedAt).ToListAsync(ct).ConfigureAwait(false);
        return items.Select(ToMsgDto).ToList();
    }

    private static TicketDto ToDto(TicketEntity e) => new(e.Id, e.TelegramUserId, e.Subject, e.Status, e.Priority, e.Category, e.CreatedAt, e.UpdatedAt, e.ClosedAt);
    private static TicketMessageDto ToMsgDto(TicketMessageEntity e) => new(e.Id, e.TicketId, e.SenderType, e.SenderName, e.Text, e.FileId, e.CreatedAt);
}
