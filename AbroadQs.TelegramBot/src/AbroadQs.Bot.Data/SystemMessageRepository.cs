using AbroadQs.Bot.Contracts;
using Microsoft.EntityFrameworkCore;

namespace AbroadQs.Bot.Data;

public sealed class SystemMessageRepository : ISystemMessageRepository
{
    private readonly ApplicationDbContext _db;
    public SystemMessageRepository(ApplicationDbContext db) => _db = db;

    public async Task<SystemMessageDto> CreateAsync(SystemMessageDto dto, CancellationToken ct = default)
    {
        var entity = new SystemMessageEntity
        {
            TelegramUserId = dto.TelegramUserId,
            TitleFa = dto.TitleFa, TitleEn = dto.TitleEn,
            BodyFa = dto.BodyFa, BodyEn = dto.BodyEn,
            Category = dto.Category, IsRead = false,
            RelatedEntityType = dto.RelatedEntityType, RelatedEntityId = dto.RelatedEntityId,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _db.SystemMessages.Add(entity);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToDto(entity);
    }

    public async Task<IReadOnlyList<SystemMessageDto>> ListAsync(long telegramUserId, bool unreadOnly = false, int page = 0, int pageSize = 10, CancellationToken ct = default)
    {
        var q = _db.SystemMessages.Where(m => m.TelegramUserId == telegramUserId);
        if (unreadOnly) q = q.Where(m => !m.IsRead);
        var items = await q.OrderByDescending(m => m.CreatedAt).Skip(page * pageSize).Take(pageSize).ToListAsync(ct).ConfigureAwait(false);
        return items.Select(ToDto).ToList();
    }

    public async Task<int> UnreadCountAsync(long telegramUserId, CancellationToken ct = default)
        => await _db.SystemMessages.CountAsync(m => m.TelegramUserId == telegramUserId && !m.IsRead, ct).ConfigureAwait(false);

    public async Task MarkAsReadAsync(int id, CancellationToken ct = default)
    {
        var e = await _db.SystemMessages.FindAsync(new object[] { id }, ct).ConfigureAwait(false);
        if (e != null) { e.IsRead = true; await _db.SaveChangesAsync(ct).ConfigureAwait(false); }
    }

    public async Task<SystemMessageDto?> GetAsync(int id, CancellationToken ct = default)
    {
        var e = await _db.SystemMessages.FindAsync(new object[] { id }, ct).ConfigureAwait(false);
        return e == null ? null : ToDto(e);
    }

    private static SystemMessageDto ToDto(SystemMessageEntity e) => new(e.Id, e.TelegramUserId, e.TitleFa, e.TitleEn, e.BodyFa, e.BodyEn, e.Category, e.IsRead, e.RelatedEntityType, e.RelatedEntityId, e.CreatedAt);
}
