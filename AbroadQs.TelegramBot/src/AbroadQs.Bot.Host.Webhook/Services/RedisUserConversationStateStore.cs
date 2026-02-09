using AbroadQs.Bot.Contracts;
using StackExchange.Redis;

namespace AbroadQs.Bot.Host.Webhook.Services;

public sealed class RedisUserConversationStateStore : IUserConversationStateStore
{
    private const string KeyPrefix = "user:conv:";
    private static readonly TimeSpan KeyTtl = TimeSpan.FromHours(1);
    private readonly IConnectionMultiplexer _redis;
    private readonly IDatabase _db;
    private readonly ILogger<RedisUserConversationStateStore> _logger;
    private readonly IProcessingContext? _processingContext;

    public RedisUserConversationStateStore(
        IConnectionMultiplexer redis,
        ILogger<RedisUserConversationStateStore> logger,
        IProcessingContext? processingContext = null)
    {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _processingContext = processingContext;
        _db = _redis.GetDatabase();
    }

    public async Task SetStateAsync(long userId, string state, CancellationToken cancellationToken = default)
    {
        try
        {
            var key = KeyPrefix + userId;
            await _db.StringSetAsync(key, state, KeyTtl).ConfigureAwait(false);
            if (_processingContext != null)
                _processingContext.RedisAccessed = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis SetState failed for user {UserId}", userId);
        }
    }

    public async Task<string?> GetStateAsync(long userId, CancellationToken cancellationToken = default)
    {
        try
        {
            var key = KeyPrefix + userId;
            var value = await _db.StringGetAsync(key).ConfigureAwait(false);
            if (_processingContext != null)
                _processingContext.RedisAccessed = true;
            return value.HasValue ? value.ToString() : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis GetState failed for user {UserId}", userId);
            return null;
        }
    }

    public async Task ClearStateAsync(long userId, CancellationToken cancellationToken = default)
    {
        try
        {
            var key = KeyPrefix + userId;
            await _db.KeyDeleteAsync(key).ConfigureAwait(false);
            if (_processingContext != null)
                _processingContext.RedisAccessed = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis ClearState failed for user {UserId}", userId);
        }
    }

    private const string ReplyStagePrefix = "user:replystage:";

    public async Task SetReplyStageAsync(long userId, string stageKey, CancellationToken cancellationToken = default)
    {
        try
        {
            var key = ReplyStagePrefix + userId;
            await _db.StringSetAsync(key, stageKey, TimeSpan.FromHours(24)).ConfigureAwait(false);
            if (_processingContext != null)
                _processingContext.RedisAccessed = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis SetReplyStage failed for user {UserId}", userId);
        }
    }

    public async Task<string?> GetReplyStageAsync(long userId, CancellationToken cancellationToken = default)
    {
        try
        {
            var key = ReplyStagePrefix + userId;
            var value = await _db.StringGetAsync(key).ConfigureAwait(false);
            if (_processingContext != null)
                _processingContext.RedisAccessed = true;
            return value.HasValue ? value.ToString() : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis GetReplyStage failed for user {UserId}", userId);
            return null;
        }
    }
}
