using AbroadQs.Bot.Contracts;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace AbroadQs.Bot.Host.Webhook.Services;

public sealed class RedisUserLastCommandStore : IUserLastCommandStore
{
    private const string KeyPrefix = "user:lastcmd:";
    private static readonly TimeSpan KeyTtl = TimeSpan.FromDays(30);
    private readonly IConnectionMultiplexer _redis;
    private readonly IDatabase _db;
    private readonly ILogger<RedisUserLastCommandStore> _logger;
    private readonly IProcessingContext? _processingContext;

    public RedisUserLastCommandStore(
        IConnectionMultiplexer redis,
        ILogger<RedisUserLastCommandStore> logger,
        IProcessingContext? processingContext = null)
    {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _processingContext = processingContext;
        _db = _redis.GetDatabase();
    }

    public async Task SetLastCommandAsync(long userId, string command, CancellationToken cancellationToken = default)
    {
        try
        {
            var key = KeyPrefix + userId;
            await _db.StringSetAsync(key, command, KeyTtl).ConfigureAwait(false);
            if (_processingContext != null)
                _processingContext.RedisAccessed = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis SetLastCommand failed for user {UserId}", userId);
        }
    }

    public async Task<string?> GetLastCommandAsync(long userId, CancellationToken cancellationToken = default)
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
            _logger.LogWarning(ex, "Redis GetLastCommand failed for user {UserId}", userId);
            return null;
        }
    }
}
