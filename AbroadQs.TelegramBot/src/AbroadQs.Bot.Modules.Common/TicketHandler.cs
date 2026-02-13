using AbroadQs.Bot.Contracts;
using static AbroadQs.Bot.Contracts.BilingualHelper;

namespace AbroadQs.Bot.Modules.Common;

/// <summary>
/// Phase 4: Support ticket system — create, view, reply to tickets.
/// Callback prefix: tkt_   States: tkt_new_subject, tkt_new_message, tkt_reply
/// </summary>
public sealed class TicketHandler : IUpdateHandler
{
    private readonly IResponseSender _sender;
    private readonly ITelegramUserRepository _userRepo;
    private readonly IUserConversationStateStore _stateStore;
    private readonly ITicketRepository? _ticketRepo;
    private readonly IUserMessageStateRepository? _msgStateRepo;

    public TicketHandler(IResponseSender sender, ITelegramUserRepository userRepo,
        IUserConversationStateStore stateStore, ITicketRepository? ticketRepo = null,
        IUserMessageStateRepository? msgStateRepo = null)
    {
        _sender = sender; _userRepo = userRepo; _stateStore = stateStore;
        _ticketRepo = ticketRepo; _msgStateRepo = msgStateRepo;
    }

    public string? Command => null;

    public bool CanHandle(BotUpdateContext context)
    {
        if (context.UserId == null) return false;
        if (context.IsCallbackQuery)
            return (context.MessageText?.Trim() ?? "").StartsWith("tkt_", StringComparison.Ordinal);
        // Text is handled via DynamicStageHandler state-based delegation
        return false;
    }

    public async Task<bool> HandleAsync(BotUpdateContext context, CancellationToken ct)
    {
        if (context.UserId == null) return false;
        var userId = context.UserId.Value;
        var chatId = context.ChatId;
        var user = await SafeGetUser(userId, ct);
        var lang = user?.PreferredLanguage;

        if (context.IsCallbackQuery)
        {
            var cb = context.MessageText?.Trim() ?? "";
            await SafeAnswerCallback(context.CallbackQueryId, ct);
            var eid = context.CallbackMessageId;

            if (cb == "tkt_menu") { await ShowTicketsMenu(chatId, userId, lang, eid, ct); return true; }
            if (cb == "tkt_new") { await StartNewTicket(chatId, userId, lang, eid, ct); return true; }
            if (cb == "tkt_list") { await ShowMyTickets(chatId, userId, lang, 0, eid, ct); return true; }
            if (cb.StartsWith("tkt_list_p:")) { int.TryParse(cb["tkt_list_p:".Length..], out var p); await ShowMyTickets(chatId, userId, lang, p, eid, ct); return true; }
            if (cb.StartsWith("tkt_detail:")) { int.TryParse(cb["tkt_detail:".Length..], out var tid); await ShowTicketDetail(chatId, userId, tid, lang, eid, ct); return true; }
            if (cb.StartsWith("tkt_reply:")) { int.TryParse(cb["tkt_reply:".Length..], out var tid2); await StartReply(chatId, userId, tid2, lang, eid, ct); return true; }
            if (cb == "tkt_cancel") { await CancelFlow(chatId, userId, lang, eid, ct); return true; }
            return false;
        }

        var state = await _stateStore.GetStateAsync(userId, ct).ConfigureAwait(false);
        if (state == null || !state.StartsWith("tkt_")) return false;
        var text = context.MessageText?.Trim() ?? "";
        if (string.IsNullOrEmpty(text)) return false;

        if (text.Contains(L("انصراف", "Cancel", lang))) { await CancelFlow(chatId, userId, lang, null, ct); await SafeDelete(chatId, context.IncomingMessageId, ct); return true; }

        return state switch
        {
            "tkt_new_subject" => await HandleSubject(chatId, userId, text, lang, context.IncomingMessageId, ct),
            "tkt_new_message" => await HandleNewMessage(chatId, userId, text, lang, context.IncomingMessageId, ct),
            "tkt_reply" => await HandleReply(chatId, userId, text, lang, context.IncomingMessageId, ct),
            _ => false
        };
    }

