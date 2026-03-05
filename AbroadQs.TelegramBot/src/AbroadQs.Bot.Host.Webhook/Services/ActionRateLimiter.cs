using System.Collections.Concurrent;

namespace AbroadQs.Bot.Host.Webhook.Services;

public sealed class ActionRateLimiter
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastAction = new();

    public bool IsAllowed(string key, TimeSpan interval)
    {
        var now = DateTimeOffset.UtcNow;
        if (_lastAction.TryGetValue(key, out var previous) && now - previous < interval)
            return false;
        _lastAction[key] = now;
        return true;
    }
}
