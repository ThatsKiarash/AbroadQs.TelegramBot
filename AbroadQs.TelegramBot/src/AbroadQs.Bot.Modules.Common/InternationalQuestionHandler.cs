using AbroadQs.Bot.Contracts;
using static AbroadQs.Bot.Contracts.BilingualHelper;

namespace AbroadQs.Bot.Modules.Common;

/// <summary>
/// Phase 6: International Questions — post questions with bounty, browse, answer.
/// Callback prefix: iq_   States: iq_text, iq_country, iq_bounty, iq_preview, iq_answer
/// </summary>
public sealed class InternationalQuestionHandler : IUpdateHandler
{
    private readonly IResponseSender _sender;
    private readonly ITelegramUserRepository _userRepo;
    private readonly IUserConversationStateStore _stateStore;
    private readonly IInternationalQuestionRepository? _questionRepo;
    private readonly IWalletRepository? _walletRepo;
    private readonly IUserMessageStateRepository? _msgStateRepo;

    public InternationalQuestionHandler(IResponseSender sender, ITelegramUserRepository userRepo,
        IUserConversationStateStore stateStore, IInternationalQuestionRepository? questionRepo = null,
        IWalletRepository? walletRepo = null, IUserMessageStateRepository? msgStateRepo = null)
    {
        _sender = sender; _userRepo = userRepo; _stateStore = stateStore;
        _questionRepo = questionRepo; _walletRepo = walletRepo; _msgStateRepo = msgStateRepo;
    }

    public string? Command => null;

    public bool CanHandle(BotUpdateContext context)
    {
        if (context.UserId == null) return false;
        if (context.IsCallbackQuery)
            return (context.MessageText?.Trim() ?? "").StartsWith("iq_", StringComparison.Ordinal);
        return !string.IsNullOrEmpty(context.MessageText);
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

            if (cb == "iq_menu") { await ShowMenu(chatId, userId, lang, eid, ct); return true; }
            if (cb == "iq_post") { await StartPost(chatId, userId, lang, eid, ct); return true; }
            if (cb == "iq_browse") { await BrowseQuestions(chatId, userId, lang, 0, eid, ct); return true; }
            if (cb.StartsWith("iq_browse_p:")) { int.TryParse(cb["iq_browse_p:".Length..], out var p); await BrowseQuestions(chatId, userId, lang, p, eid, ct); return true; }
            if (cb.StartsWith("iq_detail:")) { int.TryParse(cb["iq_detail:".Length..], out var qid); await ShowDetail(chatId, userId, qid, lang, eid, ct); return true; }
            if (cb.StartsWith("iq_answer:")) { int.TryParse(cb["iq_answer:".Length..], out var qid2); await StartAnswer(chatId, userId, qid2, lang, eid, ct); return true; }
            if (cb.StartsWith("iq_accept_ans:")) { int.TryParse(cb["iq_accept_ans:".Length..], out var aid); await AcceptAnswer(chatId, userId, aid, lang, eid, ct); return true; }
            if (cb == "iq_confirm") { await DoSubmitQuestion(chatId, userId, lang, eid, ct); return true; }
            if (cb == "iq_cancel") { await CancelFlow(chatId, userId, lang, eid, ct); return true; }
            return false;
        }

        var state = await _stateStore.GetStateAsync(userId, ct).ConfigureAwait(false);
        if (state == null || !state.StartsWith("iq_")) return false;
        var text = context.MessageText?.Trim() ?? "";
        if (string.IsNullOrEmpty(text)) return false;
        if (text.Contains(L("انصراف", "Cancel", lang))) { await CancelFlow(chatId, userId, lang, null, ct); await SafeDelete(chatId, context.IncomingMessageId, ct); return true; }

