using AbroadQs.Bot.Contracts;

namespace AbroadQs.Bot.Modules.Common;

/// <summary>
/// Handles callbacks like "stage:xxx" — loads the stage from DB, checks permissions, and displays it.
/// Also handles "lang:xx" callbacks for language selection.
/// </summary>
public sealed class DynamicStageHandler : IUpdateHandler
{
    private readonly IResponseSender _sender;
    private readonly IBotStageRepository _stageRepo;
    private readonly IPermissionRepository _permRepo;
    private readonly ITelegramUserRepository _userRepo;
    private readonly IUserConversationStateStore _stateStore;

    public DynamicStageHandler(
        IResponseSender sender,
        IBotStageRepository stageRepo,
        IPermissionRepository permRepo,
        ITelegramUserRepository userRepo,
        IUserConversationStateStore stateStore)
    {
        _sender = sender;
        _stageRepo = stageRepo;
        _permRepo = permRepo;
        _userRepo = userRepo;
        _stateStore = stateStore;
    }

    public string? Command => null;

    public bool CanHandle(BotUpdateContext context)
    {
        if (context.UserId == null) return false;
        var data = context.MessageText?.Trim();
        if (context.IsCallbackQuery && data != null)
        {
            return data.StartsWith("stage:", StringComparison.OrdinalIgnoreCase)
                || data.StartsWith("lang:", StringComparison.OrdinalIgnoreCase);
        }
        // Also handle /settings and /menu commands
        var cmd = context.Command;
        if (string.Equals(cmd, "settings", StringComparison.OrdinalIgnoreCase)
            || string.Equals(cmd, "menu", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    public async Task<bool> HandleAsync(BotUpdateContext context, CancellationToken cancellationToken)
    {
        var userId = context.UserId!.Value;
        var data = context.MessageText?.Trim() ?? "";
        var editMessageId = context.IsCallbackQuery ? context.CallbackMessageId : null;

        // Answer callback to remove loading spinner
        if (context.IsCallbackQuery && context.CallbackQueryId != null)
            await _sender.AnswerCallbackQueryAsync(context.CallbackQueryId, null, cancellationToken).ConfigureAwait(false);

        // Handle language selection
        if (data.StartsWith("lang:", StringComparison.OrdinalIgnoreCase))
        {
            var code = data["lang:".Length..].Trim();
            if (code.Length > 0)
            {
                await _userRepo.UpdateProfileAsync(userId, null, null, code, cancellationToken).ConfigureAwait(false);
                // After setting language, show main_menu
                await ShowStageAsync(userId, "main_menu", editMessageId, code, cancellationToken).ConfigureAwait(false);
            }
            return true;
        }

        // /settings or /menu → show main_menu stage
        if (string.Equals(context.Command, "settings", StringComparison.OrdinalIgnoreCase)
            || string.Equals(context.Command, "menu", StringComparison.OrdinalIgnoreCase))
        {
            await ShowStageAsync(userId, "main_menu", editMessageId, null, cancellationToken).ConfigureAwait(false);
            return true;
        }

        // Handle stage:xxx callback
        if (data.StartsWith("stage:", StringComparison.OrdinalIgnoreCase))
        {
            var stageKey = data["stage:".Length..].Trim();
            if (stageKey.Length > 0)
            {
                // Special handling for profile stage: set conversation state
                if (string.Equals(stageKey, "profile", StringComparison.OrdinalIgnoreCase))
                {
                    await _stateStore.SetStateAsync(userId, "awaiting_profile_name", cancellationToken).ConfigureAwait(false);
                }
                await ShowStageAsync(userId, stageKey, editMessageId, null, cancellationToken).ConfigureAwait(false);
            }
            return true;
        }

        return false;
    }

    private async Task ShowStageAsync(long userId, string stageKey, int? editMessageId, string? langOverride, CancellationToken cancellationToken)
    {
        var user = await _userRepo.GetByTelegramUserIdAsync(userId, cancellationToken).ConfigureAwait(false);
        var lang = langOverride ?? user?.PreferredLanguage ?? "fa";
        var isFa = lang == "fa";

        var stage = await _stageRepo.GetByKeyAsync(stageKey, cancellationToken).ConfigureAwait(false);
        if (stage == null)
        {
            var notFound = isFa ? "این بخش یافت نشد." : "Section not found.";
            await SendOrEditTextAsync(userId, notFound, Array.Empty<IReadOnlyList<InlineButton>>(), editMessageId, cancellationToken).ConfigureAwait(false);
            return;
        }

        // Check if stage is enabled
        if (!stage.IsEnabled)
        {
            var disabled = isFa ? "این بخش در حال حاضر غیرفعال است." : "This section is currently disabled.";
            await SendOrEditTextAsync(userId, disabled, Array.Empty<IReadOnlyList<InlineButton>>(), editMessageId, cancellationToken).ConfigureAwait(false);
            return;
        }

        // Check stage-level permission
        if (!string.IsNullOrEmpty(stage.RequiredPermission))
        {
            var hasAccess = await _permRepo.UserHasPermissionAsync(userId, stage.RequiredPermission, cancellationToken).ConfigureAwait(false);
            if (!hasAccess)
            {
                var denied = isFa ? "شما دسترسی به این بخش ندارید." : "You don't have access to this section.";
                await SendOrEditTextAsync(userId, denied, Array.Empty<IReadOnlyList<InlineButton>>(), editMessageId, cancellationToken).ConfigureAwait(false);
                return;
            }
        }

        // Build text
        var text = isFa ? (stage.TextFa ?? stage.TextEn ?? stageKey) : (stage.TextEn ?? stage.TextFa ?? stageKey);

        // Load and filter buttons
        var allButtons = await _stageRepo.GetButtonsAsync(stageKey, cancellationToken).ConfigureAwait(false);
        var userPerms = await _permRepo.GetUserPermissionsAsync(userId, cancellationToken).ConfigureAwait(false);
        var permSet = new HashSet<string>(userPerms, StringComparer.OrdinalIgnoreCase);

        var visibleButtons = new List<BotStageButtonDto>();
        foreach (var btn in allButtons)
        {
            if (!btn.IsEnabled) continue;
            if (!string.IsNullOrEmpty(btn.RequiredPermission) && !permSet.Contains(btn.RequiredPermission)) continue;
            visibleButtons.Add(btn);
        }

        // Group by row and build keyboard
        var keyboard = new List<IReadOnlyList<InlineButton>>();
        var rows = visibleButtons.GroupBy(b => b.Row).OrderBy(g => g.Key);
        foreach (var row in rows)
        {
            var rowButtons = new List<InlineButton>();
            foreach (var btn in row.OrderBy(b => b.Column))
            {
                var btnText = isFa ? (btn.TextFa ?? btn.TextEn ?? "?") : (btn.TextEn ?? btn.TextFa ?? "?");
                var callbackData = btn.CallbackData;
                // If TargetStageKey is set and CallbackData is empty, auto-generate callback
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

        // Auto back-button if ParentStageKey is set and not already in buttons
        if (!string.IsNullOrEmpty(stage.ParentStageKey))
        {
            var hasBack = visibleButtons.Any(b =>
                b.TargetStageKey == stage.ParentStageKey ||
                b.CallbackData == $"stage:{stage.ParentStageKey}");
            if (!hasBack)
            {
                var backLabel = isFa ? "◀ بازگشت" : "◀ Back";
                keyboard.Add(new[] { new InlineButton(backLabel, $"stage:{stage.ParentStageKey}") });
            }
        }

        await SendOrEditTextAsync(userId, text, keyboard, editMessageId, cancellationToken).ConfigureAwait(false);
    }

    private async Task SendOrEditTextAsync(long chatId, string text, IReadOnlyList<IReadOnlyList<InlineButton>> keyboard, int? editMessageId, CancellationToken cancellationToken)
    {
        if (editMessageId.HasValue)
            await _sender.EditMessageTextWithInlineKeyboardAsync(chatId, editMessageId.Value, text, keyboard, cancellationToken).ConfigureAwait(false);
        else
            await _sender.SendTextMessageWithInlineKeyboardAsync(chatId, text, keyboard, cancellationToken).ConfigureAwait(false);
    }
}
