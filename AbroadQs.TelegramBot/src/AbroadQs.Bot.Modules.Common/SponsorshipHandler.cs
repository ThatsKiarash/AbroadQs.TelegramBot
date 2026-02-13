using AbroadQs.Bot.Contracts;
using static AbroadQs.Bot.Contracts.BilingualHelper;

namespace AbroadQs.Bot.Modules.Common;

/// <summary>
/// Phase 7: Sponsorship System — request sponsorship, browse, sponsor a project.
/// Callback prefix: sp_   States: sp_select_project, sp_amount, sp_profit, sp_deadline, sp_desc, sp_preview, sp_fund_confirm
/// Requires at least one student project to request sponsorship (used as collateral/guarantee).
/// </summary>
public sealed class SponsorshipHandler : IUpdateHandler
{
    private readonly IResponseSender _sender;
    private readonly ITelegramUserRepository _userRepo;
    private readonly IUserConversationStateStore _stateStore;
    private readonly ISponsorshipRepository? _sponsorRepo;
    private readonly IStudentProjectRepository? _projectRepo;
    private readonly IWalletRepository? _walletRepo;
    private readonly IUserMessageStateRepository? _msgStateRepo;

    public SponsorshipHandler(IResponseSender sender, ITelegramUserRepository userRepo,
        IUserConversationStateStore stateStore, ISponsorshipRepository? sponsorRepo = null,
        IStudentProjectRepository? projectRepo = null, IWalletRepository? walletRepo = null,
        IUserMessageStateRepository? msgStateRepo = null)
    {
        _sender = sender; _userRepo = userRepo; _stateStore = stateStore;
        _sponsorRepo = sponsorRepo; _projectRepo = projectRepo; _walletRepo = walletRepo; _msgStateRepo = msgStateRepo;
    }

    public string? Command => null;

    public bool CanHandle(BotUpdateContext context)
    {
        if (context.UserId == null) return false;
        if (context.IsCallbackQuery)
            return (context.MessageText?.Trim() ?? "").StartsWith("sp_", StringComparison.Ordinal);
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

            if (cb == "sp_menu") { await ShowMenu(chatId, userId, lang, eid, ct); return true; }
            if (cb == "sp_request") { await StartRequest(chatId, userId, lang, eid, ct); return true; }
            if (cb == "sp_browse") { await BrowseRequests(chatId, userId, lang, 0, eid, ct); return true; }
            if (cb == "sp_my") { await ShowMySponsorship(chatId, userId, lang, eid, ct); return true; }
            if (cb.StartsWith("sp_browse_p:")) { int.TryParse(cb["sp_browse_p:".Length..], out var p); await BrowseRequests(chatId, userId, lang, p, eid, ct); return true; }
            if (cb.StartsWith("sp_proj:")) { int.TryParse(cb["sp_proj:".Length..], out var projId); await OnProjectSelected(chatId, userId, projId, lang, eid, ct); return true; }
            if (cb.StartsWith("sp_detail:")) { int.TryParse(cb["sp_detail:".Length..], out var rid); await ShowDetail(chatId, userId, rid, lang, eid, ct); return true; }
            if (cb.StartsWith("sp_fund:")) { int.TryParse(cb["sp_fund:".Length..], out var rid2); await StartFund(chatId, userId, rid2, lang, eid, ct); return true; }
            if (cb == "sp_fund_confirm") { await DoFund(chatId, userId, lang, eid, ct); return true; }
            if (cb == "sp_confirm") { await DoSubmitRequest(chatId, userId, lang, eid, ct); return true; }
            if (cb == "sp_cancel" || cb == "sp_fund_cancel") { await CancelFlow(chatId, userId, lang, eid, ct); return true; }
            return false;
        }

        var state = await _stateStore.GetStateAsync(userId, ct).ConfigureAwait(false);
        if (state == null || !state.StartsWith("sp_")) return false;
        var text = context.MessageText?.Trim() ?? "";
        if (string.IsNullOrEmpty(text)) return false;
        if (text.Contains(L("انصراف", "Cancel", lang))) { await CancelFlow(chatId, userId, lang, null, ct); await SafeDelete(chatId, context.IncomingMessageId, ct); return true; }

