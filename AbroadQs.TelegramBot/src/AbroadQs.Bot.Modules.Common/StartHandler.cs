using AbroadQs.Bot.Contracts;

namespace AbroadQs.Bot.Modules.Common;

public sealed class StartHandler : IUpdateHandler
{
    private readonly IResponseSender _sender;
    private readonly ITelegramUserRepository _userRepo;
    private readonly IBotStageRepository _stageRepo;
    private readonly IPermissionRepository _permRepo;

    public StartHandler(
        IResponseSender sender,
        ITelegramUserRepository userRepo,
        IBotStageRepository stageRepo,
        IPermissionRepository permRepo)
    {
        _sender = sender;
        _userRepo = userRepo;
        _stageRepo = stageRepo;
        _permRepo = permRepo;
    }

    public string? Command => "start";

    public bool CanHandle(BotUpdateContext context) =>
        string.Equals(context.Command, Command, StringComparison.OrdinalIgnoreCase);

    public async Task<bool> HandleAsync(BotUpdateContext context, CancellationToken cancellationToken)
    {
        if (context.UserId == null) return false;
        var userId = context.UserId.Value;

        // Load user
        var user = await _userRepo.GetByTelegramUserIdAsync(userId, cancellationToken).ConfigureAwait(false);
        var isNewUser = user == null || !user.IsRegistered;

        // If not registered, mark as registered now
        if (isNewUser)
        {
            await _userRepo.MarkAsRegisteredAsync(userId, cancellationToken).ConfigureAwait(false);
            // Grant default permission on first registration
            try { await _permRepo.GrantPermissionAsync(userId, "default", cancellationToken).ConfigureAwait(false); } catch { }
            // Re-load user
            user = await _userRepo.GetByTelegramUserIdAsync(userId, cancellationToken).ConfigureAwait(false);
        }

        var lang = user?.PreferredLanguage ?? "fa";
        var isFa = lang == "fa";
        var name = Escape(context.FirstName ?? context.Username ?? "User");

        // New user → show welcome + language selection
        // Returning user → show main_menu directly
        var stageKey = isNewUser ? "welcome" : "main_menu";

        var stage = await _stageRepo.GetByKeyAsync(stageKey, cancellationToken).ConfigureAwait(false);
        string text;
        if (stage != null && stage.IsEnabled)
        {
            var template = isFa ? (stage.TextFa ?? stage.TextEn ?? "") : (stage.TextEn ?? stage.TextFa ?? "");
            text = template.Replace("{name}", name);
        }
        else
        {
            // Fallback
            text = isNewUser
                ? (isFa
                    ? $"<b>سلام {name}!</b>\n\nبه ربات <b>AbroadQs</b> خوش آمدید.\nلطفاً زبان مورد نظر خود را انتخاب کنید."
                    : $"<b>Hello {name}!</b>\n\nWelcome to <b>AbroadQs</b> bot.\nPlease select your preferred language.")
                : (isFa
                    ? $"<b>سلام {name}!</b>\n\nیکی از گزینه‌های زیر را انتخاب کنید:"
                    : $"<b>Hello {name}!</b>\n\nSelect an option below:");
        }

        // Load buttons from DB for the chosen stage
        var keyboard = await BuildKeyboardAsync(userId, stageKey, isFa, cancellationToken).ConfigureAwait(false);

        // If no buttons defined in DB, add defaults
        if (keyboard.Count == 0)
        {
            if (isNewUser)
                keyboard = new List<IReadOnlyList<InlineButton>>
                {
                    new[] { new InlineButton("فارسی 🇮🇷", "lang:fa"), new InlineButton("English 🇬🇧", "lang:en") }
                };
            else
                keyboard = new List<IReadOnlyList<InlineButton>>
                {
                    new[] { new InlineButton(isFa ? "📋 ثبت درخواست" : "📋 Submit Request", "stage:new_request") },
                    new[] { new InlineButton(isFa ? "💰 امور مالی" : "💰 Finance", "stage:finance"), new InlineButton(isFa ? "💡 پیشنهادات من" : "💡 My Suggestions", "stage:my_suggestions"), new InlineButton(isFa ? "✉️ پیام های من" : "✉️ My Messages", "stage:my_messages") },
                    new[] { new InlineButton(isFa ? "👤 پروفایل من" : "👤 My Profile", "stage:profile"), new InlineButton(isFa ? "ℹ️ درباره ما" : "ℹ️ About Us", "stage:about_us"), new InlineButton(isFa ? "🎫 تیکت ها" : "🎫 Tickets", "stage:tickets") },
                    new[] { new InlineButton(isFa ? "⚙️ تنظیمات" : "⚙️ Settings", "stage:settings") }
                };
        }

        await _sender.SendTextMessageWithInlineKeyboardAsync(context.ChatId, text, keyboard, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task<List<IReadOnlyList<InlineButton>>> BuildKeyboardAsync(long userId, string stageKey, bool isFa, CancellationToken cancellationToken)
    {
        var allButtons = await _stageRepo.GetButtonsAsync(stageKey, cancellationToken).ConfigureAwait(false);
        if (allButtons.Count == 0) return new List<IReadOnlyList<InlineButton>>();

        var userPerms = await _permRepo.GetUserPermissionsAsync(userId, cancellationToken).ConfigureAwait(false);
        var permSet = new HashSet<string>(userPerms, StringComparer.OrdinalIgnoreCase);

        var visibleButtons = new List<BotStageButtonDto>();
        foreach (var btn in allButtons)
        {
            if (!btn.IsEnabled) continue;
            if (!string.IsNullOrEmpty(btn.RequiredPermission) && !permSet.Contains(btn.RequiredPermission)) continue;
            visibleButtons.Add(btn);
        }

        var keyboard = new List<IReadOnlyList<InlineButton>>();
        var rows = visibleButtons.GroupBy(b => b.Row).OrderBy(g => g.Key);
        foreach (var row in rows)
        {
            var rowButtons = new List<InlineButton>();
            foreach (var btn in row.OrderBy(b => b.Column))
            {
                var btnText = isFa ? (btn.TextFa ?? btn.TextEn ?? "?") : (btn.TextEn ?? btn.TextFa ?? "?");
                var callbackData = btn.CallbackData;
                if (string.IsNullOrEmpty(callbackData) && !string.IsNullOrEmpty(btn.TargetStageKey))
                    callbackData = $"stage:{btn.TargetStageKey}";

                if (btn.ButtonType == "url" && !string.IsNullOrEmpty(btn.Url))
                    rowButtons.Add(new InlineButton(btnText, null, btn.Url));
                else
                    rowButtons.Add(new InlineButton(btnText, callbackData ?? "noop"));
            }
            if (rowButtons.Count > 0)
                keyboard.Add(rowButtons);
        }
        return keyboard;
    }

    private static string Escape(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
