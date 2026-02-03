using AbroadQs.Bot.Contracts;
using Microsoft.EntityFrameworkCore;

namespace AbroadQs.Bot.Data;

public sealed class UserMessageStateRepository : IUserMessageStateRepository
{
    private readonly ApplicationDbContext _db;
    private readonly IProcessingContext? _processingContext;

    public UserMessageStateRepository(ApplicationDbContext db, IProcessingContext? processingContext = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _processingContext = processingContext;
    }

    public async Task SetLastBotMessageAsync(long telegramUserId, int messageId, long telegramMessageId, CancellationToken cancellationToken = default)
    {
        var entity = await _db.UserMessageStates
            .FirstOrDefaultAsync(x => x.TelegramUserId == telegramUserId, cancellationToken)
            .ConfigureAwait(false);

        if (entity == null)
        {
            entity = new UserMessageStateEntity
            {
                TelegramUserId = telegramUserId,
                LastBotMessageId = messageId,
                LastBotTelegramMessageId = telegramMessageId,
                LastAction = "Sent",
                LastActionAt = DateTimeOffset.UtcNow
            };
            _db.UserMessageStates.Add(entity);
        }
        else
        {
            entity.LastBotMessageId = messageId;
            entity.LastBotTelegramMessageId = telegramMessageId;
            entity.LastAction = "Sent";
            entity.LastActionAt = DateTimeOffset.UtcNow;
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (_processingContext != null)
            _processingContext.SqlAccessed = true;
    }

    public async Task<UserMessageStateDto?> GetUserMessageStateAsync(long telegramUserId, CancellationToken cancellationToken = default)
    {
        var entity = await _db.UserMessageStates
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.TelegramUserId == telegramUserId, cancellationToken)
            .ConfigureAwait(false);

        if (entity == null) return null;

        return new UserMessageStateDto(
            entity.TelegramUserId,
            entity.LastBotMessageId,
            entity.LastBotTelegramMessageId,
            entity.ShouldEdit,
            entity.ShouldReply,
            entity.ShouldKeepStatic,
            entity.DeleteNextMessages,
            entity.LastAction,
            entity.LastActionAt);
    }

    public async Task UpdateUserMessageStateAsync(long telegramUserId, Action<UserMessageStateUpdateInfo> updateAction, CancellationToken cancellationToken = default)
    {
        var entity = await _db.UserMessageStates
            .FirstOrDefaultAsync(x => x.TelegramUserId == telegramUserId, cancellationToken)
            .ConfigureAwait(false);

        if (entity == null)
        {
            entity = new UserMessageStateEntity { TelegramUserId = telegramUserId };
            _db.UserMessageStates.Add(entity);
        }

        var updateInfo = new UserMessageStateUpdateInfo();
        updateAction(updateInfo);

        if (updateInfo.LastBotMessageId.HasValue) entity.LastBotMessageId = updateInfo.LastBotMessageId.Value;
        if (updateInfo.LastBotTelegramMessageId.HasValue) entity.LastBotTelegramMessageId = updateInfo.LastBotTelegramMessageId.Value;
        if (updateInfo.ShouldEdit.HasValue) entity.ShouldEdit = updateInfo.ShouldEdit.Value;
        if (updateInfo.ShouldReply.HasValue) entity.ShouldReply = updateInfo.ShouldReply.Value;
        if (updateInfo.ShouldKeepStatic.HasValue) entity.ShouldKeepStatic = updateInfo.ShouldKeepStatic.Value;
        if (updateInfo.DeleteNextMessages.HasValue) entity.DeleteNextMessages = updateInfo.DeleteNextMessages.Value;
        if (updateInfo.LastAction != null) entity.LastAction = updateInfo.LastAction;
        if (updateInfo.LastActionAt.HasValue) entity.LastActionAt = updateInfo.LastActionAt.Value;

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (_processingContext != null)
            _processingContext.SqlAccessed = true;
    }
}
