using AbroadQs.Bot.Contracts;

namespace AbroadQs.Bot.Modules.Common;

public sealed class StartHandler : IUpdateHandler
{
    private readonly IResponseSender _sender;
    private readonly ITelegramUserRepository _userRepo;
    private readonly IBotStageRepository _stageRepo;
    private readonly IPermissionRepository _permRepo;
    private readonly IUserMessageStateRepository? _msgStateRepo;
    private readonly IUserConversationStateStore _stateStore;
    private readonly BidStateHandler? _bidHandler;
    private const string ServerMenuBtnFa = "مدیریت سرورها";
    private const string ServerMenuBtnEn = "Server Management";

    public StartHandler(
        IResponseSender sender,
        ITelegramUserRepository userRepo,
        IBotStageRepository stageRepo,
        IPermissionRepository permRepo,
        IUserConversationStateStore stateStore,
        IUserMessageStateRepository? msgStateRepo = null,
        BidStateHandler? bidHandler = null)
    {
        _sender = sender;
        _userRepo = userRepo;
        _stageRepo = stageRepo;
        _permRepo = permRepo;
        _stateStore = stateStore;
        _msgStateRepo = msgStateRepo;
        _bidHandler = bidHandler;
    }

    public string? Command => "start";

    public bool CanHandle(BotUpdateContext context) =>
        string.Equals(context.Command, Command, StringComparison.OrdinalIgnoreCase);

    public async Task<bool> HandleAsync(BotUpdateContext context, CancellationToken cancellationToken)
    {
        if (context.UserId == null) return false;
        var userId = context.UserId.Value;

        // ── Deep link: /start bid_{requestId} ──
        var args = context.CommandArguments;
        if (!string.IsNullOrEmpty(args) && args.StartsWith("bid_", StringComparison.Ordinal) && _bidHandler != null)
        {
            if (int.TryParse(args["bid_".Length..], out var bidRequestId))
            {
                try { if (context.IncomingMessageId.HasValue) await _sender.DeleteMessageAsync(context.ChatId, context.IncomingMessageId.Value, cancellationToken).ConfigureAwait(false); } catch { }
                await _bidHandler.StartBidFlow(context.ChatId, userId, bidRequestId, cancellationToken).ConfigureAwait(false);
                return true;
            }
        }

        // Load user
        var user = await _userRepo.GetByTelegramUserIdAsync(userId, cancellationToken).ConfigureAwait(false);
        // A user is "new" if they haven't selected a language yet (first visit)
        var isNewUser = user == null || string.IsNullOrEmpty(user.PreferredLanguage);

        // Grant default permission on first visit (if not already granted)
        if (isNewUser)
        {
            try { await _permRepo.GrantPermissionAsync(userId, "default", cancellationToken).ConfigureAwait(false); } catch { }
        }

        var lang = user?.PreferredLanguage ?? "fa";
        var isFa = lang == "fa";
        var cleanMode = user?.CleanChatMode ?? true;
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

        // Delete user's /start message (only in clean mode)
        if (cleanMode)
            try { if (context.IncomingMessageId.HasValue) await _sender.DeleteMessageAsync(context.ChatId, context.IncomingMessageId.Value, cancellationToken).ConfigureAwait(false); } catch { }

        // Get old bot message ID
        int? oldBotMsgId = null;
        if (_msgStateRepo != null)
        {
            try
            {
                var msgState = await _msgStateRepo.GetUserMessageStateAsync(userId, cancellationToken).ConfigureAwait(false);
                if (msgState?.LastBotTelegramMessageId is > 0) oldBotMsgId = (int)msgState.LastBotTelegramMessageId;
            }
            catch { }
        }

        if (isNewUser)
        {
            // Welcome stage → inline keyboard (language selection) — always send new
            var keyboard = await BuildInlineKeyboardAsync(userId, stageKey, isFa, cancellationToken).ConfigureAwait(false);
            if (keyboard.Count == 0)
                keyboard = new List<IReadOnlyList<InlineButton>>
                {
                    new[] { new InlineButton("فارسی 🇮🇷", "lang:fa"), new InlineButton("English 🇬🇧", "lang:en") }
                };
            await _sender.SendTextMessageWithInlineKeyboardAsync(context.ChatId, text, keyboard, cancellationToken).ConfigureAwait(false);
            // Delete old bot msg after (only in clean mode)
            if (cleanMode && oldBotMsgId.HasValue)
                try { await _sender.DeleteMessageAsync(context.ChatId, oldBotMsgId.Value, cancellationToken).ConfigureAwait(false); } catch { }
        }
        else
        {
            // Returning user → send new reply keyboard, then delete old
            var keyboard = await BuildReplyKeyboardAsync(userId, stageKey, isFa, cancellationToken).ConfigureAwait(false);
            await _sender.SendTextMessageWithReplyKeyboardAsync(context.ChatId, text, keyboard, cancellationToken).ConfigureAwait(false);
            // Track current reply stage for back-button routing
            await _stateStore.SetReplyStageAsync(userId, "main_menu", cancellationToken).ConfigureAwait(false);
            if (cleanMode && oldBotMsgId.HasValue)
                try { await _sender.DeleteMessageAsync(context.ChatId, oldBotMsgId.Value, cancellationToken).ConfigureAwait(false); } catch { }
        }

        return true;
    }

