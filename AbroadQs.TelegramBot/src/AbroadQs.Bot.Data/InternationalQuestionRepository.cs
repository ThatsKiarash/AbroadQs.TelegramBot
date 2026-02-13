using AbroadQs.Bot.Contracts;
using Microsoft.EntityFrameworkCore;

namespace AbroadQs.Bot.Data;

public sealed class InternationalQuestionRepository : IInternationalQuestionRepository
{
    private readonly ApplicationDbContext _db;
    public InternationalQuestionRepository(ApplicationDbContext db) => _db = db;

    public async Task<IntlQuestionDto> CreateAsync(IntlQuestionDto dto, CancellationToken ct = default)
    {
        var entity = new InternationalQuestionEntity
        {
            TelegramUserId = dto.TelegramUserId, QuestionText = dto.QuestionText,
            TargetCountry = dto.TargetCountry, BountyAmount = dto.BountyAmount,
            Status = "open", UserDisplayName = dto.UserDisplayName,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _db.InternationalQuestions.Add(entity);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToDto(entity);
    }

    public async Task<IntlQuestionDto?> GetAsync(int id, CancellationToken ct = default)
    {
        var e = await _db.InternationalQuestions.FindAsync(new object[] { id }, ct).ConfigureAwait(false);
        return e == null ? null : ToDto(e);
    }

    public async Task<IReadOnlyList<IntlQuestionDto>> ListAsync(string? status = null, string? country = null, long? userId = null, int page = 0, int pageSize = 10, CancellationToken ct = default)
    {
        var q = _db.InternationalQuestions.AsQueryable();
        if (!string.IsNullOrEmpty(status)) q = q.Where(x => x.Status == status);
        if (!string.IsNullOrEmpty(country)) q = q.Where(x => x.TargetCountry == country);
        if (userId.HasValue) q = q.Where(x => x.TelegramUserId == userId.Value);
        var items = await q.OrderByDescending(x => x.CreatedAt).Skip(page * pageSize).Take(pageSize).ToListAsync(ct).ConfigureAwait(false);
        return items.Select(ToDto).ToList();
    }

    public async Task UpdateStatusAsync(int id, string status, CancellationToken ct = default)
    {
        var e = await _db.InternationalQuestions.FindAsync(new object[] { id }, ct).ConfigureAwait(false);
        if (e != null) { e.Status = status; e.UpdatedAt = DateTimeOffset.UtcNow; await _db.SaveChangesAsync(ct).ConfigureAwait(false); }
    }

    public async Task<QuestionAnswerDto> CreateAnswerAsync(QuestionAnswerDto dto, CancellationToken ct = default)
    {
        var entity = new QuestionAnswerEntity
        {
            QuestionId = dto.QuestionId, AnswererTelegramUserId = dto.AnswererTelegramUserId,
            AnswerText = dto.AnswerText, Status = "pending", CreatedAt = DateTimeOffset.UtcNow,
        };
        _db.QuestionAnswers.Add(entity);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToAnswerDto(entity);
    }

    public async Task<IReadOnlyList<QuestionAnswerDto>> ListAnswersAsync(int questionId, CancellationToken ct = default)
    {
        var items = await _db.QuestionAnswers.Where(a => a.QuestionId == questionId).OrderByDescending(a => a.CreatedAt).ToListAsync(ct).ConfigureAwait(false);
        return items.Select(ToAnswerDto).ToList();
    }

    public async Task UpdateAnswerStatusAsync(int id, string status, CancellationToken ct = default)
    {
        var e = await _db.QuestionAnswers.FindAsync(new object[] { id }, ct).ConfigureAwait(false);
        if (e != null) { e.Status = status; await _db.SaveChangesAsync(ct).ConfigureAwait(false); }
    }

    public async Task<IReadOnlyList<IntlQuestionWithAnswerCountDto>> ListByUserWithAnswerCountAsync(long userId, int page, int pageSize, CancellationToken ct = default)
    {
        var questions = await _db.InternationalQuestions
            .Where(x => x.TelegramUserId == userId)
            .OrderByDescending(x => x.CreatedAt)
            .Skip(page * pageSize)
            .Take(pageSize)
            .ToListAsync(ct).ConfigureAwait(false);
        if (questions.Count == 0) return Array.Empty<IntlQuestionWithAnswerCountDto>();

        var ids = questions.Select(q => q.Id).ToList();
        var counts = await _db.QuestionAnswers
            .Where(a => ids.Contains(a.QuestionId))
            .GroupBy(a => a.QuestionId)
            .Select(g => new { QuestionId = g.Key, Count = g.Count() })
            .ToListAsync(ct).ConfigureAwait(false);
        var countDict = counts.ToDictionary(x => x.QuestionId, x => x.Count);

        return questions.Select(q => new IntlQuestionWithAnswerCountDto(
            q.Id, q.TelegramUserId, q.QuestionText, q.TargetCountry, q.BountyAmount, q.Status,
            q.UserDisplayName, q.CreatedAt, countDict.GetValueOrDefault(q.Id, 0))).ToList();
    }

    public async Task<IReadOnlyList<UserAnswerWithQuestionDto>> ListAnswersByUserAsync(long userId, int page, int pageSize, CancellationToken ct = default)
    {
        var items = await _db.QuestionAnswers
            .Where(a => a.AnswererTelegramUserId == userId)
            .OrderByDescending(a => a.CreatedAt)
            .Skip(page * pageSize)
            .Take(pageSize)
            .Join(_db.InternationalQuestions, a => a.QuestionId, q => q.Id, (a, q) => new UserAnswerWithQuestionDto(a.Id, a.QuestionId, q.QuestionText, a.AnswerText, a.Status, a.CreatedAt))
            .ToListAsync(ct).ConfigureAwait(false);
        return items;
    }

    private static IntlQuestionDto ToDto(InternationalQuestionEntity e) => new(e.Id, e.TelegramUserId, e.QuestionText, e.TargetCountry, e.BountyAmount, e.Status, e.ChannelMessageId, e.UserDisplayName, e.CreatedAt, e.UpdatedAt);
    private static QuestionAnswerDto ToAnswerDto(QuestionAnswerEntity e) => new(e.Id, e.QuestionId, e.AnswererTelegramUserId, e.AnswerText, e.Status, e.CreatedAt);
}
