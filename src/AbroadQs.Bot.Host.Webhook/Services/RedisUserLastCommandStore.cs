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

    public RedisUserLastCommandStore(IOptions<RedisOptions> options, ILogger<RedisUserLastCommandStore> logger)
    {
        var config = options?.Value?.Configuration ?? "localhost:6379";
        _redis = ConnectionMultiplexer.Connect(config);
        _db = _redis.GetDatabase();
    }

    public async Task SetLastCommandAsync(long userId, string command, CancellationToken cancellationToken = default)
    {
        var key = KeyPrefix + userId;
        await _db.StringSetAsync(key, command, KeyTtl).ConfigureAwait(false);
    }

    public async Task<string?> GetLastCommandAsync(long userId, CancellationToken cancellationToken = default)
    {
        var key = KeyPrefix + userId;
        var value = await _db.StringGetAsync(key).ConfigureAwait(false);
        return value.HasValue ? value.ToString() : null;
    }
}
