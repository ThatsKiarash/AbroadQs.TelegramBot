using AbroadQs.Bot.Contracts;

namespace AbroadQs.Bot.Modules.Common;

/// <summary>
/// به هر پیام متنی که با دستور شناخته‌شده جور نشد، یک پاسخ پیش‌فرض می‌دهد تا کاربر بداند ربات زنده است.
/// </summary>
public sealed class UnknownCommandHandler : IUpdateHandler
{
    private readonly IResponseSender _sender;

    public UnknownCommandHandler(IResponseSender sender)
    {
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
    }

    public string? Command => null;

    public bool CanHandle(BotUpdateContext context) =>
        !string.IsNullOrWhiteSpace(context.MessageText);

    public async Task<bool> HandleAsync(BotUpdateContext context, CancellationToken cancellationToken)
    {
        const string text = "این دستور شناخته نشد. برای راهنما /help را بزنید.";
        await _sender.SendTextMessageAsync(context.ChatId, text, cancellationToken).ConfigureAwait(false);
        return true;
    }
}