    public async Task ShowTicketsMenu(long chatId, long userId, string? lang, int? editMsgId, CancellationToken ct)
    {
        var text = L("<b>🎫 تیکت‌های پشتیبانی</b>\n━━━━━━━━━━━━━━━━━━━\n\nاز این بخش می‌توانید تیکت جدید ایجاد کنید یا تیکت‌های قبلی را مشاهده کنید.",
                     "<b>🎫 Support Tickets</b>\n━━━━━━━━━━━━━━━━━━━\n\nCreate new tickets or view existing ones.", lang);
        var kb = new List<IReadOnlyList<InlineButton>>
        {
            new[] { new InlineButton(L("➕ تیکت جدید", "➕ New Ticket", lang), "tkt_new") },
            new[] { new InlineButton(L("📋 تیکت‌های من", "📋 My Tickets", lang), "tkt_list") },
            new[] { new InlineButton(L("🔙 بازگشت", "🔙 Back", lang), "stage:main_menu") },
        };
        if (editMsgId.HasValue)
        { try { await _sender.EditMessageTextWithInlineKeyboardAsync(chatId, editMsgId.Value, text, kb, ct).ConfigureAwait(false); return; } catch { } }
        try { await _sender.RemoveReplyKeyboardSilentAsync(chatId, ct).ConfigureAwait(false); } catch { }
        try { await _sender.SendTextMessageWithInlineKeyboardAsync(chatId, text, kb, ct).ConfigureAwait(false); } catch { }
    }

    public async Task HandleCallbackAction(long chatId, long userId, string action, int? editMsgId, CancellationToken ct)
    {
        var user = await SafeGetUser(userId, ct);
        var lang = user?.PreferredLanguage;
        switch (action)
        {
            case "tkt_new": await StartNewTicket(chatId, userId, lang, editMsgId, ct); break;
            case "tkt_list": await ShowMyTickets(chatId, userId, lang, 0, editMsgId, ct); break;
        }
    }

    private async Task StartNewTicket(long chatId, long userId, string? lang, int? editMsgId, CancellationToken ct)
    {
        await SafeDelete(chatId, editMsgId, ct);
        await _stateStore.ClearAllFlowDataAsync(userId, ct).ConfigureAwait(false);
        await _stateStore.SetStateAsync(userId, "tkt_new_subject", ct).ConfigureAwait(false);
        var msg = L("<b>➕ تیکت جدید — موضوع</b>\n━━━━━━━━━━━━━━━━━━━\n\nموضوع تیکت را وارد کنید:",
                    "<b>➕ New Ticket — Subject</b>\n━━━━━━━━━━━━━━━━━━━\n\nEnter the ticket subject:", lang);
        var kb = new List<IReadOnlyList<string>> { new[] { L("❌ انصراف", "❌ Cancel", lang) } };
        try { await _sender.SendTextMessageWithReplyKeyboardAsync(chatId, msg, kb, ct).ConfigureAwait(false); } catch { }
    }

    private async Task<bool> HandleSubject(long chatId, long userId, string text, string? lang, int? userMsgId, CancellationToken ct)
    {
        await SafeDelete(chatId, userMsgId, ct);
        await DeletePrevBotMsg(chatId, userId, ct);
        await _stateStore.SetFlowDataAsync(userId, "tkt_subject", text, ct).ConfigureAwait(false);
        await _stateStore.SetStateAsync(userId, "tkt_new_message", ct).ConfigureAwait(false);
        var msg = L("<b>➕ تیکت جدید — پیام</b>\n━━━━━━━━━━━━━━━━━━━\n\nمتن پیام خود را وارد کنید:",
                    "<b>➕ New Ticket — Message</b>\n━━━━━━━━━━━━━━━━━━━\n\nEnter your message:", lang);
        var kb = new List<IReadOnlyList<string>> { new[] { L("❌ انصراف", "❌ Cancel", lang) } };
        try { await _sender.SendTextMessageWithReplyKeyboardAsync(chatId, msg, kb, ct).ConfigureAwait(false); } catch { }
        return true;
    }

    private async Task<bool> HandleNewMessage(long chatId, long userId, string text, string? lang, int? userMsgId, CancellationToken ct)
    {
        if (_ticketRepo == null) return false;
        await SafeDelete(chatId, userMsgId, ct);
        await DeletePrevBotMsg(chatId, userId, ct);
        await _stateStore.ClearStateAsync(userId, ct).ConfigureAwait(false);
        try { await _sender.RemoveReplyKeyboardSilentAsync(chatId, ct).ConfigureAwait(false); } catch { }

        var subject = await _stateStore.GetFlowDataAsync(userId, "tkt_subject", ct).ConfigureAwait(false) ?? "";
        await _stateStore.ClearAllFlowDataAsync(userId, ct).ConfigureAwait(false);

        var user = await SafeGetUser(userId, ct);
        var displayName = $"{user?.FirstName} {user?.LastName}".Trim();
        if (string.IsNullOrEmpty(displayName)) displayName = user?.Username ?? $"User_{userId}";

        var ticket = await _ticketRepo.CreateTicketAsync(new TicketDto(0, userId, subject, "open", "medium", null, default, default, null), ct).ConfigureAwait(false);
        await _ticketRepo.AddMessageAsync(new TicketMessageDto(0, ticket.Id, "user", displayName, text, null, default), ct).ConfigureAwait(false);

        var msg = L($"<b>✅ تیکت #{ticket.Id} ایجاد شد</b>\n━━━━━━━━━━━━━━━━━━━\n\n📋 موضوع: {subject}\n\nتیم پشتیبانی به زودی پاسخ خواهد داد.",
                    $"<b>✅ Ticket #{ticket.Id} Created</b>\n━━━━━━━━━━━━━━━━━━━\n\n📋 Subject: {subject}\n\nOur support team will respond soon.", lang);
        var kb = new List<IReadOnlyList<InlineButton>>
        {
            new[] { new InlineButton(L("📋 مشاهده تیکت", "📋 View Ticket", lang), $"tkt_detail:{ticket.Id}") },
            new[] { new InlineButton(L("🔙 بازگشت", "🔙 Back", lang), "tkt_menu") },
        };
        try { await _sender.SendTextMessageWithInlineKeyboardAsync(chatId, msg, kb, ct).ConfigureAwait(false); } catch { }
        return true;
    }

