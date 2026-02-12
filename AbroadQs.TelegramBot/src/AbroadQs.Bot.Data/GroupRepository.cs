using AbroadQs.Bot.Contracts;
using Microsoft.EntityFrameworkCore;

namespace AbroadQs.Bot.Data;

public sealed class GroupRepository : IGroupRepository
{
    private readonly ApplicationDbContext _db;
    public GroupRepository(ApplicationDbContext db) => _db = db;

    public async Task<ExchangeGroupDto> CreateGroupAsync(ExchangeGroupDto dto, CancellationToken ct = default)
    {
        var entity = new ExchangeGroupEntity
        {
            Name = dto.Name,
            TelegramGroupId = dto.TelegramGroupId,
            TelegramGroupLink = dto.TelegramGroupLink,
            GroupType = dto.GroupType,
            CurrencyCode = dto.CurrencyCode,
            CountryCode = dto.CountryCode,
            Description = dto.Description,
            MemberCount = dto.MemberCount,
            SubmittedByUserId = dto.SubmittedByUserId,
            Status = dto.Status,
            IsOfficial = dto.IsOfficial,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _db.ExchangeGroups.Add(entity);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToDto(entity);
    }

    public async Task<ExchangeGroupDto?> GetGroupAsync(int id, CancellationToken ct = default)
    {
        var e = await _db.ExchangeGroups.FindAsync(new object[] { id }, ct).ConfigureAwait(false);
        return e == null ? null : ToDto(e);
    }

    public async Task<IReadOnlyList<ExchangeGroupDto>> ListGroupsAsync(string? status = null, string? groupType = null, string? currencyCode = null, string? countryCode = null, CancellationToken ct = default)
    {
        var q = _db.ExchangeGroups.AsQueryable();
        if (!string.IsNullOrEmpty(status)) q = q.Where(g => g.Status == status);
        if (!string.IsNullOrEmpty(groupType)) q = q.Where(g => g.GroupType == groupType);
        if (!string.IsNullOrEmpty(currencyCode)) q = q.Where(g => g.CurrencyCode == currencyCode);
        if (!string.IsNullOrEmpty(countryCode)) q = q.Where(g => g.CountryCode == countryCode);
        var list = await q.OrderByDescending(g => g.IsOfficial).ThenByDescending(g => g.MemberCount).ToListAsync(ct).ConfigureAwait(false);
        return list.Select(ToDto).ToList();
    }

    public async Task UpdateGroupStatusAsync(int id, string status, string? adminNote = null, CancellationToken ct = default)
    {
        var e = await _db.ExchangeGroups.FindAsync(new object[] { id }, ct).ConfigureAwait(false);
        if (e == null) return;
        e.Status = status;
        if (adminNote != null) e.AdminNote = adminNote;
        e.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task UpdateGroupAsync(int id, string? name = null, string? description = null, bool? isOfficial = null, CancellationToken ct = default)
    {
        var e = await _db.ExchangeGroups.FindAsync(new object[] { id }, ct).ConfigureAwait(false);
        if (e == null) return;
        if (name != null) e.Name = name;
        if (description != null) e.Description = description;
        if (isOfficial.HasValue) e.IsOfficial = isOfficial.Value;
        e.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteGroupAsync(int id, CancellationToken ct = default)
    {
        var e = await _db.ExchangeGroups.FindAsync(new object[] { id }, ct).ConfigureAwait(false);
        if (e == null) return;
        _db.ExchangeGroups.Remove(e);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static ExchangeGroupDto ToDto(ExchangeGroupEntity e) => new(
        e.Id, e.Name, e.TelegramGroupId, e.TelegramGroupLink, e.GroupType,
        e.CurrencyCode, e.CountryCode, e.Description, e.MemberCount,
        e.SubmittedByUserId, e.Status, e.AdminNote, e.IsOfficial, e.CreatedAt, e.UpdatedAt);
}
