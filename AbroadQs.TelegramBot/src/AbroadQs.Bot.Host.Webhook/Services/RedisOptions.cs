namespace AbroadQs.Bot.Host.Webhook.Services;

public sealed class RedisOptions
{
    public const string SectionName = "Redis";
    public string Configuration { get; set; } = "localhost:6379";
}
