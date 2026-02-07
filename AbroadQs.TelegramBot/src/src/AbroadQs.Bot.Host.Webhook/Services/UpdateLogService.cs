using System.Collections.Concurrent;

namespace AbroadQs.Bot.Host.Webhook.Services;

/// <summary>
/// سرویس مشترک برای ثبت لاگ‌های آپدیت‌ها (Webhook و GetUpdates)
/// </summary>
public sealed class UpdateLogService
{
    private readonly ConcurrentQueue<UpdateLogEntry> _logs = new();
    private const int MaxLogs = 50;

    public void Log(UpdateLogEntry entry)
    {
        _logs.Enqueue(entry);
        while (_logs.Count > MaxLogs && _logs.TryDequeue(out _)) { }
    }

    public IReadOnlyList<UpdateLogEntry> GetRecentLogs(int count = 5)
    {
        return _logs.ToArray()
            .OrderByDescending(x => x.Time)
            .Take(count)
            .ToList();
    }
}

public record UpdateLogEntry(
    DateTime Time,
    string Status,
    string PayloadPreview,
    string? Error,
    string Source = "Unknown",
    bool RedisProcessed = false,
    bool RabbitMqPublished = false,
    bool SqlProcessed = false,
    bool ResponseSent = false,
    string? HandlerName = null
);
