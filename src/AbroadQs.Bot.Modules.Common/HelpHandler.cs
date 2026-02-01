using AbroadQs.Bot.Contracts;

namespace AbroadQs.Bot.Modules.Common;

public sealed class HelpHandler : IUpdateHandler
{
    private readonly IResponseSender _sender;

    public HelpHandler(IResponseSender sender)
    {
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
    }

    public string? Command => "help";

    public bool CanHandle(BotUpdateContext context) =>
        string.Equals(context.Command, Command, StringComparison.OrdinalIgnoreCase);

    public async Task<bool> HandleAsync(BotUpdateContext context, CancellationToken cancellationToken)
    {
        const string text = """
            <b>راهنما</b>

            /start - شروع و خوش‌آمد
            /help - این راهنما
            /echo متن - (ماژول نمونه) بازگرداندن همان متن

            هر ماژول جدید می‌تواند دستورات خود را اضافه کند.
            """;
        await _sender.SendTextMessageAsync(context.ChatId, text.Trim(), cancellationToken).ConfigureAwait(false);
        return true;
    }
}
