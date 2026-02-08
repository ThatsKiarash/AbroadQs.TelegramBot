using AbroadQs.Bot.Contracts;

namespace AbroadQs.Bot.Modules.Common;

public sealed class StartHandler : IUpdateHandler
{
    private readonly IResponseSender _sender;
    private readonly ITelegramUserRepository _userRepo;
    private readonly IBotStageRepository _stageRepo;
    private readonly IPermissionRepository _permRepo;
    private readonly IUserMessageStateRepository? _msgStateRepo;

    public StartHandler(
        IResponseSender sender,
        ITelegramUserRepository userRepo,
        IBotStageRepository stageRepo,
        IPermissionRepository permRepo,
        IUserMessageStateRepository? msgStateRepo = null)
    {
        _sender = sender;
        _userRepo = userRepo;
        _stageRepo = stageRepo;
        _permRepo = permRepo;
        _msgStateRepo = msgStateRepo;
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

        // Delete user's /start message
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
            // Delete old bot msg after
            if (oldBotMsgId.HasValue)
                try { await _sender.DeleteMessageAsync(context.ChatId, oldBotMsgId.Value, cancellationToken).ConfigureAwait(false); } catch { }
        }
        else
        {
            // Returning user → reply-kb: edit text + update keyboard
            var keyboard = await BuildReplyKeyboardAsync(userId, stageKey, isFa, cancellationToken).ConfigureAwait(false);
            if (oldBotMsgId.HasValue)
            {
                try
                {
                    await _sender.EditMessageTextAsync(context.ChatId, oldBotMsgId.Value, text, cancellationToken).ConfigureAwait(false);
                    await _sender.UpdateReplyKeyboardSilentAsync(context.ChatId, keyboard, cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    // Edit failed — send plain text + phantom keyboard (so it's editable next time)
                    await _sender.SendTextMessageAsync(context.ChatId, text, cancellationToken).ConfigureAwait(false);
                    await _sender.UpdateReplyKeyboardSilentAsync(context.ChatId, keyboard, cancellationToken).ConfigureAwait(false);
                }
            }
            else
            {
                // First time — send plain text + phantom keyboard
                await _sender.SendTextMessageAsync(context.ChatId, text, cancellationToken).ConfigureAwait(false);
                await _sender.UpdateReplyKeyboardSilentAsync(context.ChatId, keyboard, cancellationToken).ConfigureAwait(false);
            }
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
        return keyboard;
    }

    private static string Escape(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