        return state switch
        {
            "sp_amount" => await HandleInput(chatId, userId, "sp_amount", text, lang, context.IncomingMessageId, ct),
            "sp_profit" => await HandleInput(chatId, userId, "sp_profit", text, lang, context.IncomingMessageId, ct),
            "sp_deadline" => await HandleInput(chatId, userId, "sp_deadline", text, lang, context.IncomingMessageId, ct),
            "sp_desc" => await HandleInput(chatId, userId, "sp_desc", text, lang, context.IncomingMessageId, ct),
            "sp_fund_amount" => await HandleInput(chatId, userId, "sp_fund_amount", text, lang, context.IncomingMessageId, ct),
            _ => false
        };
    }

    public async Task ShowMenu(long chatId, long userId, string? lang, int? editMsgId, CancellationToken ct)
    {
        var text = L("<b>🤝 حامی مالی</b>\n━━━━━━━━━━━━━━━━━━━\n\nدرخواست حمایت مالی ثبت کنید یا حامی یک پروژه شوید.",
                     "<b>🤝 Financial Sponsor</b>\n━━━━━━━━━━━━━━━━━━━\n\nRequest sponsorship or sponsor a project.", lang);
        var kb = new List<IReadOnlyList<InlineButton>>
        {
            new[] { new InlineButton(L("📝 درخواست حمایت", "📝 Request Sponsorship", lang), "sp_request") },
            new[] { new InlineButton(L("💰 حمایت از پروژه", "💰 Fund a Project", lang), "sp_browse") },
            new[] { new InlineButton(L("📊 حمایت‌های من", "📊 My Sponsorships", lang), "sp_my") },
            new[] { new InlineButton(L("🔙 بازگشت", "🔙 Back", lang), "stage:new_request") },
        };
        if (editMsgId.HasValue) { try { await _sender.EditMessageTextWithInlineKeyboardAsync(chatId, editMsgId.Value, text, kb, ct).ConfigureAwait(false); return; } catch { } }
        try { await _sender.RemoveReplyKeyboardSilentAsync(chatId, ct).ConfigureAwait(false); } catch { }
        try { await _sender.SendTextMessageWithInlineKeyboardAsync(chatId, text, kb, ct).ConfigureAwait(false); } catch { }
    }

    public async Task HandleCallbackAction(long chatId, long userId, string action, int? editMsgId, CancellationToken ct)
    {
        var user = await SafeGetUser(userId, ct);
        var lang = user?.PreferredLanguage;
        switch (action)
        {
            case "sp_request": await StartRequest(chatId, userId, lang, editMsgId, ct); break;
            case "sp_browse": await BrowseRequests(chatId, userId, lang, 0, editMsgId, ct); break;
            case "sp_my": await ShowMySponsorship(chatId, userId, lang, editMsgId, ct); break;
        }
    }

    private async Task StartRequest(long chatId, long userId, string? lang, int? editMsgId, CancellationToken ct)
    {
        await SafeDelete(chatId, editMsgId, ct);
        await _stateStore.ClearAllFlowDataAsync(userId, ct).ConfigureAwait(false);

        if (_projectRepo == null)
        {
            var errMsg = L("سرویس پروژه‌ها در دسترس نیست.", "Project service is unavailable.", lang);
            var errKb = new List<IReadOnlyList<InlineButton>> { new[] { new InlineButton(L("🔙 بازگشت", "🔙 Back", lang), "sp_menu") } };
            try { await _sender.SendTextMessageWithInlineKeyboardAsync(chatId, errMsg, errKb, ct).ConfigureAwait(false); } catch { }
            return;
        }

        var projects = await _projectRepo.ListAsync(status: null, category: null, userId: userId, page: 0, pageSize: 50, ct).ConfigureAwait(false);
        if (projects == null || projects.Count == 0)
        {
            var noProjMsg = L(
                "برای درخواست حمایت مالی، باید حداقل یک پروژه دانشجویی ثبت کرده باشید. ابتدا از بخش پروژه‌ها یک پروژه ثبت کنید.",
                "To request sponsorship, you must have at least one student project registered. Please register a project first from the Projects section.",
                lang);
            var noProjKb = new List<IReadOnlyList<InlineButton>> { new[] { new InlineButton(L("🔙 بازگشت", "🔙 Back", lang), "sp_menu") } };
            try { await _sender.SendTextMessageWithInlineKeyboardAsync(chatId, noProjMsg, noProjKb, ct).ConfigureAwait(false); } catch { }
            return;
        }

        await _stateStore.SetStateAsync(userId, "sp_select_project", ct).ConfigureAwait(false);
        var text = L("<b>پروژه ضمانت</b>\n━━━━━━━━━━━━━━━━━━━\n\nپروژه‌ای را که به عنوان ضمانت استفاده می‌کنید انتخاب کنید:",
                    "<b>Collateral Project</b>\n━━━━━━━━━━━━━━━━━━━\n\nSelect the project to use as guarantee:", lang);
        var kb = new List<IReadOnlyList<InlineButton>>();
        foreach (var p in projects)
        {
            var label = $"{p.Title} (#{p.Id})";
            if (label.Length > 40) label = label[..37] + "...";
            kb.Add(new[] { new InlineButton(label, $"sp_proj:{p.Id}") });
        }
        kb.Add(new[] { new InlineButton(L("❌ انصراف", "❌ Cancel", lang), "sp_cancel") });
        try { await _sender.SendTextMessageWithInlineKeyboardAsync(chatId, text, kb, ct).ConfigureAwait(false); } catch { }
    }

    private async Task OnProjectSelected(long chatId, long userId, int projectId, string? lang, int? editMsgId, CancellationToken ct)
    {
        if (_projectRepo == null) return;
        var proj = await _projectRepo.GetAsync(projectId, ct).ConfigureAwait(false);
        if (proj == null || proj.TelegramUserId != userId)
        {
            await CancelFlow(chatId, userId, lang, editMsgId, ct);
            return;
        }
        await SafeDelete(chatId, editMsgId, ct);
        await _stateStore.SetFlowDataAsync(userId, "sp_project_id", projectId.ToString(), ct).ConfigureAwait(false);
        await _stateStore.SetStateAsync(userId, "sp_amount", ct).ConfigureAwait(false);
        var msg = L("<b>مرحله ۱ از ۴</b>\n━━━━━━━━━━━━━━━━━━━\n\nمبلغ مورد نیاز (تومان):",
                    "<b>Step 1/4</b>\n━━━━━━━━━━━━━━━━━━━\n\nRequired amount (Toman):", lang);
        var kb = new List<IReadOnlyList<string>> { new[] { L("❌ انصراف", "❌ Cancel", lang) } };
        try { await _sender.SendTextMessageWithReplyKeyboardAsync(chatId, msg, kb, ct).ConfigureAwait(false); } catch { }
    }

    private async Task<bool> HandleInput(long chatId, long userId, string state, string text, string? lang, int? userMsgId, CancellationToken ct)
    {
        await SafeDelete(chatId, userMsgId, ct);
        await DeletePrevBotMsg(chatId, userId, ct);

        switch (state)
        {
            case "sp_amount":
                await _stateStore.SetFlowDataAsync(userId, "sp_amount", text.Replace(",", ""), ct).ConfigureAwait(false);
                await _stateStore.SetStateAsync(userId, "sp_profit", ct).ConfigureAwait(false);
                await SendStep(chatId, L("درصد سود پیشنهادی (مثلاً ۲۰):", "Proposed profit share % (e.g. 20):", lang), 2, 4, lang, ct);
                break;
            case "sp_profit":
                await _stateStore.SetFlowDataAsync(userId, "sp_profit", text, ct).ConfigureAwait(false);
                await _stateStore.SetStateAsync(userId, "sp_deadline", ct).ConfigureAwait(false);
                await SendStep(chatId, L("مهلت بازپرداخت:", "Repayment deadline:", lang), 3, 4, lang, ct);
                break;
            case "sp_deadline":
                await _stateStore.SetFlowDataAsync(userId, "sp_deadline", text, ct).ConfigureAwait(false);
                await _stateStore.SetStateAsync(userId, "sp_desc", ct).ConfigureAwait(false);
                await SendStep(chatId, L("توضیحات:", "Description:", lang), 4, 4, lang, ct);
                break;
            case "sp_desc":
                await _stateStore.SetFlowDataAsync(userId, "sp_desc", text, ct).ConfigureAwait(false);
                await _stateStore.SetStateAsync(userId, "sp_preview", ct).ConfigureAwait(false);
                try { await _sender.RemoveReplyKeyboardSilentAsync(chatId, ct).ConfigureAwait(false); } catch { }
                await ShowPreview(chatId, userId, lang, ct);
                break;
            case "sp_fund_amount":
                await _stateStore.SetFlowDataAsync(userId, "sp_fund_amt", text.Replace(",", ""), ct).ConfigureAwait(false);
                await _stateStore.SetStateAsync(userId, "sp_fund_confirm", ct).ConfigureAwait(false);
                try { await _sender.RemoveReplyKeyboardSilentAsync(chatId, ct).ConfigureAwait(false); } catch { }
                var amt = text.Replace(",", "");
                var confirmMsg = L($"<b>تأیید حمایت</b>\n\n💰 مبلغ: {amt} تومان\n\nآیا مطمئن هستید؟",
                                   $"<b>Confirm Sponsorship</b>\n\n💰 Amount: {amt} Toman\n\nAre you sure?", lang);
                var ckb = new List<IReadOnlyList<InlineButton>>
                {
                    new[] { new InlineButton(L("✅ تأیید", "✅ Confirm", lang), "sp_fund_confirm") },
                    new[] { new InlineButton(L("❌ انصراف", "❌ Cancel", lang), "sp_fund_cancel") },
                };
                try { await _sender.SendTextMessageWithInlineKeyboardAsync(chatId, confirmMsg, ckb, ct).ConfigureAwait(false); } catch { }
                break;
        }
        return true;
    }

    private async Task SendStep(long chatId, string prompt, int step, int total, string? lang, CancellationToken ct)
    {
        var msg = L($"<b>مرحله {step} از {total}</b>\n━━━━━━━━━━━━━━━━━━━\n\n{prompt}",
                    $"<b>Step {step}/{total}</b>\n━━━━━━━━━━━━━━━━━━━\n\n{prompt}", lang);
        var kb = new List<IReadOnlyList<string>> { new[] { L("❌ انصراف", "❌ Cancel", lang) } };
        try { await _sender.SendTextMessageWithReplyKeyboardAsync(chatId, msg, kb, ct).ConfigureAwait(false); } catch { }
    }

    private async Task ShowPreview(long chatId, long userId, string? lang, CancellationToken ct)
    {
        var amount = await _stateStore.GetFlowDataAsync(userId, "sp_amount", ct).ConfigureAwait(false) ?? "0";
        var profit = await _stateStore.GetFlowDataAsync(userId, "sp_profit", ct).ConfigureAwait(false) ?? "0";
        var deadline = await _stateStore.GetFlowDataAsync(userId, "sp_deadline", ct).ConfigureAwait(false) ?? "";
        var desc = await _stateStore.GetFlowDataAsync(userId, "sp_desc", ct).ConfigureAwait(false) ?? "";
        var projectLine = "";
        var projectIdStr = await _stateStore.GetFlowDataAsync(userId, "sp_project_id", ct).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(projectIdStr) && int.TryParse(projectIdStr, out var projId) && _projectRepo != null)
        {
            var proj = await _projectRepo.GetAsync(projId, ct).ConfigureAwait(false);
            projectLine = proj != null
                ? (lang == "en" ? $"\n📁 Guarantee project: {proj.Title} (#{proj.Id})\n" : $"\n📁 پروژه ضمانت: {proj.Title} (#{proj.Id})\n")
                : "";
        }
        var preview = L($"<b>📋 پیش‌نمایش درخواست حمایت</b>\n━━━━━━━━━━━━━━━━━━━\n\n{projectLine}💰 مبلغ: {amount} تومان\n📊 سود: {profit}%\n📅 مهلت: {deadline}\n📝 توضیحات: {desc}",
                        $"<b>📋 Sponsorship Request Preview</b>\n━━━━━━━━━━━━━━━━━━━\n\n{projectLine}💰 Amount: {amount} Toman\n📊 Profit: {profit}%\n📅 Deadline: {deadline}\n📝 Description: {desc}", lang);
        var kb = new List<IReadOnlyList<InlineButton>>
        {
            new[] { new InlineButton(L("✅ ارسال", "✅ Submit", lang), "sp_confirm") },
            new[] { new InlineButton(L("❌ انصراف", "❌ Cancel", lang), "sp_cancel") },
        };
        try { await _sender.SendTextMessageWithInlineKeyboardAsync(chatId, preview, kb, ct).ConfigureAwait(false); } catch { }
    }

    private async Task DoSubmitRequest(long chatId, long userId, string? lang, int? editMsgId, CancellationToken ct)
    {
        if (_sponsorRepo == null) return;
        var amountStr = await _stateStore.GetFlowDataAsync(userId, "sp_amount", ct).ConfigureAwait(false) ?? "0";
        var profitStr = await _stateStore.GetFlowDataAsync(userId, "sp_profit", ct).ConfigureAwait(false) ?? "0";
        var desc = await _stateStore.GetFlowDataAsync(userId, "sp_desc", ct).ConfigureAwait(false);
        var projectIdStr = await _stateStore.GetFlowDataAsync(userId, "sp_project_id", ct).ConfigureAwait(false);
        int? projectId = null;
        if (!string.IsNullOrEmpty(projectIdStr) && int.TryParse(projectIdStr, out var pid)) projectId = pid;
        decimal.TryParse(amountStr, out var amount);
        decimal.TryParse(profitStr, out var profit);

        await _sponsorRepo.CreateRequestAsync(new SponsorshipRequestDto(0, projectId, userId, amount, profit, null, desc, "open", default), ct).ConfigureAwait(false);
        await _stateStore.ClearStateAsync(userId, ct).ConfigureAwait(false);
        await _stateStore.ClearAllFlowDataAsync(userId, ct).ConfigureAwait(false);
        await SafeDelete(chatId, editMsgId, ct);

        var msg = L("<b>✅ درخواست حمایت مالی ثبت شد</b>", "<b>✅ Sponsorship request submitted</b>", lang);
        var kb = new List<IReadOnlyList<InlineButton>> { new[] { new InlineButton(L("🔙 بازگشت", "🔙 Back", lang), "sp_menu") } };
        try { await _sender.SendTextMessageWithInlineKeyboardAsync(chatId, msg, kb, ct).ConfigureAwait(false); } catch { }
    }

    private async Task BrowseRequests(long chatId, long userId, string? lang, int page, int? editMsgId, CancellationToken ct)
    {
        if (_sponsorRepo == null) return;
        var requests = await _sponsorRepo.ListRequestsAsync("open", null, page, 10, ct).ConfigureAwait(false);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(L("<b>📋 درخواست‌های حمایت مالی</b>", "<b>📋 Sponsorship Requests</b>", lang));
        sb.AppendLine("━━━━━━━━━━━━━━━━━━━\n");
        if (requests.Count == 0) sb.AppendLine(L("📭 درخواستی یافت نشد.", "📭 No requests found.", lang));
        var kb = new List<IReadOnlyList<InlineButton>>();
        foreach (var r in requests)
            kb.Add(new[] { new InlineButton(L($"💰 {r.RequestedAmount:N0}T — {r.ProfitSharePercent}%", $"💰 {r.RequestedAmount:N0}T — {r.ProfitSharePercent}%", lang), $"sp_detail:{r.Id}") });
        var nav = new List<InlineButton>();
        if (page > 0) nav.Add(new InlineButton("◀️", $"sp_browse_p:{page - 1}"));
        if (requests.Count == 10) nav.Add(new InlineButton("▶️", $"sp_browse_p:{page + 1}"));
        if (nav.Count > 0) kb.Add(nav);
        kb.Add(new[] { new InlineButton(L("🔙 بازگشت", "🔙 Back", lang), "sp_menu") });

        if (editMsgId.HasValue) { try { await _sender.EditMessageTextWithInlineKeyboardAsync(chatId, editMsgId.Value, sb.ToString(), kb, ct).ConfigureAwait(false); return; } catch { } }
        try { await _sender.RemoveReplyKeyboardSilentAsync(chatId, ct).ConfigureAwait(false); } catch { }
        try { await _sender.SendTextMessageWithInlineKeyboardAsync(chatId, sb.ToString(), kb, ct).ConfigureAwait(false); } catch { }
    }

    private async Task ShowDetail(long chatId, long userId, int requestId, string? lang, int? editMsgId, CancellationToken ct)
    {
        if (_sponsorRepo == null) return;
        var r = await _sponsorRepo.GetRequestAsync(requestId, ct).ConfigureAwait(false);
        if (r == null) return;
        var projectLine = "";
        if (r.ProjectId.HasValue && _projectRepo != null)
        {
            var proj = await _projectRepo.GetAsync(r.ProjectId.Value, ct).ConfigureAwait(false);
            if (proj != null)
                projectLine = lang == "en" ? $"\n📁 Guarantee project: {proj.Title} (#{proj.Id})\n" : $"\n📁 پروژه ضمانت: {proj.Title} (#{proj.Id})\n";
        }
        var text = L($"<b>🤝 درخواست حمایت #{r.Id}</b>\n━━━━━━━━━━━━━━━━━━━\n\n{projectLine}💰 مبلغ: {r.RequestedAmount:N0} تومان\n📊 سود: {r.ProfitSharePercent}%\n📝 {r.Description}",
                     $"<b>🤝 Sponsorship #{r.Id}</b>\n━━━━━━━━━━━━━━━━━━━\n\n{projectLine}💰 Amount: {r.RequestedAmount:N0} Toman\n📊 Profit: {r.ProfitSharePercent}%\n📝 {r.Description}", lang);
        var kb = new List<IReadOnlyList<InlineButton>>();
        if (r.RequesterTelegramUserId != userId && r.Status == "open")
            kb.Add(new[] { new InlineButton(L("💰 حمایت کردن", "💰 Sponsor", lang), $"sp_fund:{r.Id}") });
        kb.Add(new[] { new InlineButton(L("🔙 بازگشت", "🔙 Back", lang), "sp_browse") });

        if (editMsgId.HasValue) { try { await _sender.EditMessageTextWithInlineKeyboardAsync(chatId, editMsgId.Value, text, kb, ct).ConfigureAwait(false); return; } catch { } }
        try { await _sender.SendTextMessageWithInlineKeyboardAsync(chatId, text, kb, ct).ConfigureAwait(false); } catch { }
    }

    private async Task ShowMySponsorship(long chatId, long userId, string? lang, int? editMsgId, CancellationToken ct)
    {
        if (_sponsorRepo == null) return;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(L("<b>📊 حمایت‌های من</b>", "<b>📊 My Sponsorships</b>", lang));
        sb.AppendLine("━━━━━━━━━━━━━━━━━━━\n");

        var myRequests = await _sponsorRepo.ListRequestsAsync(null, userId, 0, 20, ct).ConfigureAwait(false);
        sb.AppendLine(L("<b>درخواست‌های من (به عنوان متقاضی)</b>", "<b>My Requests (as requester)</b>", lang));
        if (myRequests.Count == 0)
            sb.AppendLine(L("— درخواستی ثبت نکرده‌اید.", "— No requests submitted.", lang));
        else
            foreach (var r in myRequests)
                sb.AppendLine(L($"• #{r.Id}: {r.RequestedAmount:N0}T — {r.ProfitSharePercent}% — {r.Status}", $"• #{r.Id}: {r.RequestedAmount:N0}T — {r.ProfitSharePercent}% — {r.Status}", lang));
        sb.AppendLine();

        var myFunded = await _sponsorRepo.ListBySponsorAsync(userId, 0, 20, ct).ConfigureAwait(false);
        sb.AppendLine(L("<b>حمایت‌های من (به عنوان حامی)</b>", "<b>My Sponsorships (as funder)</b>", lang));
        if (myFunded.Count == 0)
            sb.AppendLine(L("— پروژه‌ای را حمایت نکرده‌اید.", "— No projects sponsored yet.", lang));
        else
            foreach (var s in myFunded)
                sb.AppendLine(L($"• درخواست #{s.RequestId}: {s.Amount:N0}T — {s.Status}", $"• Request #{s.RequestId}: {s.Amount:N0}T — {s.Status}", lang));

        var kb = new List<IReadOnlyList<InlineButton>> { new[] { new InlineButton(L("🔙 بازگشت", "🔙 Back", lang), "sp_menu") } };
        if (editMsgId.HasValue) { try { await _sender.EditMessageTextWithInlineKeyboardAsync(chatId, editMsgId.Value, sb.ToString(), kb, ct).ConfigureAwait(false); return; } catch { } }
        try { await _sender.RemoveReplyKeyboardSilentAsync(chatId, ct).ConfigureAwait(false); } catch { }
        try { await _sender.SendTextMessageWithInlineKeyboardAsync(chatId, sb.ToString(), kb, ct).ConfigureAwait(false); } catch { }
    }

    private async Task StartFund(long chatId, long userId, int requestId, string? lang, int? editMsgId, CancellationToken ct)
    {
        await SafeDelete(chatId, editMsgId, ct);
        await _stateStore.ClearAllFlowDataAsync(userId, ct).ConfigureAwait(false);
        await _stateStore.SetFlowDataAsync(userId, "sp_fund_rid", requestId.ToString(), ct).ConfigureAwait(false);
        await _stateStore.SetStateAsync(userId, "sp_fund_amount", ct).ConfigureAwait(false);
        var msg = L("<b>💰 مبلغ حمایت</b>\n━━━━━━━━━━━━━━━━━━━\n\nمبلغ (تومان) را وارد کنید:",
                    "<b>💰 Sponsorship Amount</b>\n━━━━━━━━━━━━━━━━━━━\n\nEnter the amount (Toman):", lang);
        var kb = new List<IReadOnlyList<string>> { new[] { L("❌ انصراف", "❌ Cancel", lang) } };
        try { await _sender.SendTextMessageWithReplyKeyboardAsync(chatId, msg, kb, ct).ConfigureAwait(false); } catch { }
    }

    private async Task DoFund(long chatId, long userId, string? lang, int? editMsgId, CancellationToken ct)
    {
        if (_sponsorRepo == null || _walletRepo == null) return;
        var ridStr = await _stateStore.GetFlowDataAsync(userId, "sp_fund_rid", ct).ConfigureAwait(false) ?? "0";
        var amtStr = await _stateStore.GetFlowDataAsync(userId, "sp_fund_amt", ct).ConfigureAwait(false) ?? "0";
        int.TryParse(ridStr, out var rid);
        decimal.TryParse(amtStr, out var amt);

        await _stateStore.ClearStateAsync(userId, ct).ConfigureAwait(false);
        await _stateStore.ClearAllFlowDataAsync(userId, ct).ConfigureAwait(false);
        await SafeDelete(chatId, editMsgId, ct);

        var balance = await _walletRepo.GetBalanceAsync(userId, ct).ConfigureAwait(false);
        if (balance < amt)
        {
            try { await _sender.SendTextMessageAsync(chatId, L("⚠️ موجودی کافی نیست.", "⚠️ Insufficient balance.", lang), ct).ConfigureAwait(false); } catch { }
            return;
        }

        await _walletRepo.DebitAsync(userId, amt, L("حمایت مالی", "Sponsorship", lang), rid.ToString(), ct).ConfigureAwait(false);
        await _sponsorRepo.CreateSponsorshipAsync(new SponsorshipDto(0, rid, userId, amt, "active", default), ct).ConfigureAwait(false);
        await _sponsorRepo.UpdateRequestStatusAsync(rid, "funded", ct).ConfigureAwait(false);

        var msg = L("<b>✅ حمایت شما ثبت شد</b>", "<b>✅ Sponsorship recorded</b>", lang);
        var kb = new List<IReadOnlyList<InlineButton>> { new[] { new InlineButton(L("🔙 بازگشت", "🔙 Back", lang), "sp_menu") } };
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