    private async Task ShowMyTickets(long chatId, long userId, string? lang, int page, int? editMsgId, CancellationToken ct)
    {
        if (_ticketRepo == null) return;
        var tickets = await _ticketRepo.ListTicketsAsync(userId, null, page, 10, ct).ConfigureAwait(false);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(L("<b>📋 تیکت‌های من</b>", "<b>📋 My Tickets</b>", lang));
        sb.AppendLine("━━━━━━━━━━━━━━━━━━━\n");
        if (tickets.Count == 0) sb.AppendLine(L("📭 تیکتی یافت نشد.", "📭 No tickets found.", lang));
        var kb = new List<IReadOnlyList<InlineButton>>();
        foreach (var t in tickets)
        {
            var statusIcon = t.Status == "open" ? "🟢" : t.Status == "in_progress" ? "🟡" : "✅";
            kb.Add(new[] { new InlineButton($"{statusIcon} #{t.Id} — {t.Subject[..Math.Min(30, t.Subject.Length)]}", $"tkt_detail:{t.Id}") });
        }
        var nav = new List<InlineButton>();
        if (page > 0) nav.Add(new InlineButton("◀️", $"tkt_list_p:{page - 1}"));
        if (tickets.Count == 10) nav.Add(new InlineButton("▶️", $"tkt_list_p:{page + 1}"));
        if (nav.Count > 0) kb.Add(nav);
        kb.Add(new[] { new InlineButton(L("🔙 بازگشت", "🔙 Back", lang), "tkt_menu") });

        if (editMsgId.HasValue)
        { try { await _sender.EditMessageTextWithInlineKeyboardAsync(chatId, editMsgId.Value, sb.ToString(), kb, ct).ConfigureAwait(false); return; } catch { } }
        try { await _sender.RemoveReplyKeyboardSilentAsync(chatId, ct).ConfigureAwait(false); } catch { }
        try { await _sender.SendTextMessageWithInlineKeyboardAsync(chatId, sb.ToString(), kb, ct).ConfigureAwait(false); } catch { }
    }

    private async Task ShowTicketDetail(long chatId, long userId, int ticketId, string? lang, int? editMsgId, CancellationToken ct)
    {
        if (_ticketRepo == null) return;
        var ticket = await _ticketRepo.GetTicketAsync(ticketId, ct).ConfigureAwait(false);
        if (ticket == null || ticket.TelegramUserId != userId) return;
        var messages = await _ticketRepo.GetMessagesAsync(ticketId, ct).ConfigureAwait(false);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine(L($"<b>🎫 تیکت #{ticket.Id}</b>", $"<b>🎫 Ticket #{ticket.Id}</b>", lang));
        sb.AppendLine("━━━━━━━━━━━━━━━━━━━");
        sb.AppendLine(L($"\n📋 موضوع: {ticket.Subject}", $"\n📋 Subject: {ticket.Subject}", lang));
        var statusLabel = ticket.Status == "open" ? L("🟢 باز", "🟢 Open", lang) : ticket.Status == "in_progress" ? L("🟡 در حال بررسی", "🟡 In Progress", lang) : L("✅ بسته", "✅ Closed", lang);
        sb.AppendLine(L($"📌 وضعیت: {statusLabel}", $"📌 Status: {statusLabel}", lang));
        sb.AppendLine();

        foreach (var m in messages.TakeLast(10))
        {
            var icon = m.SenderType == "admin" ? "👤" : "🧑";
            sb.AppendLine($"{icon} <b>{m.SenderName ?? m.SenderType}</b> — {m.CreatedAt:MM/dd HH:mm}");
            sb.AppendLine($"   {m.Text}\n");
        }

        var kb = new List<IReadOnlyList<InlineButton>>();
        if (ticket.Status != "closed")
            kb.Add(new[] { new InlineButton(L("💬 پاسخ", "💬 Reply", lang), $"tkt_reply:{ticket.Id}") });
        kb.Add(new[] { new InlineButton(L("🔙 بازگشت", "🔙 Back", lang), "tkt_list") });

        if (editMsgId.HasValue)
        { try { await _sender.EditMessageTextWithInlineKeyboardAsync(chatId, editMsgId.Value, sb.ToString(), kb, ct).ConfigureAwait(false); return; } catch { } }
        try { await _sender.SendTextMessageWithInlineKeyboardAsync(chatId, sb.ToString(), kb, ct).ConfigureAwait(false); } catch { }
    }