        return state switch
        {
            "iq_text" => await HandleInput(chatId, userId, "iq_text", text, lang, context.IncomingMessageId, ct),
            "iq_country" => await HandleInput(chatId, userId, "iq_country", text, lang, context.IncomingMessageId, ct),
            "iq_bounty" => await HandleInput(chatId, userId, "iq_bounty", text, lang, context.IncomingMessageId, ct),
            "iq_answer" => await HandleAnswerInput(chatId, userId, text, lang, context.IncomingMessageId, ct),
            _ => false
        };
    }

    public async Task ShowMenu(long chatId, long userId, string? lang, int? editMsgId, CancellationToken ct)
    {
        var text = L("<b>❓ سوالات بین‌المللی</b>\n━━━━━━━━━━━━━━━━━━━\n\nسوال بپرسید یا به سوالات دیگران پاسخ دهید.",
                     "<b>❓ International Questions</b>\n━━━━━━━━━━━━━━━━━━━\n\nAsk a question or answer others' questions.", lang);
        var kb = new List<IReadOnlyList<InlineButton>>
        {
            new[] { new InlineButton(L("➕ ثبت سوال", "➕ Ask Question", lang), "iq_post") },
            new[] { new InlineButton(L("📋 مرور سوالات", "📋 Browse Questions", lang), "iq_browse") },
            new[] { new InlineButton(L("🔙 بازگشت", "🔙 Back", lang), "stage:new_request") },
        };
        if (editMsgId.HasValue) { try { await _sender.EditMessageTextWithInlineKeyboardAsync(chatId, editMsgId.Value, text, kb, ct).ConfigureAwait(false); return; } catch { } }
        try { await _sender.RemoveReplyKeyboardSilentAsync(chatId, ct).ConfigureAwait(false); } catch { }
        try { await _sender.SendTextMessageWithInlineKeyboardAsync(chatId, text, kb, ct).ConfigureAwait(false); } catch { }
    }

    private async Task StartPost(long chatId, long userId, string? lang, int? editMsgId, CancellationToken ct)
    {
        await SafeDelete(chatId, editMsgId, ct);
        await _stateStore.ClearAllFlowDataAsync(userId, ct).ConfigureAwait(false);
        await _stateStore.SetStateAsync(userId, "iq_text", ct).ConfigureAwait(false);
        var msg = L("<b>➕ ثبت سوال — مرحله ۱ از ۳</b>\n━━━━━━━━━━━━━━━━━━━\n\nمتن سوال خود را بنویسید:",
                    "<b>➕ Ask Question — Step 1/3</b>\n━━━━━━━━━━━━━━━━━━━\n\nWrite your question:", lang);
        var kb = new List<IReadOnlyList<string>> { new[] { L("❌ انصراف", "❌ Cancel", lang) } };
        try { await _sender.SendTextMessageWithReplyKeyboardAsync(chatId, msg, kb, ct).ConfigureAwait(false); } catch { }
    }

    private async Task<bool> HandleInput(long chatId, long userId, string state, string text, string? lang, int? userMsgId, CancellationToken ct)
    {
        await SafeDelete(chatId, userMsgId, ct);
        await DeletePrevBotMsg(chatId, userId, ct);

        switch (state)
        {
            case "iq_text":
                await _stateStore.SetFlowDataAsync(userId, "iq_text", text, ct).ConfigureAwait(false);
                await _stateStore.SetStateAsync(userId, "iq_country", ct).ConfigureAwait(false);
                var msg2 = L("<b>مرحله ۲ از ۳</b>\n━━━━━━━━━━━━━━━━━━━\n\nکشور هدف را وارد کنید:",
                             "<b>Step 2/3</b>\n━━━━━━━━━━━━━━━━━━━\n\nEnter the target country:", lang);
                var kb2 = new List<IReadOnlyList<string>> { new[] { L("❌ انصراف", "❌ Cancel", lang) } };
                try { await _sender.SendTextMessageWithReplyKeyboardAsync(chatId, msg2, kb2, ct).ConfigureAwait(false); } catch { }
                break;
            case "iq_country":
                await _stateStore.SetFlowDataAsync(userId, "iq_country", text, ct).ConfigureAwait(false);
                await _stateStore.SetStateAsync(userId, "iq_bounty", ct).ConfigureAwait(false);
                var msg3 = L("<b>مرحله ۳ از ۳</b>\n━━━━━━━━━━━━━━━━━━━\n\nمبلغ جایزه (تومان) — ۰ برای بدون جایزه:",
                             "<b>Step 3/3</b>\n━━━━━━━━━━━━━━━━━━━\n\nBounty amount (Toman) — 0 for no bounty:", lang);
                var kb3 = new List<IReadOnlyList<string>> { new[] { "0", "10,000", "50,000" }, new[] { L("❌ انصراف", "❌ Cancel", lang) } };
                try { await _sender.SendTextMessageWithReplyKeyboardAsync(chatId, msg3, kb3, ct).ConfigureAwait(false); } catch { }
                break;
            case "iq_bounty":
                await _stateStore.SetFlowDataAsync(userId, "iq_bounty", text.Replace(",", ""), ct).ConfigureAwait(false);
                await _stateStore.SetStateAsync(userId, "iq_preview", ct).ConfigureAwait(false);
                try { await _sender.RemoveReplyKeyboardSilentAsync(chatId, ct).ConfigureAwait(false); } catch { }
                await ShowPreview(chatId, userId, lang, ct);
                break;
        }
        return true;
    }

    private async Task ShowPreview(long chatId, long userId, string? lang, CancellationToken ct)
    {
        var qText = await _stateStore.GetFlowDataAsync(userId, "iq_text", ct).ConfigureAwait(false) ?? "";
        var country = await _stateStore.GetFlowDataAsync(userId, "iq_country", ct).ConfigureAwait(false) ?? "";
        var bounty = await _stateStore.GetFlowDataAsync(userId, "iq_bounty", ct).ConfigureAwait(false) ?? "0";
        var preview = L($"<b>📋 پیش‌نمایش سوال</b>\n━━━━━━━━━━━━━━━━━━━\n\n❓ {qText}\n🌍 کشور: {country}\n💰 جایزه: {bounty} تومان",
                        $"<b>📋 Question Preview</b>\n━━━━━━━━━━━━━━━━━━━\n\n❓ {qText}\n🌍 Country: {country}\n💰 Bounty: {bounty} Toman", lang);
        var kb = new List<IReadOnlyList<InlineButton>>
        {
            new[] { new InlineButton(L("✅ ارسال", "✅ Submit", lang), "iq_confirm") },
            new[] { new InlineButton(L("❌ انصراف", "❌ Cancel", lang), "iq_cancel") },
        };
        try { await _sender.SendTextMessageWithInlineKeyboardAsync(chatId, preview, kb, ct).ConfigureAwait(false); } catch { }
    }

    private async Task DoSubmitQuestion(long chatId, long userId, string? lang, int? editMsgId, CancellationToken ct)
    {
        if (_questionRepo == null) return;
        var qText = await _stateStore.GetFlowDataAsync(userId, "iq_text", ct).ConfigureAwait(false) ?? "";
        var country = await _stateStore.GetFlowDataAsync(userId, "iq_country", ct).ConfigureAwait(false);
        var bountyStr = await _stateStore.GetFlowDataAsync(userId, "iq_bounty", ct).ConfigureAwait(false) ?? "0";
        decimal.TryParse(bountyStr, out var bounty);
        var user = await SafeGetUser(userId, ct);
        var displayName = $"{user?.FirstName} {user?.LastName}".Trim();
        if (string.IsNullOrEmpty(displayName)) displayName = user?.Username ?? $"User_{userId}";

        await _questionRepo.CreateAsync(new IntlQuestionDto(0, userId, qText, country, bounty, "open", null, displayName, default, null), ct).ConfigureAwait(false);
        await _stateStore.ClearStateAsync(userId, ct).ConfigureAwait(false);
        await _stateStore.ClearAllFlowDataAsync(userId, ct).ConfigureAwait(false);
        await SafeDelete(chatId, editMsgId, ct);

        var msg = L("<b>✅ سوال شما ثبت شد</b>\n\nبه زودی پاسخ دریافت خواهید کرد.",
                    "<b>✅ Your question has been submitted</b>\n\nYou will receive answers soon.", lang);
        var kb = new List<IReadOnlyList<InlineButton>> { new[] { new InlineButton(L("🔙 بازگشت", "🔙 Back", lang), "iq_menu") } };
        try { await _sender.SendTextMessageWithInlineKeyboardAsync(chatId, msg, kb, ct).ConfigureAwait(false); } catch { }
    }

    private async Task BrowseQuestions(long chatId, long userId, string? lang, int page, int? editMsgId, CancellationToken ct)
    {
        if (_questionRepo == null) return;
        var questions = await _questionRepo.ListAsync("open", null, null, page, 10, ct).ConfigureAwait(false);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(L("<b>📋 سوالات بین‌المللی</b>", "<b>📋 International Questions</b>", lang));
        sb.AppendLine("━━━━━━━━━━━━━━━━━━━\n");
        if (questions.Count == 0) sb.AppendLine(L("📭 سوالی یافت نشد.", "📭 No questions found.", lang));
        var kb = new List<IReadOnlyList<InlineButton>>();
        foreach (var q in questions)
        {
            var label = q.QuestionText.Length > 40 ? q.QuestionText[..40] + "..." : q.QuestionText;
            var bountyBadge = q.BountyAmount > 0 ? $" 💰{q.BountyAmount:N0}" : "";
            kb.Add(new[] { new InlineButton($"❓ {label}{bountyBadge}", $"iq_detail:{q.Id}") });
        }
        var nav = new List<InlineButton>();
        if (page > 0) nav.Add(new InlineButton("◀️", $"iq_browse_p:{page - 1}"));
        if (questions.Count == 10) nav.Add(new InlineButton("▶️", $"iq_browse_p:{page + 1}"));
        if (nav.Count > 0) kb.Add(nav);
        kb.Add(new[] { new InlineButton(L("🔙 بازگشت", "🔙 Back", lang), "iq_menu") });

        if (editMsgId.HasValue) { try { await _sender.EditMessageTextWithInlineKeyboardAsync(chatId, editMsgId.Value, sb.ToString(), kb, ct).ConfigureAwait(false); return; } catch { } }
        try { await _sender.RemoveReplyKeyboardSilentAsync(chatId, ct).ConfigureAwait(false); } catch { }
        try { await _sender.SendTextMessageWithInlineKeyboardAsync(chatId, sb.ToString(), kb, ct).ConfigureAwait(false); } catch { }
    }

    private async Task ShowDetail(long chatId, long userId, int questionId, string? lang, int? editMsgId, CancellationToken ct)
    {
        if (_questionRepo == null) return;
        var q = await _questionRepo.GetAsync(questionId, ct).ConfigureAwait(false);
        if (q == null) return;
        var answers = await _questionRepo.ListAnswersAsync(questionId, ct).ConfigureAwait(false);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine(L($"<b>❓ سوال #{q.Id}</b>", $"<b>❓ Question #{q.Id}</b>", lang));
        sb.AppendLine("━━━━━━━━━━━━━━━━━━━\n");
        sb.AppendLine($"❓ {q.QuestionText}");
        sb.AppendLine(L($"\n🌍 کشور: {q.TargetCountry}", $"\n🌍 Country: {q.TargetCountry}", lang));
        if (q.BountyAmount > 0)
            sb.AppendLine(L($"💰 جایزه: {q.BountyAmount:N0} تومان", $"💰 Bounty: {q.BountyAmount:N0} Toman", lang));
        sb.AppendLine(L($"👤 از: {q.UserDisplayName}", $"👤 By: {q.UserDisplayName}", lang));

        if (answers.Count > 0)
        {
            sb.AppendLine(L($"\n📝 پاسخ‌ها ({answers.Count}):", $"\n📝 Answers ({answers.Count}):", lang));
            foreach (var a in answers.Take(5))
            {
                var statusIcon = a.Status == "accepted" ? "✅" : "🟡";
                sb.AppendLine($"\n{statusIcon} {a.AnswerText[..Math.Min(100, a.AnswerText.Length)]}");
            }
        }

        var kb = new List<IReadOnlyList<InlineButton>>();
        if (q.TelegramUserId != userId && q.Status == "open")
            kb.Add(new[] { new InlineButton(L("💬 پاسخ دادن", "💬 Answer", lang), $"iq_answer:{q.Id}") });
        // Question owner can accept answers
        if (q.TelegramUserId == userId)
        {
            foreach (var a in answers.Where(a => a.Status == "pending"))
                kb.Add(new[] { new InlineButton(L($"✅ پذیرش پاسخ #{a.Id}", $"✅ Accept #{a.Id}", lang), $"iq_accept_ans:{a.Id}") });
        }
        kb.Add(new[] { new InlineButton(L("🔙 بازگشت", "🔙 Back", lang), "iq_browse") });

        if (editMsgId.HasValue) { try { await _sender.EditMessageTextWithInlineKeyboardAsync(chatId, editMsgId.Value, sb.ToString(), kb, ct).ConfigureAwait(false); return; } catch { } }
        try { await _sender.SendTextMessageWithInlineKeyboardAsync(chatId, sb.ToString(), kb, ct).ConfigureAwait(false); } catch { }
    }

    private async Task StartAnswer(long chatId, long userId, int questionId, string? lang, int? editMsgId, CancellationToken ct)
    {
        await SafeDelete(chatId, editMsgId, ct);
        await _stateStore.SetFlowDataAsync(userId, "iq_answer_qid", questionId.ToString(), ct).ConfigureAwait(false);
        await _stateStore.SetStateAsync(userId, "iq_answer", ct).ConfigureAwait(false);
        var msg = L("<b>💬 پاسخ شما</b>\n━━━━━━━━━━━━━━━━━━━\n\nپاسخ خود را بنویسید:",
                    "<b>💬 Your Answer</b>\n━━━━━━━━━━━━━━━━━━━\n\nWrite your answer:", lang);
        var kb = new List<IReadOnlyList<string>> { new[] { L("❌ انصراف", "❌ Cancel", lang) } };
        try { await _sender.SendTextMessageWithReplyKeyboardAsync(chatId, msg, kb, ct).ConfigureAwait(false); } catch { }
    }

    private async Task<bool> HandleAnswerInput(long chatId, long userId, string text, string? lang, int? userMsgId, CancellationToken ct)
    {
        if (_questionRepo == null) return false;
        await SafeDelete(chatId, userMsgId, ct);
        await DeletePrevBotMsg(chatId, userId, ct);
        var qidStr = await _stateStore.GetFlowDataAsync(userId, "iq_answer_qid", ct).ConfigureAwait(false) ?? "0";
        int.TryParse(qidStr, out var qid);
        await _stateStore.ClearStateAsync(userId, ct).ConfigureAwait(false);
        await _stateStore.ClearAllFlowDataAsync(userId, ct).ConfigureAwait(false);
        try { await _sender.RemoveReplyKeyboardSilentAsync(chatId, ct).ConfigureAwait(false); } catch { }

        await _questionRepo.CreateAnswerAsync(new QuestionAnswerDto(0, qid, userId, text, "pending", default), ct).ConfigureAwait(false);

        var msg = L("<b>✅ پاسخ شما ثبت شد</b>", "<b>✅ Your answer has been submitted</b>", lang);
        var kb = new List<IReadOnlyList<InlineButton>> { new[] { new InlineButton(L("🔙 بازگشت", "🔙 Back", lang), $"iq_detail:{qid}") } };
        try { await _sender.SendTextMessageWithInlineKeyboardAsync(chatId, msg, kb, ct).ConfigureAwait(false); } catch { }
        return true;
    }

    private async Task AcceptAnswer(long chatId, long userId, int answerId, string? lang, int? editMsgId, CancellationToken ct)
    {
        if (_questionRepo == null) return;
        await _questionRepo.UpdateAnswerStatusAsync(answerId, "accepted", ct).ConfigureAwait(false);
        // If bounty > 0, transfer from questioner wallet to answerer wallet
        // (simplified — full escrow would be more complex)
        await SafeDelete(chatId, editMsgId, ct);
        var msg = L("<b>✅ پاسخ پذیرفته شد</b>", "<b>✅ Answer accepted</b>", lang);
        var kb = new List<IReadOnlyList<InlineButton>> { new[] { new InlineButton(L("🔙 بازگشت", "🔙 Back", lang), "iq_menu") } };
        try { await _sender.SendTextMessageWithInlineKeyboardAsync(chatId, msg, kb, ct).ConfigureAwait(false); } catch { }
    }

    private async Task CancelFlow(long chatId, long userId, string? lang, int? editMsgId, CancellationToken ct)
    {
        await _stateStore.ClearStateAsync(userId, ct).ConfigureAwait(false);
        await _stateStore.ClearAllFlowDataAsync(userId, ct).ConfigureAwait(false);
        await SafeDelete(chatId, editMsgId, ct);
        try { await _sender.RemoveReplyKeyboardSilentAsync(chatId, ct).ConfigureAwait(false); } catch { }
        await ShowMenu(chatId, userId, lang, null, ct);
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
