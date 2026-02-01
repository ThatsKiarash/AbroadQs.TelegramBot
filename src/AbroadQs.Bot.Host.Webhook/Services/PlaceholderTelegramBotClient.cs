using Telegram.Bot;
using Telegram.Bot.Args;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Requests.Abstractions;
using Telegram.Bot.Types;

namespace AbroadQs.Bot.Host.Webhook.Services;

/// <summary>
/// وقتی توکن ست نشده، این کلاینت ثبت می‌شود. هیچ exception پرتاب نمی‌کند تا ربات هرگز متوقف نشود.
/// </summary>
internal sealed class PlaceholderTelegramBotClient : ITelegramBotClient
{
    public bool LocalBotServer => false;
    public long BotId => 0;
    public TimeSpan Timeout { get; set; }
    public IExceptionParser ExceptionsParser { get; set; } = null!;
    public event AsyncEventHandler<ApiRequestEventArgs>? OnMakingApiRequest;
    public event AsyncEventHandler<ApiResponseEventArgs>? OnApiResponseReceived;

    public Task<TResponse> SendRequest<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        => Task.FromResult<TResponse>(default!);

    public Task<bool> TestApi(CancellationToken cancellationToken = default)
        => Task.FromResult(false);

    public Task DownloadFile(string filePath, Stream destination, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task DownloadFile(TGFile file, Stream destination, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
