using AbroadQs.Bot.Contracts;

namespace AbroadQs.Bot.Modules.Common;

/// <summary>
/// Handles callbacks like "stage:xxx" — loads the stage from DB, checks permissions, and displays it.
/// Also handles "lang:xx" callbacks for language selection.
/// Also handles plain text messages that match reply keyboard buttons.
///
/// Message transition rules:
///   • Same type (inline → inline)      : editMessageText in-place
///   • Same type (reply-kb → reply-kb)   : editMessageText + silent keyboard update (phantom)
///   • Type change (reply-kb → inline)   : delete reply-kb msg, send new inline msg
///   • Type change (inline → reply-kb)   : delete inline msg, send new reply-kb msg
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
        var cmd = context.Command;
        if (string.Equals(cmd, "settings", StringComparison.OrdinalIgnoreCase)
            || string.Equals(cmd, "menu", StringComparison.OrdinalIgnoreCase))
            return true;

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

        // ── lang:xx callback ──────────────────────────────────────────
        if (data.StartsWith("lang:", StringComparison.OrdinalIgnoreCase))
        {
            var code = data["lang:".Length..].Trim();
            if (code.Length > 0)
            {
                await _userRepo.UpdateProfileAsync(userId, null, null, code, cancellationToken).ConfigureAwait(false);
                // Type change: inline → reply-kb
                if (editMessageId.HasValue)
                    await _sender.DeleteMessageAsync(chatId, editMessageId.Value, cancellationToken).ConfigureAwait(false);
                await ShowReplyKeyboardStageAsync(userId, "main_menu", code, null, cancellationToken).ConfigureAwait(false);
            }
            return true;
        }

        // ── /settings or /menu command ────────────────────────────────
        if (string.Equals(context.Command, "settings", StringComparison.OrdinalIgnoreCase)
            || string.Equals(context.Command, "menu", StringComparison.OrdinalIgnoreCase))
        {
            await TryDeleteAsync(chatId, context.IncomingMessageId, cancellationToken).ConfigureAwait(false);
            // Same type (reply-kb → reply-kb): edit text + update keyboard
            var oldBotMsgId = await GetOldBotMessageIdAsync(userId, cancellationToken).ConfigureAwait(false);
            await ShowReplyKeyboardStageAsync(userId, "main_menu", null, oldBotMsgId, cancellationToken).ConfigureAwait(false);
            return true;
        }

        // ── stage:xxx callback (inline button press) ──────────────────
        if (data.StartsWith("stage:", StringComparison.OrdinalIgnoreCase))
        {
            var stageKey = data["stage:".Length..].Trim();
            if (stageKey.Length > 0)
            {
                if (IsReplyKeyboardStage(stageKey))
                {
                    // Type change: inline → reply-kb
                    if (editMessageId.HasValue)
                        await _sender.DeleteMessageAsync(chatId, editMessageId.Value, cancellationToken).ConfigureAwait(false);
                    await ShowReplyKeyboardStageAsync(userId, stageKey, null, null, cancellationToken).ConfigureAwait(false);
                    return true;
                }

                if (string.Equals(stageKey, "profile", StringComparison.OrdinalIgnoreCase))
                    await _stateStore.SetStateAsync(userId, "awaiting_profile_name", cancellationToken).ConfigureAwait(false);

                // Same type: inline → inline: edit in place
                await ShowStageInlineAsync(userId, stageKey, editMessageId, null, cancellationToken).ConfigureAwait(false);
            }
            return true;
        }

        // ── Plain text → match against reply keyboard buttons ──────────
        if (!context.IsCallbackQuery && !string.IsNullOrEmpty(data) && string.IsNullOrEmpty(context.Command))
        {
            var matched = await HandleReplyKeyboardButtonAsync(chatId, userId, data, context.IncomingMessageId, cancellationToken).ConfigureAwait(false);
            return matched;
        }

        return false;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Stage type registry
    // ═══════════════════════════════════════════════════════════════════

    private static readonly HashSet<string> ReplyKeyboardStages = new(StringComparer.OrdinalIgnoreCase)
    {
        "main_menu", "new_request"
    };

    private static bool IsReplyKeyboardStage(string stageKey) =>
        ReplyKeyboardStages.Contains(stageKey);

    // ═══════════════════════════════════════════════════════════════════
    //  Reply keyboard button handler
    // ═══════════════════════════════════════════════════════════════════

    private async Task<bool> HandleReplyKeyboardButtonAsync(long chatId, long userId, string text, int? incomingMessageId, CancellationToken cancellationToken)
    {
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

                var targetStage = btn.TargetStageKey;
                if (string.IsNullOrEmpty(targetStage) && !string.IsNullOrEmpty(btn.CallbackData))
                {
                    var cb = btn.CallbackData.Trim();
                    if (cb.StartsWith("stage:", StringComparison.OrdinalIgnoreCase))
                        targetStage = cb["stage:".Length..].Trim();
                }

                if (string.IsNullOrEmpty(targetStage)) return false;

                // Delete user's incoming text message
                await TryDeleteAsync(chatId, incomingMessageId, cancellationToken).ConfigureAwait(false);

                var oldBotMsgId = await GetOldBotMessageIdAsync(userId, cancellationToken).ConfigureAwait(false);

                if (IsReplyKeyboardStage(targetStage))
                {
                    // Same type (reply-kb → reply-kb): edit text in place + silent keyboard update
                    await ShowReplyKeyboardStageAsync(userId, targetStage, null, oldBotMsgId, cancellationToken).ConfigureAwait(false);
                    return true;
                }

                // Type change (reply-kb → inline): delete reply-kb msg, send new inline
                if (string.Equals(targetStage, "profile", StringComparison.OrdinalIgnoreCase))
                    await _stateStore.SetStateAsync(userId, "awaiting_profile_name", cancellationToken).ConfigureAwait(false);

                await TryDeleteAsync(chatId, oldBotMsgId, cancellationToken).ConfigureAwait(false);
                await ShowStageInlineAsync(userId, targetStage, null, null, cancellationToken).ConfigureAwait(false);
                return true;
            }
        }
        return false;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Helpers
    // ═══════════════════════════════════════════════════════════════════

    private async Task<int?> GetOldBotMessageIdAsync(long userId, CancellationToken cancellationToken)
    {
        if (_msgStateRepo == null) return null;
        try
        {
            var msgState = await _msgStateRepo.GetUserMessageStateAsync(userId, cancellationToken).ConfigureAwait(false);
            return msgState?.LastBotTelegramMessageId is > 0 ? (int)msgState.LastBotTelegramMessageId : null;
        }
        catch { return null; }
    }

    private async Task TryDeleteAsync(long chatId, int? messageId, CancellationToken cancellationToken)
    {
        if (!messageId.HasValue) return;
        try { await _sender.DeleteMessageAsync(chatId, messageId.Value, cancellationToken).ConfigureAwait(false); }
        catch { /* swallow */ }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Stage renderers
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Show a stage with reply keyboard.
    /// If editMessageId is provided → edit text in place + silently update keyboard (phantom).
    /// If editMessageId is null → send new message with text + keyboard.
    /// </summary>
    private async Task ShowReplyKeyboardStageAsync(long userId, string stageKey, string? langOverride, int? editMessageId, CancellationToken cancellationToken)
    {
        var user = await _userRepo.GetByTelegramUserIdAsync(userId, cancellationToken).ConfigureAwait(false);
        var lang = langOverride ?? user?.PreferredLanguage ?? "fa";
        var isFa = lang == "fa";

        var stage = await _stageRepo.GetByKeyAsync(stageKey, cancellationToken).ConfigureAwait(false);
        var text = stage != null && stage.IsEnabled
            ? (isFa ? (stage.TextFa ?? stage.TextEn ?? stageKey) : (stage.TextEn ?? stage.TextFa ?? stageKey))
            : stageKey;

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

        if (editMessageId.HasValue)
        {
            // EDIT mode: edit text in place + silently update keyboard via phantom
            try
            {
                await _sender.EditMessageTextAsync(userId, editMessageId.Value, text, cancellationToken).ConfigureAwait(false);
                await _sender.UpdateReplyKeyboardSilentAsync(userId, keyboard, cancellationToken).ConfigureAwait(false);
                return; // success
            }
            catch
            {
                // Edit failed — fall back to sending a new message
            }
        }

        // SEND mode: send plain text (no markup) + set keyboard via phantom
        // This ensures the message is editable next time (messages with ReplyKeyboardMarkup can't be edited)
        await _sender.SendTextMessageAsync(userId, text, cancellationToken).ConfigureAwait(false);
        await _sender.UpdateReplyKeyboardSilentAsync(userId, keyboard, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Show a stage with inline keyboard. If editMessageId is provided, edits in-place.
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

        // Auto back-button
        if (!string.IsNullOrEmpty(stage.ParentStageKey))
        {
            var hasBack = visibleButtons.Any(b =>
                b.TargetStageKey == stage.ParentStageKey ||
                b.CallbackData == $"stage:{stage.ParentStageKey}");
            if (!hasBack)
            {
                var backLabel = isFa ? "بازگشت" : "Back";
                keyboard.Add(new[] { new InlineButton(backLabel, $"stage:{stage.ParentStageKey}") });
            }
        }

        await SendOrEditTextAsync(userId, text, keyboard, editMessageId, cancellationToken).ConfigureAwait(false);
    }

    private async Task SendOrEditTextAsync(long chatId, string text, IReadOnlyList<IReadOnlyList<InlineButton>> keyboard, int? editMessageId, CancellationToken cancellationToken)
    {
        if (editMessageId.HasValue)
        {
            try
            {
                await _sender.EditMessageTextWithInlineKeyboardAsync(chatId, editMessageId.Value, text, keyboard, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch
            {
                // Edit failed — fall back to sending new message
            }
        }
        await _sender.SendTextMessageWithInlineKeyboardAsync(chatId, text, keyboard, cancellationToken).ConfigureAwait(false);
    }
}
