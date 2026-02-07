using AbroadQs.Bot.Contracts;
using Microsoft.EntityFrameworkCore;

namespace AbroadQs.Bot.Data;

public sealed class TelegramUserRepository : ITelegramUserRepository
{
    private readonly ApplicationDbContext _db;
    private readonly IProcessingContext? _processingContext;

    public TelegramUserRepository(ApplicationDbContext db, IProcessingContext? processingContext = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _processingContext = processingContext;
    }

    public async Task SaveOrUpdateAsync(long telegramUserId, string? username, string? firstName, string? lastName, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var entity = await _db.TelegramUsers
            .FirstOrDefaultAsync(x => x.TelegramUserId == telegramUserId, cancellationToken)
            .ConfigureAwait(false);
        if (entity == null)
        {
            entity = new TelegramUserEntity
            {
                TelegramUserId = telegramUserId,
                Username = username,
                FirstName = firstName,
                LastName = lastName,
                FirstSeenAt = now,
                LastSeenAt = now
            };
            _db.TelegramUsers.Add(entity);
        }
        else
        {
            entity.Username = username;
            entity.FirstName = firstName;
            entity.LastName = lastName;
            entity.LastSeenAt = now;
        }
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (_processingContext != null)
            _processingContext.SqlAccessed = true;
    }

    public async Task<IReadOnlyList<AbroadQs.Bot.Contracts.TelegramUserDto>> ListAllAsync(CancellationToken cancellationToken = default)
    {
        var list = await _db.TelegramUsers
            .AsNoTracking()
            .OrderByDescending(x => x.LastSeenAt)
            .Select(x => new AbroadQs.Bot.Contracts.TelegramUserDto(
                x.TelegramUserId,
                x.Username,
                x.FirstName,
                x.LastName,
                x.PreferredLanguage,
                x.IsRegistered,
                x.RegisteredAt,
                x.FirstSeenAt,
                x.LastSeenAt))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return list;
    }

    public async Task<AbroadQs.Bot.Contracts.TelegramUserDto?> GetByTelegramUserIdAsync(long telegramUserId, CancellationToken cancellationToken = default)
    {
        var entity = await _db.TelegramUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.TelegramUserId == telegramUserId, cancellationToken)
            .ConfigureAwait(false);
        if (entity == null) return null;
        return new AbroadQs.Bot.Contracts.TelegramUserDto(
            entity.TelegramUserId,
            entity.Username,
            entity.FirstName,
            entity.LastName,
            entity.PreferredLanguage,
            entity.IsRegistered,
            entity.RegisteredAt,
            entity.FirstSeenAt,
            entity.LastSeenAt);
    }

    public async Task UpdateProfileAsync(long telegramUserId, string? firstName, string? lastName, string? preferredLanguage, CancellationToken cancellationToken = default)
    {
        var entity = await _db.TelegramUsers.FirstOrDefaultAsync(x => x.TelegramUserId == telegramUserId, cancellationToken).ConfigureAwait(false);
        if (entity == null) return;
        if (firstName != null) entity.FirstName = firstName;
        if (lastName != null) entity.LastName = lastName;
        if (preferredLanguage != null) entity.PreferredLanguage = preferredLanguage;
        entity.LastSeenAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (_processingContext != null)
            _processingContext.SqlAccessed = true;
    }

    public async Task MarkAsRegisteredAsync(long telegramUserId, CancellationToken cancellationToken = default)
    {
        var entity = await _db.TelegramUsers.FirstOrDefaultAsync(x => x.TelegramUserId == telegramUserId, cancellationToken).ConfigureAwait(false);
        if (entity == null || entity.IsRegistered) return;
        entity.IsRegistered = true;
        entity.RegisteredAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (_processingContext != null)
            _processingContext.SqlAccessed = true;
    }
}
