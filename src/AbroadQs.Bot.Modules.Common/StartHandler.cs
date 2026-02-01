using AbroadQs.Bot.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace AbroadQs.Bot.Modules.Common;

public sealed class StartHandler : IUpdateHandler
{
    private readonly IResponseSender _sender;
    private readonly IUserLastCommandStore _lastCommandStore;
    private readonly IServiceProvider _serviceProvider;

    public StartHandler(IResponseSender sender, IUserLastCommandStore lastCommandStore, IServiceProvider serviceProvider)
    {
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
        _lastCommandStore = lastCommandStore ?? throw new ArgumentNullException(nameof(lastCommandStore));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
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
            using (var scope = _serviceProvider.CreateScope())
            {
                var userRepo = scope.ServiceProvider.GetService<ITelegramUserRepository>();
                if (userRepo != null)
                    await userRepo.SaveOrUpdateAsync(context.UserId.Value, context.Username, context.FirstName, context.LastName, cancellationToken).ConfigureAwait(false);
            }
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
