using AbroadQs.Bot.Host.Webhook.Services;
using Xunit;

namespace AbroadQs.Bot.Host.Webhook.Tests;

public sealed class ActionRateLimiterTests
{
    [Fact]
    public void IsAllowed_Blocks_Immediate_Replay()
    {
        var limiter = new ActionRateLimiter();
        var key = "user:action";

        var first = limiter.IsAllowed(key, TimeSpan.FromSeconds(2));
        var second = limiter.IsAllowed(key, TimeSpan.FromSeconds(2));

        Assert.True(first);
        Assert.False(second);
    }
}
