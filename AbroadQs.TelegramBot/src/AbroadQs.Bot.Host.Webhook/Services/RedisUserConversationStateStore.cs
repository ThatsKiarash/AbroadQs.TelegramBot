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

    private const string FlowMsgPrefix = "user:flowmsgs:";

    public async Task AddFlowMessageIdAsync(long userId, int messageId, CancellationToken cancellationToken = default)
    {
        try
        {
            var key = FlowMsgPrefix + userId;
            await _db.ListRightPushAsync(key, messageId).ConfigureAwait(false);
            await _db.KeyExpireAsync(key, TimeSpan.FromHours(2)).ConfigureAwait(false);
            if (_processingContext != null) _processingContext.RedisAccessed = true;
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Redis AddFlowMessageId failed for user {UserId}", userId); }
    }

    public async Task<List<int>> GetAndClearFlowMessageIdsAsync(long userId, CancellationToken cancellationToken = default)
    {
        var result = new List<int>();
        try
        {
            var key = FlowMsgPrefix + userId;
            var values = await _db.ListRangeAsync(key).ConfigureAwait(false);
            foreach (var v in values)
                if (v.TryParse(out int id)) result.Add(id);
            await _db.KeyDeleteAsync(key).ConfigureAwait(false);
            if (_processingContext != null) _processingContext.RedisAccessed = true;
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Redis GetAndClearFlowMessageIds failed for user {UserId}", userId); }
        return result;
    }

    // ── Flow data (key-value for multi-step flows) ───────────────────

    private const string FlowDataPrefix = "user:flowdata:";
    private static readonly TimeSpan FlowDataTtl = TimeSpan.FromHours(2);

    public async Task SetFlowDataAsync(long userId, string key, string value, CancellationToken cancellationToken = default)
    {
        try
        {
            var redisKey = FlowDataPrefix + userId;
            await _db.HashSetAsync(redisKey, key, value).ConfigureAwait(false);
            await _db.KeyExpireAsync(redisKey, FlowDataTtl).ConfigureAwait(false);
            if (_processingContext != null) _processingContext.RedisAccessed = true;
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Redis SetFlowData failed for user {UserId} key {Key}", userId, key); }
    }

    public async Task<string?> GetFlowDataAsync(long userId, string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var redisKey = FlowDataPrefix + userId;
            var value = await _db.HashGetAsync(redisKey, key).ConfigureAwait(false);
            if (_processingContext != null) _processingContext.RedisAccessed = true;
            return value.HasValue ? value.ToString() : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis GetFlowData failed for user {UserId} key {Key}", userId, key);
            return null;
        }
    }

    public async Task ClearAllFlowDataAsync(long userId, CancellationToken cancellationToken = default)
    {
        try
        {
            var redisKey = FlowDataPrefix + userId;
            await _db.KeyDeleteAsync(redisKey).ConfigureAwait(false);
            if (_processingContext != null) _processingContext.RedisAccessed = true;
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Redis ClearAllFlowData failed for user {UserId}", userId); }
    }
}