    private async Task<List<BotStageButtonDto>> GetVisibleButtonsAsync(long userId, string stageKey, CancellationToken cancellationToken)
    {
        var allButtons = await _stageRepo.GetButtonsAsync(stageKey, cancellationToken).ConfigureAwait(false);
        if (allButtons.Count == 0) return new List<BotStageButtonDto>();

        var userPerms = await _permRepo.GetUserPermissionsAsync(userId, cancellationToken).ConfigureAwait(false);
        var permSet = new HashSet<string>(userPerms, StringComparer.OrdinalIgnoreCase);

        var visibleButtons = new List<BotStageButtonDto>();
        foreach (var btn in allButtons)
        {
            if (!btn.IsEnabled) continue;
            if (!string.IsNullOrEmpty(btn.RequiredPermission) && !permSet.Contains(btn.RequiredPermission)) continue;
            visibleButtons.Add(btn);
        }
        return visibleButtons;
    }

    private async Task<List<IReadOnlyList<InlineButton>>> BuildInlineKeyboardAsync(long userId, string stageKey, bool isFa, CancellationToken cancellationToken)
    {
        var visibleButtons = await GetVisibleButtonsAsync(userId, stageKey, cancellationToken).ConfigureAwait(false);
        var keyboard = new List<IReadOnlyList<InlineButton>>();
        foreach (var row in visibleButtons.GroupBy(b => b.Row).OrderBy(g => g.Key))
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

    private async Task<List<IReadOnlyList<string>>> BuildReplyKeyboardAsync(long userId, string stageKey, bool isFa, CancellationToken cancellationToken)
    {
        var visibleButtons = await GetVisibleButtonsAsync(userId, stageKey, cancellationToken).ConfigureAwait(false);
        var keyboard = new List<IReadOnlyList<string>>();
        foreach (var row in visibleButtons.GroupBy(b => b.Row).OrderBy(g => g.Key))
        {
            var rowTexts = new List<string>();
            foreach (var btn in row.OrderBy(b => b.Column))
            {
                var btnText = isFa ? (btn.TextFa ?? btn.TextEn ?? "?") : (btn.TextEn ?? btn.TextFa ?? "?");
                rowTexts.Add(btnText);
            }
            if (rowTexts.Count > 0)
                keyboard.Add(rowTexts);
        }

        // Keep server button next to Settings in main menu.
        if (string.Equals(stageKey, "main_menu", StringComparison.OrdinalIgnoreCase))
            keyboard = AttachServerButtonNearSettings(keyboard, isFa);

        return keyboard;
    }

    private static List<IReadOnlyList<string>> AttachServerButtonNearSettings(List<IReadOnlyList<string>> keyboard, bool isFa)
    {
        var serverBtn = isFa ? ServerMenuBtnFa : ServerMenuBtnEn;
        var settingsHint = isFa ? "تنظیم" : "setting";

        if (keyboard.Any(r => r.Any(c => string.Equals(c, serverBtn, StringComparison.Ordinal))))
            return keyboard;

        for (var i = 0; i < keyboard.Count; i++)
        {
            var row = keyboard[i].ToList();
            if (row.Any(c => c.Contains(settingsHint, StringComparison.OrdinalIgnoreCase)))
            {
                row.Add(serverBtn);
                keyboard[i] = row;
                return keyboard;
            }
        }

        keyboard.Add(new[] { serverBtn });
        return keyboard;
    }

    private static string Escape(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
