namespace AbroadQs.Bot.Contracts;

/// <summary>
/// ردیابی عملیات پردازش هر درخواست
/// </summary>
public interface IProcessingContext
{
    /// <summary>منبع دریافت آپدیت: Webhook یا GetUpdates</summary>
    string Source { get; set; }

    /// <summary>آیا Redis استفاده شد</summary>
    bool RedisAccessed { get; set; }

    /// <summary>آیا به RabbitMQ publish شد</summary>
    bool RabbitMqPublished { get; set; }

    /// <summary>آیا SQL Server استفاده شد</summary>
    bool SqlAccessed { get; set; }

    /// <summary>آیا پاسخ به کاربر ارسال شد</summary>
    bool ResponseSent { get; set; }

    /// <summary>نام هندلری که آپدیت را پردازش کرد</summary>
    string? HandlerName { get; set; }
}
