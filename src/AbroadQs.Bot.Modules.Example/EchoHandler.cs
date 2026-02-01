using AbroadQs.Bot.Contracts;

namespace AbroadQs.Bot.Modules.Example;

public sealed class EchoHandler : IUpdateHandler
{
    private readonly IResponseSender _sender;

    public EchoHandler(IResponseSender sender)
    {
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
    }

    public string? Command => "echo";

    public bool CanHandle(BotUpdateContext context) =>
        string.Equals(context.Command, Command, StringComparison.OrdinalIgnoreCase);

    public async Task<bool> HandleAsync(BotUpdateContext context, CancellationToken cancellationToken)
    {
        var args = context.CommandArguments;
        var text = string.IsNullOrWhiteSpace(args)
            ? "یک متن بعد از /echo بنویس. مثال: /echo سلام"
            : $"<b>Echo:</b> {Escape(args)}";
        await _sender.SendTextMessageAsync(context.ChatId, text, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private static string Escape(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
