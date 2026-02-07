using AbroadQs.Bot.Contracts;

namespace AbroadQs.Bot.Modules.Common;

/// <summary>
/// Handles text message when user is in "awaiting_profile_name" state (after tapping Profile).
/// Parses "FirstName LastName" and updates profile, then shows main menu.
/// </summary>
public sealed class ProfileStateHandler : IUpdateHandler
{
    private readonly IResponseSender _sender;
    private readonly ITelegramUserRepository _userRepo;
    private readonly IUserConversationStateStore _stateStore;

    public ProfileStateHandler(IResponseSender sender, ITelegramUserRepository userRepo, IUserConversationStateStore stateStore)
    {
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
        _userRepo = userRepo ?? throw new ArgumentNullException(nameof(userRepo));
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
    }

    public string? Command => null;

    public bool CanHandle(BotUpdateContext context)
    {
        if (context.UserId == null || context.IsCallbackQuery) return false;
        if (string.IsNullOrWhiteSpace(context.MessageText)) return false;
        if (context.Command != null) return false; // don't intercept commands
        return true; // state check in HandleAsync; if not awaiting_profile_name we return false
    }

    public async Task<bool> HandleAsync(BotUpdateContext context, CancellationToken cancellationToken)
    {
        if (context.UserId == null) return false;
        var state = await _stateStore.GetStateAsync(context.UserId.Value, cancellationToken).ConfigureAwait(false);
        if (state != "awaiting_profile_name") return false;

        var text = context.MessageText!.Trim();
        var parts = text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var firstName = parts.Length > 0 ? parts[0].Trim() : null;
        var lastName = parts.Length > 1 ? parts[1].Trim() : null;

        await _stateStore.ClearStateAsync(context.UserId.Value, cancellationToken).ConfigureAwait(false);
        await _userRepo.UpdateProfileAsync(context.UserId.Value, firstName, lastName, null, cancellationToken).ConfigureAwait(false);

        var user = await _userRepo.GetByTelegramUserIdAsync(context.UserId.Value, cancellationToken).ConfigureAwait(false);
        var lang = user?.PreferredLanguage;
        var isFa = lang == "fa";
        var saved = isFa
            ? $"ذخیره شد.\nنام: {Escape(firstName ?? "—")}\nنام خانوادگی: {Escape(lastName ?? "—")}"
            : $"Saved.\nFirst name: {Escape(firstName ?? "—")}\nLast name: {Escape(lastName ?? "—")}";
        var back = isFa ? "◀ بازگشت به تنظیمات" : "◀ Back to settings";
        await _sender.SendTextMessageWithInlineKeyboardAsync(context.ChatId, saved,
            new[] { new[] { new InlineButton(back, "menu:main") } }, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private static string Escape(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
