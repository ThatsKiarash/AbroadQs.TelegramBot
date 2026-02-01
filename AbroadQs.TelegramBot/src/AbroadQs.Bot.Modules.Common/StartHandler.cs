using AbroadQs.Bot.Contracts;

namespace AbroadQs.Bot.Modules.Common;

public sealed class StartHandler : IUpdateHandler
{
    private readonly IResponseSender _sender;
    private readonly IUserLastCommandStore _lastCommandStore;

    public StartHandler(IResponseSender sender, IUserLastCommandStore lastCommandStore)
    {
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
        _lastCommandStore = lastCommandStore ?? throw new ArgumentNullException(nameof(lastCommandStore));
    }

    public string? Command => "start";

    public bool CanHandle(BotUpdateContext context) =>
        string.Equals(context.Command, Command, StringComparison.OrdinalIgnoreCase);

    public async Task<bool> HandleAsync(BotUpdateContext context, CancellationToken cancellationToken)
    {
        string? lastCmd = null;
        if (context.UserId.HasValue)
        {
            lastCmd = await _lastCommandStore.GetLastCommandAsync(context.UserId.Value, cancellationToken).ConfigureAwait(false);
            await _lastCommandStore.SetLastCommandAsync(context.UserId.Value, "start", cancellationToken).ConfigureAwait(false);
        }

        var name = context.FirstName ?? context.Username ?? "User";
        var lastLine = lastCmd != null ? $"\n(آخرین دستور قبلی: /{Escape(lastCmd)})" : "";
        var text = $"<b>سلام {Escape(name)}!</b>\n\nبه ربات AbroadQs خوش آمدید.\n\nدستور /help را برای راهنما بزنید.{lastLine}";
        await _sender.SendTextMessageAsync(context.ChatId, text, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private static string Escape(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
