using AbroadQs.Bot.Contracts;

namespace AbroadQs.Bot.Modules.Common;

/// <summary>
/// Handles callbacks like "stage:xxx" — loads the stage from DB, checks permissions, and displays it.
/// Also handles "lang:xx" callbacks for language selection.
/// Also handles plain text messages that match main_menu reply keyboard buttons.
/// </summary>
public sealed class DynamicStageHandler : IUpdateHandler
{
    private readonly IResponseSender _sender;
    private readonly IBotStageRepository _stageRepo;
    private readonly IPermissionRepository _permRepo;
    private readonly ITelegramUserRepository _userRepo;
    private readonly IUserConversationStateStore _stateStore;
    private readonly IUserMessageStateRepository? _msgStateRepo;

    public DynamicStageHandler(
        IResponseSender sender,
        IBotStageRepository stageRepo,
        IPermissionRepository permRepo,
        ITelegramUserRepository userRepo,
        IUserConversationStateStore stateStore,
        IUserMessageStateRepository? msgStateRepo = null)
    {
        _sender = sender;
        _stageRepo = stageRepo;
        _permRepo = permRepo;
        _userRepo = userRepo;
        _stateStore = stateStore;
        _msgStateRepo = msgStateRepo;
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
        // Handle /settings and /menu commands
        var cmd = context.Command;
        if (string.Equals(cmd, "settings", StringComparison.OrdinalIgnoreCase)
            || string.Equals(cmd, "menu", StringComparison.OrdinalIgnoreCase))
            return true;

        // Handle plain text messages (potential reply keyboard button presses)
        if (!context.IsCallbackQuery && !string.IsNullOrEmpty(data) && string.IsNullOrEmpty(cmd))
            return true;

        return false;
    }

