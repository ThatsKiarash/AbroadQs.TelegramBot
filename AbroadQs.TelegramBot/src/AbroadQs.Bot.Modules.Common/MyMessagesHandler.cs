using AbroadQs.Bot.Contracts;
using static AbroadQs.Bot.Contracts.BilingualHelper;

namespace AbroadQs.Bot.Modules.Common;

/// <summary>
/// Phase 4: My Messages — view system notifications, mark as read.
/// Callback prefix: msg_
/// </summary>
public sealed class MyMessagesHandler : IUpdateHandler
{
    private readonly IResponseSender _sender;
    private readonly ITelegramUserRepository _userRepo;
    private readonly ISystemMessageRepository? _msgRepo;

    public MyMessagesHandler(IResponseSender sender, ITelegramUserRepository userRepo, ISystemMessageRepository? msgRepo = null)
    {
        _sender = sender; _userRepo = userRepo; _msgRepo = msgRepo;
    }

    public string? Command => null;

    public bool CanHandle(BotUpdateContext context)
    {
        if (context.UserId == null || !context.IsCallbackQuery) return false;
        var cb = context.MessageText?.Trim() ?? "";
        return cb.StartsWith("msg_", StringComparison.Ordinal);
    }

    public async Task<bool> HandleAsync(BotUpdateContext context, CancellationToken ct)
    {
        if (context.UserId == null) return false;
        var userId = context.UserId.Value;
        var chatId = context.ChatId;
        var user = await SafeGetUser(userId, ct);
        var lang = user?.PreferredLanguage;
        var cb = context.MessageText?.Trim() ?? "";
        if (context.CallbackQueryId != null) try { await _sender.AnswerCallbackQueryAsync(context.CallbackQueryId, null, ct).ConfigureAwait(false); } catch { }
        var eid = context.CallbackMessageId;

        if (cb == "msg_menu") { await ShowMenu(chatId, userId, lang, eid, ct); return true; }
        if (cb == "msg_unread") { await ShowList(chatId, userId, lang, true, 0, eid, ct); return true; }
        if (cb == "msg_all") { await ShowList(chatId, userId, lang, false, 0, eid, ct); return true; }
        if (cb.StartsWith("msg_p:")) { var parts = cb["msg_p:".Length..].Split(':'); if (parts.Length >= 2) { bool unread = parts[0] == "u"; int.TryParse(parts[1], out var p); await ShowList(chatId, userId, lang, unread, p, eid, ct); } return true; }
        if (cb.StartsWith("msg_read:")) { int.TryParse(cb["msg_read:".Length..], out var mid); await MarkReadAndShow(chatId, userId, mid, lang, eid, ct); return true; }
        return false;
    }

    public async Task ShowMenu(long chatId, long userId, string? lang, int? editMsgId, CancellationToken ct)
    {
        int unreadCount = 0;
        if (_msgRepo != null) try { unreadCount = await _msgRepo.UnreadCountAsync(userId, ct).ConfigureAwait(false); } catch { }
        var badge = unreadCount > 0 ? $" ({unreadCount})" : "";
        var text = L($"<b>📬 پیام‌های من</b>\n━━━━━━━━━━━━━━━━━━━\n\n📩 خوانده نشده: <b>{unreadCount}</b>",
                     $"<b>📬 My Messages</b>\n━━━━━━━━━━━━━━━━━━━\n\n📩 Unread: <b>{unreadCount}</b>", lang);
        var kb = new List<IReadOnlyList<InlineButton>>
        {
            new[] { new InlineButton(L($"📩 خوانده نشده{badge}", $"📩 Unread{badge}", lang), "msg_unread") },
            new[] { new InlineButton(L("📋 همه پیام‌ها", "📋 All Messages", lang), "msg_all") },
            new[] { new InlineButton(L("🔙 بازگشت", "🔙 Back", lang), "stage:main_menu") },
        };
        if (editMsgId.HasValue) try { await _sender.EditMessageTextWithInlineKeyboardAsync(chatId, editMsgId.Value, text, kb, ct).ConfigureAwait(false); return; } catch { }
        try { await _sender.SendTextMessageWithInlineKeyboardAsync(chatId, text, kb, ct).ConfigureAwait(false); } catch { }
    }

