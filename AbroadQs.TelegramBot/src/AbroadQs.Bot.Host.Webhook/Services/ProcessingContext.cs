using AbroadQs.Bot.Contracts;

namespace AbroadQs.Bot.Host.Webhook.Services;

/// <summary>
/// ردیابی عملیات پردازش هر درخواست — Scoped برای هر request/update
/// </summary>
public sealed class ProcessingContext : IProcessingContext
{
    /// <summary>منبع دریافت آپدیت: Webhook یا GetUpdates</summary>
    public string Source { get; set; } = "Unknown";

    /// <summary>آیا Redis استفاده شد (مثلاً ذخیره آخرین دستور)</summary>
    public bool RedisAccessed { get; set; }

    /// <summary>آیا به RabbitMQ publish شد</summary>
    public bool RabbitMqPublished { get; set; }

    /// <summary>آیا SQL Server استفاده شد (مثلاً ذخیره کاربر)</summary>
    public bool SqlAccessed { get; set; }

    /// <summary>آیا پاسخ به کاربر ارسال شد</summary>
    public bool ResponseSent { get; set; }

    /// <summary>نام هندلری که آپدیت را پردازش کرد</summary>
    public string? HandlerName { get; set; }

    /// <summary>خطا در صورت وجود</summary>
    public string? Error { get; set; }
}
