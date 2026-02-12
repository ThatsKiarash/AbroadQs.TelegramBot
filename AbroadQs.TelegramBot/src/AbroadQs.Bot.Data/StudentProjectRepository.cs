using AbroadQs.Bot.Contracts;
using Microsoft.EntityFrameworkCore;

namespace AbroadQs.Bot.Data;

public sealed class StudentProjectRepository : IStudentProjectRepository
{
    private readonly ApplicationDbContext _db;
    public StudentProjectRepository(ApplicationDbContext db) => _db = db;

    public async Task<StudentProjectDto> CreateAsync(StudentProjectDto dto, CancellationToken ct = default)
    {
        var entity = new StudentProjectEntity
        {
            TelegramUserId = dto.TelegramUserId, Title = dto.Title, Description = dto.Description,
            Category = dto.Category, Budget = dto.Budget, Currency = dto.Currency,
            Deadline = dto.Deadline, RequiredSkills = dto.RequiredSkills,
            Status = "pending_approval", UserDisplayName = dto.UserDisplayName,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _db.StudentProjects.Add(entity);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToDto(entity);
    }

    public async Task<StudentProjectDto?> GetAsync(int id, CancellationToken ct = default)
    {
        var e = await _db.StudentProjects.FindAsync(new object[] { id }, ct).ConfigureAwait(false);
        return e == null ? null : ToDto(e);
    }

    public async Task<IReadOnlyList<StudentProjectDto>> ListAsync(string? status = null, string? category = null, long? userId = null, int page = 0, int pageSize = 10, CancellationToken ct = default)
    {
        var q = _db.StudentProjects.AsQueryable();
        if (!string.IsNullOrEmpty(status)) q = q.Where(p => p.Status == status);
        if (!string.IsNullOrEmpty(category)) q = q.Where(p => p.Category == category);
        if (userId.HasValue) q = q.Where(p => p.TelegramUserId == userId.Value);
        var items = await q.OrderByDescending(p => p.CreatedAt).Skip(page * pageSize).Take(pageSize).ToListAsync(ct).ConfigureAwait(false);
        return items.Select(ToDto).ToList();
    }

    public async Task UpdateStatusAsync(int id, string status, string? adminNote = null, int? channelMsgId = null, CancellationToken ct = default)
    {
        var e = await _db.StudentProjects.FindAsync(new object[] { id }, ct).ConfigureAwait(false);
        if (e == null) return;
        e.Status = status; e.UpdatedAt = DateTimeOffset.UtcNow;
        if (adminNote != null) e.AdminNote = adminNote;
        if (channelMsgId.HasValue) e.ChannelMessageId = channelMsgId;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task AssignAsync(int id, long assignedToUserId, CancellationToken ct = default)
    {
        var e = await _db.StudentProjects.FindAsync(new object[] { id }, ct).ConfigureAwait(false);
        if (e == null) return;
        e.AssignedToUserId = assignedToUserId; e.Status = "in_progress"; e.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static StudentProjectDto ToDto(StudentProjectEntity e) => new(e.Id, e.TelegramUserId, e.Title, e.Description, e.Category, e.Budget, e.Currency, e.Deadline, e.RequiredSkills, e.Status, e.ChannelMessageId, e.AssignedToUserId, e.AdminNote, e.UserDisplayName, e.CreatedAt, e.UpdatedAt);
}

public sealed class ProjectBidRepository : IProjectBidRepository
{
    private readonly ApplicationDbContext _db;
    public ProjectBidRepository(ApplicationDbContext db) => _db = db;

    public async Task<ProjectBidDto> CreateAsync(ProjectBidDto dto, CancellationToken ct = default)
    {
        var entity = new ProjectBidEntity
        {
            ProjectId = dto.ProjectId, BidderTelegramUserId = dto.BidderTelegramUserId,
            BidderDisplayName = dto.BidderDisplayName, ProposedAmount = dto.ProposedAmount,
            ProposedDuration = dto.ProposedDuration, CoverLetter = dto.CoverLetter,
            PortfolioLink = dto.PortfolioLink, Status = "pending",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _db.ProjectBids.Add(entity);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToDto(entity);
    }

    public async Task<ProjectBidDto?> GetAsync(int id, CancellationToken ct = default)
    {
        var e = await _db.ProjectBids.FindAsync(new object[] { id }, ct).ConfigureAwait(false);
        return e == null ? null : ToDto(e);
    }

    public async Task<IReadOnlyList<ProjectBidDto>> ListForProjectAsync(int projectId, CancellationToken ct = default)
    {
        var items = await _db.ProjectBids.Where(b => b.ProjectId == projectId).OrderByDescending(b => b.CreatedAt).ToListAsync(ct).ConfigureAwait(false);
        return items.Select(ToDto).ToList();
    }

    public async Task<IReadOnlyList<ProjectBidDto>> ListByBidderAsync(long bidderUserId, int page = 0, int pageSize = 10, CancellationToken ct = default)
    {
        var items = await _db.ProjectBids.Where(b => b.BidderTelegramUserId == bidderUserId)
            .OrderByDescending(b => b.CreatedAt).Skip(page * pageSize).Take(pageSize).ToListAsync(ct).ConfigureAwait(false);
        return items.Select(ToDto).ToList();
    }

    public async Task UpdateStatusAsync(int id, string status, CancellationToken ct = default)
    {
        var e = await _db.ProjectBids.FindAsync(new object[] { id }, ct).ConfigureAwait(false);
        if (e != null) { e.Status = status; await _db.SaveChangesAsync(ct).ConfigureAwait(false); }
    }

    private static ProjectBidDto ToDto(ProjectBidEntity e) => new(e.Id, e.ProjectId, e.BidderTelegramUserId, e.BidderDisplayName, e.ProposedAmount, e.ProposedDuration, e.CoverLetter, e.PortfolioLink, e.Status, e.CreatedAt);
}
