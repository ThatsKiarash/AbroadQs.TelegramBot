using AbroadQs.Bot.Contracts;

namespace AbroadQs.Bot.Modules.Common;

/// <summary>
/// Handles /settings, /menu and inline callbacks: language selection (glass button), profile, back to main.
/// </summary>
public sealed class SettingsMenuHandler : IUpdateHandler
{
    private readonly IResponseSender _sender;
    private readonly ITelegramUserRepository _userRepo;
    private readonly IUserConversationStateStore _stateStore;

    public SettingsMenuHandler(IResponseSender sender, ITelegramUserRepository userRepo, IUserConversationStateStore stateStore)
    {
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
        _userRepo = userRepo ?? throw new ArgumentNullException(nameof(userRepo));
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
    }

    public string? Command => null;

    public bool CanHandle(BotUpdateContext context)
    {
        if (context.UserId == null) return false;
        var cmd = context.Command;
        var data = context.MessageText?.Trim();
        if (string.Equals(cmd, "settings", StringComparison.OrdinalIgnoreCase) || string.Equals(cmd, "menu", StringComparison.OrdinalIgnoreCase))
            return true;
        if (context.IsCallbackQuery && data != null)
            return data.StartsWith("menu:", StringComparison.OrdinalIgnoreCase) || data.StartsWith("lang:", StringComparison.OrdinalIgnoreCase);
        return false;
    }

    public async Task<bool> HandleAsync(BotUpdateContext context, CancellationToken cancellationToken)
    {
        var userId = context.UserId!.Value;
        var data = context.MessageText?.Trim() ?? "";
        var lang = await GetUserLanguageAsync(userId, cancellationToken).ConfigureAwait(false);
        var editMessageId = context.IsCallbackQuery ? context.CallbackMessageId : null;

        if (context.IsCallbackQuery && context.CallbackQueryId != null)
            await _sender.AnswerCallbackQueryAsync(context.CallbackQueryId, null, cancellationToken).ConfigureAwait(false);

        if (data.StartsWith("lang:", StringComparison.OrdinalIgnoreCase))
        {
            var code = data["lang:".Length..].Trim();
            if (code.Length > 0)
            {
                await _userRepo.UpdateProfileAsync(userId, null, null, code, cancellationToken).ConfigureAwait(false);
                var msg = code == "fa" ? "زبان روی فارسی تنظیم شد." : "Language set to English.";
                await SendOrEditMainMenuAsync(context.ChatId, msg, code, editMessageId, cancellationToken).ConfigureAwait(false);
            }
            return true;
        }

        if (data.Equals("menu:lang", StringComparison.OrdinalIgnoreCase))
        {
            var langTitle = lang == "fa" ? "زبان را انتخاب کنید:" : "Select language:";
            var keyboard = new[]
            {
                new[] { new InlineButton("فارسی", "lang:fa"), new InlineButton("English", "lang:en") },
                new[] { new InlineButton(lang == "fa" ? "◀ بازگشت" : "◀ Back", "menu:main") }
            };
            await SendOrEditAsync(context.ChatId, langTitle, keyboard, editMessageId, cancellationToken).ConfigureAwait(false);
            return true;
        }

        if (data.Equals("menu:profile", StringComparison.OrdinalIgnoreCase))
        {
            await _stateStore.SetStateAsync(userId, "awaiting_profile_name", cancellationToken).ConfigureAwait(false);
            var ask = lang == "fa"
                ? "نام و نام خانوادگی خود را در یک خط بفرستید، مثلاً:\nعلی احمدی"
                : "Send your first and last name in one line, e.g.:\nJohn Smith";
            var back = lang == "fa" ? "◀ بازگشت" : "◀ Back";
            var keyboard = new[] { new[] { new InlineButton(back, "menu:main") } };
            await SendOrEditAsync(context.ChatId, ask, keyboard, editMessageId, cancellationToken).ConfigureAwait(false);
            return true;
        }

        if (data.Equals("menu:main", StringComparison.OrdinalIgnoreCase) || string.Equals(context.Command, "settings", StringComparison.OrdinalIgnoreCase) || string.Equals(context.Command, "menu", StringComparison.OrdinalIgnoreCase))
        {
            await SendOrEditMainMenuAsync(context.ChatId, null, lang, editMessageId, cancellationToken).ConfigureAwait(false);
            return true;
        }

        return false;
    }

    private async Task SendOrEditAsync(long chatId, string text, IReadOnlyList<IReadOnlyList<InlineButton>> keyboard, int? editMessageId, CancellationToken cancellationToken)
    {
        if (editMessageId.HasValue)
            await _sender.EditMessageTextWithInlineKeyboardAsync(chatId, editMessageId.Value, text, keyboard, cancellationToken).ConfigureAwait(false);
        else
            await _sender.SendTextMessageWithInlineKeyboardAsync(chatId, text, keyboard, cancellationToken).ConfigureAwait(false);
    }

    private async Task SendOrEditMainMenuAsync(long chatId, string? title, string? lang, int? editMessageId, CancellationToken cancellationToken)
    {
        var isFa = lang == "fa";
        var heading = title ?? (isFa ? "تنظیمات" : "Settings");
        var langLabel = isFa ? "🌐 زبان" : "🌐 Language";
        var profileLabel = isFa ? "👤 نام و نام خانوادگی" : "👤 Name & family";
        var keyboard = new List<IReadOnlyList<InlineButton>>
        {
            new[] { new InlineButton(langLabel, "menu:lang") },
            new[] { new InlineButton(profileLabel, "menu:profile") }
        };
        if (editMessageId.HasValue)
            await _sender.EditMessageTextWithInlineKeyboardAsync(chatId, editMessageId.Value, heading, keyboard, cancellationToken).ConfigureAwait(false);
        else
            await _sender.SendTextMessageWithInlineKeyboardAsync(chatId, heading, keyboard, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string?> GetUserLanguageAsync(long userId, CancellationToken cancellationToken)
    {
        var user = await _userRepo.GetByTelegramUserIdAsync(userId, cancellationToken).ConfigureAwait(false);
        return user?.PreferredLanguage;
    }
}
