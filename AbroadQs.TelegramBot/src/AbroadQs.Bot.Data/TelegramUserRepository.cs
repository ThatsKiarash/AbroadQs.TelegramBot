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
}
