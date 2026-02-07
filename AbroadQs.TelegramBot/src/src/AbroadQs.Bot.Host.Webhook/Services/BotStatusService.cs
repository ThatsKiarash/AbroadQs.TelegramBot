namespace AbroadQs.Bot.Host.Webhook.Services;

/// <summary>
/// سرویس مدیریت وضعیت روشن/خاموش ربات
/// </summary>
public sealed class BotStatusService
{
    private volatile bool _enabled = true;

    /// <summary>آیا ربات فعال است و باید به پیام‌ها پاسخ دهد</summary>
    public bool IsEnabled => _enabled;

    /// <summary>روشن کردن ربات</summary>
    public void Start() => _enabled = true;

    /// <summary>خاموش کردن ربات</summary>
    public void Stop() => _enabled = false;

    /// <summary>تغییر وضعیت</summary>
    public bool Toggle()
    {
        _enabled = !_enabled;
        return _enabled;
    }
}