    public async Task<bool> HandleAsync(BotUpdateContext context, CancellationToken cancellationToken)
    {
        var userId = context.UserId!.Value;
        var chatId = context.ChatId;
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
                // Delete the language-selection message so the chat stays clean
                if (editMessageId.HasValue)
                    await _sender.DeleteMessageAsync(chatId, editMessageId.Value, cancellationToken).ConfigureAwait(false);
                // Send fresh main_menu with reply keyboard
                await ShowReplyKeyboardStageAsync(userId, "main_menu", code, cancellationToken).ConfigureAwait(false);
            }
            return true;
        }

        // /settings or /menu → cleanup + show main_menu
        if (string.Equals(context.Command, "settings", StringComparison.OrdinalIgnoreCase)
            || string.Equals(context.Command, "menu", StringComparison.OrdinalIgnoreCase))
        {
            await CleanupChatAsync(chatId, userId, context.IncomingMessageId, cancellationToken).ConfigureAwait(false);
            await ShowReplyKeyboardStageAsync(userId, "main_menu", null, cancellationToken).ConfigureAwait(false);
            return true;
        }

        // Handle stage:xxx callback
        if (data.StartsWith("stage:", StringComparison.OrdinalIgnoreCase))
        {
            var stageKey = data["stage:".Length..].Trim();
            if (stageKey.Length > 0)
            {
                // Reply-keyboard stages: delete inline msg + show reply keyboard
                if (IsReplyKeyboardStage(stageKey))
                {
                    if (editMessageId.HasValue)
                        await _sender.DeleteMessageAsync(chatId, editMessageId.Value, cancellationToken).ConfigureAwait(false);
                    await ShowReplyKeyboardStageAsync(userId, stageKey, null, cancellationToken).ConfigureAwait(false);
                    return true;
                }

                // Special handling for profile stage: set conversation state
                if (string.Equals(stageKey, "profile", StringComparison.OrdinalIgnoreCase))
                {
                    await _stateStore.SetStateAsync(userId, "awaiting_profile_name", cancellationToken).ConfigureAwait(false);
                }
                await ShowStageInlineAsync(userId, stageKey, editMessageId, null, cancellationToken).ConfigureAwait(false);
            }
            return true;
        }

        // Handle plain text messages — match against main_menu reply keyboard buttons
        if (!context.IsCallbackQuery && !string.IsNullOrEmpty(data) && string.IsNullOrEmpty(context.Command))
        {
            var matched = await HandleReplyKeyboardButtonAsync(chatId, userId, data, context.IncomingMessageId, cancellationToken).ConfigureAwait(false);
            return matched;
        }

        return false;
    }

    /// <summary>
    /// Stages that use reply keyboard instead of inline keyboard.
    /// </summary>
    private static readonly HashSet<string> ReplyKeyboardStages = new(StringComparer.OrdinalIgnoreCase)
    {
        "main_menu", "new_request"
    };

    private static bool IsReplyKeyboardStage(string stageKey) =>
        ReplyKeyboardStages.Contains(stageKey);

    /// <summary>
    /// Match a plain text message against buttons of all reply-keyboard stages.
    /// If matched, cleanup chat and navigate to the target stage.
    /// </summary>
    private async Task<bool> HandleReplyKeyboardButtonAsync(long chatId, long userId, string text, int? incomingMessageId, CancellationToken cancellationToken)
    {
        // Check buttons of every reply-keyboard stage
        foreach (var stageKey in ReplyKeyboardStages)
        {
            var allButtons = await _stageRepo.GetButtonsAsync(stageKey, cancellationToken).ConfigureAwait(false);

            foreach (var btn in allButtons)
            {
                if (!btn.IsEnabled) continue;
                var matchFa = btn.TextFa?.Trim();
                var matchEn = btn.TextEn?.Trim();
                if (!string.Equals(text, matchFa, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(text, matchEn, StringComparison.OrdinalIgnoreCase))
                    continue;

                // Determine target stage
                var targetStage = btn.TargetStageKey;
                if (string.IsNullOrEmpty(targetStage) && !string.IsNullOrEmpty(btn.CallbackData))
                {
                    var cb = btn.CallbackData.Trim();
                    if (cb.StartsWith("stage:", StringComparison.OrdinalIgnoreCase))
                        targetStage = cb["stage:".Length..].Trim();
                }

                if (string.IsNullOrEmpty(targetStage)) return false;

                // Cleanup previous messages
                await CleanupChatAsync(chatId, userId, incomingMessageId, cancellationToken).ConfigureAwait(false);

                // If target is a reply-keyboard stage, show reply keyboard
                if (IsReplyKeyboardStage(targetStage))
                {
                    await ShowReplyKeyboardStageAsync(userId, targetStage, null, cancellationToken).ConfigureAwait(false);
                    return true;
                }

                // Special handling for profile stage
                if (string.Equals(targetStage, "profile", StringComparison.OrdinalIgnoreCase))
                    await _stateStore.SetStateAsync(userId, "awaiting_profile_name", cancellationToken).ConfigureAwait(false);

                await ShowStageInlineAsync(userId, targetStage, null, null, cancellationToken).ConfigureAwait(false);
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Delete the user's incoming message and the last bot message to keep the chat clean.
    /// Skips cleanup if user is in a form state (e.g. filling profile, submitting request).
    /// </summary>
    private async Task CleanupChatAsync(long chatId, long userId, int? incomingMessageId, CancellationToken cancellationToken)
    {
        try
        {
            // Don't cleanup if user is in a form state
            var convState = await _stateStore.GetStateAsync(userId, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(convState)) return;

            // Delete user's incoming message
            if (incomingMessageId.HasValue)
                await _sender.DeleteMessageAsync(chatId, incomingMessageId.Value, cancellationToken).ConfigureAwait(false);

            // Delete previous bot message
            if (_msgStateRepo != null)
            {
                var msgState = await _msgStateRepo.GetUserMessageStateAsync(userId, cancellationToken).ConfigureAwait(false);
                if (msgState?.LastBotTelegramMessageId is > 0)
                    await _sender.DeleteMessageAsync(chatId, (int)msgState.LastBotTelegramMessageId, cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            // Swallow cleanup errors — never let cleanup break the main flow
        }
    }

    /// <summary>
    /// Show a stage with reply keyboard (persistent buttons at bottom of chat).
    /// Works for main_menu, new_request, or any reply-keyboard stage.
    /// </summary>
    private async Task ShowReplyKeyboardStageAsync(long userId, string stageKey, string? langOverride, CancellationToken cancellationToken)
    {
        var user = await _userRepo.GetByTelegramUserIdAsync(userId, cancellationToken).ConfigureAwait(false);
        var lang = langOverride ?? user?.PreferredLanguage ?? "fa";
        var isFa = lang == "fa";

        var stage = await _stageRepo.GetByKeyAsync(stageKey, cancellationToken).ConfigureAwait(false);
        var text = stage != null && stage.IsEnabled
            ? (isFa ? (stage.TextFa ?? stage.TextEn ?? stageKey) : (stage.TextEn ?? stage.TextFa ?? stageKey))
            : stageKey;

        // Build reply keyboard from DB buttons
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

        await _sender.SendTextMessageWithReplyKeyboardAsync(userId, text, keyboard, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Show any non-main_menu stage with inline keyboard (as before).
    /// </summary>
    private async Task ShowStageInlineAsync(long userId, string stageKey, int? editMessageId, string? langOverride, CancellationToken cancellationToken)
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

        if (!stage.IsEnabled)
        {
            var disabled = isFa ? "این بخش در حال حاضر غیرفعال است." : "This section is currently disabled.";
            await SendOrEditTextAsync(userId, disabled, Array.Empty<IReadOnlyList<InlineButton>>(), editMessageId, cancellationToken).ConfigureAwait(false);
            return;
        }

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

        var text = isFa ? (stage.TextFa ?? stage.TextEn ?? stageKey) : (stage.TextEn ?? stage.TextFa ?? stageKey);

        // Build inline keyboard
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

        // Auto back-button: navigate to parent stage
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
