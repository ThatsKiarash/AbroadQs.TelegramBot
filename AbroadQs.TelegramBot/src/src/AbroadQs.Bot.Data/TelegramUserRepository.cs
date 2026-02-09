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
                x.CleanChatMode,
                x.PhoneNumber,
                x.IsVerified,
                x.VerificationPhotoFileId,
                x.Email,
                x.EmailVerified,
                x.Country,
                x.KycStatus,
                x.KycRejectionData,
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
            entity.CleanChatMode,
            entity.PhoneNumber,
            entity.IsVerified,
            entity.VerificationPhotoFileId,
            entity.Email,
            entity.EmailVerified,
            entity.Country,
            entity.KycStatus,
            entity.KycRejectionData,
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

    public async Task SetCleanChatModeAsync(long telegramUserId, bool enabled, CancellationToken cancellationToken = default)
    {
        var entity = await _db.TelegramUsers.FirstOrDefaultAsync(x => x.TelegramUserId == telegramUserId, cancellationToken).ConfigureAwait(false);
        if (entity == null) return;
        entity.CleanChatMode = enabled;
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (_processingContext != null)
            _processingContext.SqlAccessed = true;
    }

    public async Task SetPhoneNumberAsync(long telegramUserId, string phoneNumber, CancellationToken cancellationToken = default)
    {
        var entity = await _db.TelegramUsers.FirstOrDefaultAsync(x => x.TelegramUserId == telegramUserId, cancellationToken).ConfigureAwait(false);
        if (entity == null) return;
        entity.PhoneNumber = phoneNumber;
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (_processingContext != null)
            _processingContext.SqlAccessed = true;
    }

    public async Task SetVerifiedAsync(long telegramUserId, string? photoFileId, CancellationToken cancellationToken = default)
    {
        var entity = await _db.TelegramUsers.FirstOrDefaultAsync(x => x.TelegramUserId == telegramUserId, cancellationToken).ConfigureAwait(false);
        if (entity == null) return;
        entity.IsVerified = true;
        if (photoFileId != null) entity.VerificationPhotoFileId = photoFileId;
        entity.IsRegistered = true;
        entity.RegisteredAt = DateTimeOffset.UtcNow;
        entity.KycStatus = "approved";
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (_processingContext != null)
            _processingContext.SqlAccessed = true;
    }

    public async Task SetEmailAsync(long telegramUserId, string email, CancellationToken cancellationToken = default)
    {
        var entity = await _db.TelegramUsers.FirstOrDefaultAsync(x => x.TelegramUserId == telegramUserId, cancellationToken).ConfigureAwait(false);
        if (entity == null) return;
        entity.Email = email;
        entity.EmailVerified = false;
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (_processingContext != null) _processingContext.SqlAccessed = true;
    }

    public async Task SetEmailVerifiedAsync(long telegramUserId, CancellationToken cancellationToken = default)
    {
        var entity = await _db.TelegramUsers.FirstOrDefaultAsync(x => x.TelegramUserId == telegramUserId, cancellationToken).ConfigureAwait(false);
        if (entity == null) return;
        entity.EmailVerified = true;
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (_processingContext != null) _processingContext.SqlAccessed = true;
    }

    public async Task SetCountryAsync(long telegramUserId, string country, CancellationToken cancellationToken = default)
    {
        var entity = await _db.TelegramUsers.FirstOrDefaultAsync(x => x.TelegramUserId == telegramUserId, cancellationToken).ConfigureAwait(false);
        if (entity == null) return;
        entity.Country = country;
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (_processingContext != null) _processingContext.SqlAccessed = true;
    }

    public async Task SetKycStatusAsync(long telegramUserId, string status, string? rejectionData = null, CancellationToken cancellationToken = default)
    {
        var entity = await _db.TelegramUsers.FirstOrDefaultAsync(x => x.TelegramUserId == telegramUserId, cancellationToken).ConfigureAwait(false);
        if (entity == null) return;
        entity.KycStatus = status;
        entity.KycRejectionData = rejectionData;
        if (status == "approved")
        {
            entity.IsVerified = true;
            entity.IsRegistered = true;
            entity.RegisteredAt ??= DateTimeOffset.UtcNow;
        }
        else if (status == "rejected" || status == "pending_review")
        {
            entity.IsVerified = false;
        }
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (_processingContext != null) _processingContext.SqlAccessed = true;
    }
}