    private async Task ShowList(long chatId, long userId, string? lang, bool unreadOnly, int page, int? editMsgId, CancellationToken ct)
    {
        if (_msgRepo == null) return;
        var messages = await _msgRepo.ListAsync(userId, unreadOnly, page, 10, ct).ConfigureAwait(false);
        var sb = new System.Text.StringBuilder();
        var isUnread = unreadOnly;
        sb.AppendLine(L(isUnread ? "<b>📩 پیام‌های خوانده نشده</b>" : "<b>📋 همه پیام‌ها</b>",
                        isUnread ? "<b>📩 Unread Messages</b>" : "<b>📋 All Messages</b>", lang));
        sb.AppendLine("━━━━━━━━━━━━━━━━━━━\n");
        if (messages.Count == 0) sb.AppendLine(L("📭 پیامی یافت نشد.", "📭 No messages found.", lang));
        var kb = new List<IReadOnlyList<InlineButton>>();
        foreach (var m in messages)
        {
            var icon = m.IsRead ? "📖" : "📩";
            var catIcon = m.Category switch { "warning" => "⚠️", "success" => "✅", _ => "ℹ️" };
            var title = L(m.TitleFa ?? "", m.TitleEn ?? "", lang);
            if (title.Length > 40) title = title[..40] + "...";
            kb.Add(new[] { new InlineButton($"{icon}{catIcon} {title}", $"msg_read:{m.Id}") });
        }
        var filterCode = isUnread ? "u" : "a";
        var nav = new List<InlineButton>();
        if (page > 0) nav.Add(new InlineButton("◀️", $"msg_p:{filterCode}:{page - 1}"));
        if (messages.Count == 10) nav.Add(new InlineButton("▶️", $"msg_p:{filterCode}:{page + 1}"));
        if (nav.Count > 0) kb.Add(nav);
        kb.Add(new[] { new InlineButton(L("🔙 بازگشت", "🔙 Back", lang), "msg_menu") });
        if (editMsgId.HasValue) try { await _sender.EditMessageTextWithInlineKeyboardAsync(chatId, editMsgId.Value, sb.ToString(), kb, ct).ConfigureAwait(false); return; } catch { }
        try { await _sender.SendTextMessageWithInlineKeyboardAsync(chatId, sb.ToString(), kb, ct).ConfigureAwait(false); } catch { }
    }

    private async Task MarkReadAndShow(long chatId, long userId, int messageId, string? lang, int? editMsgId, CancellationToken ct)
    {
        if (_msgRepo == null) return;
        await _msgRepo.MarkAsReadAsync(messageId, ct).ConfigureAwait(false);
        var m = await _msgRepo.GetAsync(messageId, ct).ConfigureAwait(false);
        if (m == null) return;
        var title = L(m.TitleFa ?? "", m.TitleEn ?? "", lang);
        var body = L(m.BodyFa ?? "", m.BodyEn ?? "", lang);
        var catIcon = m.Category switch { "warning" => "⚠️", "success" => "✅", _ => "ℹ️" };
        var text = $"{catIcon} <b>{title}</b>\n━━━━━━━━━━━━━━━━━━━\n\n{body}\n\n📅 {m.CreatedAt:yyyy/MM/dd HH:mm}";
        var kb = new List<IReadOnlyList<InlineButton>> { new[] { new InlineButton(L("🔙 بازگشت", "🔙 Back", lang), "msg_menu") } };
        if (editMsgId.HasValue) try { await _sender.EditMessageTextWithInlineKeyboardAsync(chatId, editMsgId.Value, text, kb, ct).ConfigureAwait(false); return; } catch { }
        try { await _sender.SendTextMessageWithInlineKeyboardAsync(chatId, text, kb, ct).ConfigureAwait(false); } catch { }
    }

    private async Task<TelegramUserDto?> SafeGetUser(long userId, CancellationToken ct)
    { try { return await _userRepo.GetByTelegramUserIdAsync(userId, ct).ConfigureAwait(false); } catch { return null; } }
}