    private async Task StartReply(long chatId, long userId, int ticketId, string? lang, int? editMsgId, CancellationToken ct)
    {
        await SafeDelete(chatId, editMsgId, ct);
        await _stateStore.SetFlowDataAsync(userId, "tkt_reply_id", ticketId.ToString(), ct).ConfigureAwait(false);
        await _stateStore.SetStateAsync(userId, "tkt_reply", ct).ConfigureAwait(false);
        var msg = L("<b>💬 پاسخ تیکت</b>\n━━━━━━━━━━━━━━━━━━━\n\nپیام خود را بنویسید:",
                    "<b>💬 Reply to Ticket</b>\n━━━━━━━━━━━━━━━━━━━\n\nWrite your message:", lang);
        var kb = new List<IReadOnlyList<string>> { new[] { L("❌ انصراف", "❌ Cancel", lang) } };
        try { await _sender.SendTextMessageWithReplyKeyboardAsync(chatId, msg, kb, ct).ConfigureAwait(false); } catch { }
    }

    private async Task<bool> HandleReply(long chatId, long userId, string text, string? lang, int? userMsgId, CancellationToken ct)
    {
        if (_ticketRepo == null) return false;
        await SafeDelete(chatId, userMsgId, ct);
        await DeletePrevBotMsg(chatId, userId, ct);

        var tidStr = await _stateStore.GetFlowDataAsync(userId, "tkt_reply_id", ct).ConfigureAwait(false) ?? "0";
        int.TryParse(tidStr, out var ticketId);
        await _stateStore.ClearStateAsync(userId, ct).ConfigureAwait(false);
        await _stateStore.ClearAllFlowDataAsync(userId, ct).ConfigureAwait(false);
        try { await _sender.RemoveReplyKeyboardSilentAsync(chatId, ct).ConfigureAwait(false); } catch { }

        var user = await SafeGetUser(userId, ct);
        var displayName = $"{user?.FirstName} {user?.LastName}".Trim();
        if (string.IsNullOrEmpty(displayName)) displayName = user?.Username ?? $"User_{userId}";

        await _ticketRepo.AddMessageAsync(new TicketMessageDto(0, ticketId, "user", displayName, text, null, default), ct).ConfigureAwait(false);

        await ShowTicketDetail(chatId, userId, ticketId, lang, null, ct);
        return true;
    }

    private async Task CancelFlow(long chatId, long userId, string? lang, int? editMsgId, CancellationToken ct)
    {
        await _stateStore.ClearStateAsync(userId, ct).ConfigureAwait(false);
        await _stateStore.ClearAllFlowDataAsync(userId, ct).ConfigureAwait(false);
        await SafeDelete(chatId, editMsgId, ct);
        try { await _sender.RemoveReplyKeyboardSilentAsync(chatId, ct).ConfigureAwait(false); } catch { }
        await ShowTicketsMenu(chatId, userId, lang, null, ct);
    }

    private async Task<TelegramUserDto?> SafeGetUser(long userId, CancellationToken ct)
    { try { return await _userRepo.GetByTelegramUserIdAsync(userId, ct).ConfigureAwait(false); } catch { return null; } }
    private async Task SafeDelete(long chatId, int? msgId, CancellationToken ct)
    { if (msgId.HasValue) try { await _sender.DeleteMessageAsync(chatId, msgId.Value, ct).ConfigureAwait(false); } catch { } }
    private async Task SafeAnswerCallback(string? id, CancellationToken ct)
    { if (id != null) try { await _sender.AnswerCallbackQueryAsync(id, null, ct).ConfigureAwait(false); } catch { } }
    private async Task DeletePrevBotMsg(long chatId, long userId, CancellationToken ct)
    { if (_msgStateRepo == null) return; try { var s = await _msgStateRepo.GetUserMessageStateAsync(userId, ct).ConfigureAwait(false); if (s?.LastBotTelegramMessageId is > 0) await SafeDelete(chatId, (int)s.LastBotTelegramMessageId, ct); } catch { } }
}
