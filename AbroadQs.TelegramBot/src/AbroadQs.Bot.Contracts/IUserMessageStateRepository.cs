namespace AbroadQs.Bot.Contracts;

/// <summary>
/// Repository for managing user message state (last bot message and its status).
/// </summary>
public interface IUserMessageStateRepository
{
    Task SetLastBotMessageAsync(long telegramUserId, int messageId, long telegramMessageId, CancellationToken cancellationToken = default);
    Task<UserMessageStateDto?> GetUserMessageStateAsync(long telegramUserId, CancellationToken cancellationToken = default);
    Task UpdateUserMessageStateAsync(long telegramUserId, Action<UserMessageStateUpdateInfo> updateAction, CancellationToken cancellationToken = default);
}

public sealed record UserMessageStateDto(
    long TelegramUserId,
    int? LastBotMessageId,
    long? LastBotTelegramMessageId,
    bool ShouldEdit,
    bool ShouldReply,
    bool ShouldKeepStatic,
    bool DeleteNextMessages,
    string? LastAction,
    DateTimeOffset? LastActionAt);

public sealed record UserMessageStateUpdateInfo
{
    public int? LastBotMessageId { get; set; }
    public long? LastBotTelegramMessageId { get; set; }
    public bool? ShouldEdit { get; set; }
    public bool? ShouldReply { get; set; }
    public bool? ShouldKeepStatic { get; set; }
    public bool? DeleteNextMessages { get; set; }
    public string? LastAction { get; set; }
    public DateTimeOffset? LastActionAt { get; set; }
}
