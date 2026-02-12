using AbroadQs.Bot.Contracts;
using static AbroadQs.Bot.Contracts.BilingualHelper;

namespace AbroadQs.Bot.Modules.Common;

/// <summary>
/// Phase 5: Student Projects Marketplace — post, browse, bid on projects.
/// Callback prefix: proj_   States: proj_title, proj_desc, proj_category, proj_budget, proj_deadline, proj_skills, proj_preview
/// Also: proj_bid_amount, proj_bid_duration, proj_bid_letter, proj_bid_preview
/// </summary>
public sealed class StudentProjectHandler : IUpdateHandler
{
    private readonly IResponseSender _sender;
    private readonly ITelegramUserRepository _userRepo;
    private readonly IUserConversationStateStore _stateStore;
    private readonly IStudentProjectRepository? _projRepo;
    private readonly IProjectBidRepository? _projBidRepo;
    private readonly IUserMessageStateRepository? _msgStateRepo;

    public StudentProjectHandler(IResponseSender sender, ITelegramUserRepository userRepo,
        IUserConversationStateStore stateStore, IStudentProjectRepository? projRepo = null,
        IProjectBidRepository? projBidRepo = null, IUserMessageStateRepository? msgStateRepo = null)
    {
        _sender = sender; _userRepo = userRepo; _stateStore = stateStore;
        _projRepo = projRepo; _projBidRepo = projBidRepo; _msgStateRepo = msgStateRepo;
    }

    public string? Command => null;

    public bool CanHandle(BotUpdateContext context)
    {
        if (context.UserId == null) return false;
        if (context.IsCallbackQuery)
            return (context.MessageText?.Trim() ?? "").StartsWith("proj_", StringComparison.Ordinal);
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

            if (cb == "proj_menu") { await ShowMenu(chatId, userId, lang, eid, ct); return true; }
            if (cb == "proj_post") { await StartPost(chatId, userId, lang, eid, ct); return true; }
            if (cb == "proj_browse") { await BrowseProjects(chatId, userId, lang, 0, eid, ct); return true; }
            if (cb == "proj_my") { await MyProjects(chatId, userId, lang, 0, eid, ct); return true; }
            if (cb.StartsWith("proj_detail:")) { int.TryParse(cb["proj_detail:".Length..], out var pid); await ShowDetail(chatId, userId, pid, lang, eid, ct); return true; }
            if (cb.StartsWith("proj_browse_p:")) { int.TryParse(cb["proj_browse_p:".Length..], out var p); await BrowseProjects(chatId, userId, lang, p, eid, ct); return true; }
            if (cb.StartsWith("proj_my_p:")) { int.TryParse(cb["proj_my_p:".Length..], out var p); await MyProjects(chatId, userId, lang, p, eid, ct); return true; }
            if (cb == "proj_confirm") { await DoSubmitProject(chatId, userId, lang, eid, ct); return true; }
            if (cb.StartsWith("proj_bid:")) { int.TryParse(cb["proj_bid:".Length..], out var pid2); await StartBid(chatId, userId, pid2, lang, eid, ct); return true; }
            if (cb == "proj_bid_confirm") { await DoSubmitBid(chatId, userId, lang, eid, ct); return true; }
            if (cb == "proj_cancel" || cb == "proj_bid_cancel")
            { await CancelFlow(chatId, userId, lang, eid, ct); return true; }
            return false;
        }

        var state = await _stateStore.GetStateAsync(userId, ct).ConfigureAwait(false);
        if (state == null || !state.StartsWith("proj_")) return false;
        var text = context.MessageText?.Trim() ?? "";
        if (string.IsNullOrEmpty(text)) return false;
        if (text.Contains(L("انصراف", "Cancel", lang))) { await CancelFlow(chatId, userId, lang, null, ct); await SafeDelete(chatId, context.IncomingMessageId, ct); return true; }

        return state switch
        {
            "proj_title" => await HandleInput(chatId, userId, "proj_title", text, lang, context.IncomingMessageId, ct),
            "proj_desc" => await HandleInput(chatId, userId, "proj_desc", text, lang, context.IncomingMessageId, ct),
            "proj_category" => await HandleInput(chatId, userId, "proj_category", text, lang, context.IncomingMessageId, ct),
            "proj_budget" => await HandleInput(chatId, userId, "proj_budget", text, lang, context.IncomingMessageId, ct),
            "proj_deadline" => await HandleInput(chatId, userId, "proj_deadline", text, lang, context.IncomingMessageId, ct),
            "proj_skills" => await HandleInput(chatId, userId, "proj_skills", text, lang, context.IncomingMessageId, ct),
            "proj_bid_amount" => await HandleInput(chatId, userId, "proj_bid_amount", text, lang, context.IncomingMessageId, ct),
            "proj_bid_duration" => await HandleInput(chatId, userId, "proj_bid_duration", text, lang, context.IncomingMessageId, ct),
            "proj_bid_letter" => await HandleInput(chatId, userId, "proj_bid_letter", text, lang, context.IncomingMessageId, ct),
            _ => false
        };
    }

    public async Task ShowMenu(long chatId, long userId, string? lang, int? editMsgId, CancellationToken ct)
    {
        var text = L("<b>📚 پروژه‌های دانشجویی</b>\n━━━━━━━━━━━━━━━━━━━\n\nپروژه ثبت کنید یا پروژه‌های دیگران را مشاهده کنید.",
                     "<b>📚 Student Projects</b>\n━━━━━━━━━━━━━━━━━━━\n\nPost a project or browse existing ones.", lang);
        var kb = new List<IReadOnlyList<InlineButton>>
        {
            new[] { new InlineButton(L("➕ ثبت پروژه", "➕ Post Project", lang), "proj_post") },
            new[] { new InlineButton(L("📋 مرور پروژه‌ها", "📋 Browse Projects", lang), "proj_browse") },
            new[] { new InlineButton(L("📁 پروژه‌های من", "📁 My Projects", lang), "proj_my") },
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
        await _stateStore.SetStateAsync(userId, "proj_title", ct).ConfigureAwait(false);
        var msg = L("<b>➕ ثبت پروژه — مرحله ۱ از ۶</b>\n━━━━━━━━━━━━━━━━━━━\n\nعنوان پروژه را وارد کنید:",
                    "<b>➕ Post Project — Step 1/6</b>\n━━━━━━━━━━━━━━━━━━━\n\nEnter project title:", lang);
        var kb = new List<IReadOnlyList<string>> { new[] { L("❌ انصراف", "❌ Cancel", lang) } };
        try { await _sender.SendTextMessageWithReplyKeyboardAsync(chatId, msg, kb, ct).ConfigureAwait(false); } catch { }
    }

    private async Task<bool> HandleInput(long chatId, long userId, string state, string text, string? lang, int? userMsgId, CancellationToken ct)
    {
        await SafeDelete(chatId, userMsgId, ct);
        await DeletePrevBotMsg(chatId, userId, ct);

        switch (state)
        {
            case "proj_title":
                await _stateStore.SetFlowDataAsync(userId, "proj_title", text, ct).ConfigureAwait(false);
                await _stateStore.SetStateAsync(userId, "proj_desc", ct).ConfigureAwait(false);
                await SendStep(chatId, L("توضیحات پروژه:", "Project description:", lang), 2, 6, lang, ct);
                break;
            case "proj_desc":
                await _stateStore.SetFlowDataAsync(userId, "proj_desc", text, ct).ConfigureAwait(false);
                await _stateStore.SetStateAsync(userId, "proj_category", ct).ConfigureAwait(false);
                var catKb = new List<IReadOnlyList<string>> { new[] { "web", "mobile", "data" }, new[] { "design", "other" }, new[] { L("❌ انصراف", "❌ Cancel", lang) } };
                await SendStep(chatId, L("دسته‌بندی:", "Category:", lang), 3, 6, lang, ct, catKb);
                break;
            case "proj_category":
                var validCats = new[] { "web", "mobile", "data", "design", "other" };
                var cat = validCats.FirstOrDefault(c => text.Contains(c, StringComparison.OrdinalIgnoreCase)) ?? "other";
                await _stateStore.SetFlowDataAsync(userId, "proj_category", cat, ct).ConfigureAwait(false);
                await _stateStore.SetStateAsync(userId, "proj_budget", ct).ConfigureAwait(false);
                await SendStep(chatId, L("بودجه (تومان):", "Budget (Toman):", lang), 4, 6, lang, ct);
                break;
            case "proj_budget":
                await _stateStore.SetFlowDataAsync(userId, "proj_budget", text.Replace(",", ""), ct).ConfigureAwait(false);
                await _stateStore.SetStateAsync(userId, "proj_deadline", ct).ConfigureAwait(false);
                await SendStep(chatId, L("مهلت (مثلاً: 1404/03/15):", "Deadline (e.g. 2025-06-05):", lang), 5, 6, lang, ct);
                break;
            case "proj_deadline":
                await _stateStore.SetFlowDataAsync(userId, "proj_deadline", text, ct).ConfigureAwait(false);
                await _stateStore.SetStateAsync(userId, "proj_skills", ct).ConfigureAwait(false);
                await SendStep(chatId, L("مهارت‌های مورد نیاز:", "Required skills:", lang), 6, 6, lang, ct);
                break;
            case "proj_skills":
                await _stateStore.SetFlowDataAsync(userId, "proj_skills", text, ct).ConfigureAwait(false);
                await _stateStore.SetStateAsync(userId, "proj_preview", ct).ConfigureAwait(false);
                try { await _sender.RemoveReplyKeyboardSilentAsync(chatId, ct).ConfigureAwait(false); } catch { }
                await ShowProjectPreview(chatId, userId, lang, ct);
                break;
            // Bid flow
            case "proj_bid_amount":
                await _stateStore.SetFlowDataAsync(userId, "proj_bid_amount", text.Replace(",", ""), ct).ConfigureAwait(false);
                await _stateStore.SetStateAsync(userId, "proj_bid_duration", ct).ConfigureAwait(false);
                await SendStep(chatId, L("مدت زمان پیشنهادی:", "Proposed duration:", lang), 2, 3, lang, ct);
                break;
            case "proj_bid_duration":
                await _stateStore.SetFlowDataAsync(userId, "proj_bid_duration", text, ct).ConfigureAwait(false);
                await _stateStore.SetStateAsync(userId, "proj_bid_letter", ct).ConfigureAwait(false);
                await SendStep(chatId, L("توضیحات پیشنهاد:", "Cover letter:", lang), 3, 3, lang, ct);
                break;
            case "proj_bid_letter":
                await _stateStore.SetFlowDataAsync(userId, "proj_bid_letter", text, ct).ConfigureAwait(false);
                await _stateStore.SetStateAsync(userId, "proj_bid_preview", ct).ConfigureAwait(false);
                try { await _sender.RemoveReplyKeyboardSilentAsync(chatId, ct).ConfigureAwait(false); } catch { }
                await ShowBidPreview(chatId, userId, lang, ct);
                break;
        }
        return true;
    }

    private async Task SendStep(long chatId, string prompt, int step, int total, string? lang, CancellationToken ct, List<IReadOnlyList<string>>? kb = null)
    {
        var msg = L($"<b>مرحله {step} از {total}</b>\n━━━━━━━━━━━━━━━━━━━\n\n{prompt}",
                    $"<b>Step {step}/{total}</b>\n━━━━━━━━━━━━━━━━━━━\n\n{prompt}", lang);
        kb ??= new List<IReadOnlyList<string>> { new[] { L("❌ انصراف", "❌ Cancel", lang) } };
        try { await _sender.SendTextMessageWithReplyKeyboardAsync(chatId, msg, kb, ct).ConfigureAwait(false); } catch { }
    }

    private async Task ShowProjectPreview(long chatId, long userId, string? lang, CancellationToken ct)
    {
        var title = await _stateStore.GetFlowDataAsync(userId, "proj_title", ct).ConfigureAwait(false) ?? "";
        var desc = await _stateStore.GetFlowDataAsync(userId, "proj_desc", ct).ConfigureAwait(false) ?? "";
        var cat = await _stateStore.GetFlowDataAsync(userId, "proj_category", ct).ConfigureAwait(false) ?? "";
        var budget = await _stateStore.GetFlowDataAsync(userId, "proj_budget", ct).ConfigureAwait(false) ?? "0";
        var deadline = await _stateStore.GetFlowDataAsync(userId, "proj_deadline", ct).ConfigureAwait(false) ?? "";
        var skills = await _stateStore.GetFlowDataAsync(userId, "proj_skills", ct).ConfigureAwait(false) ?? "";

        var preview = L($"<b>📋 پیش‌نمایش پروژه</b>\n━━━━━━━━━━━━━━━━━━━\n\n📌 عنوان: {title}\n📝 توضیحات: {desc}\n📁 دسته: {cat}\n💰 بودجه: {budget} تومان\n📅 مهلت: {deadline}\n🛠 مهارت‌ها: {skills}",
                        $"<b>📋 Project Preview</b>\n━━━━━━━━━━━━━━━━━━━\n\n📌 Title: {title}\n📝 Description: {desc}\n📁 Category: {cat}\n💰 Budget: {budget} Toman\n📅 Deadline: {deadline}\n🛠 Skills: {skills}", lang);
        var kb = new List<IReadOnlyList<InlineButton>>
        {
            new[] { new InlineButton(L("✅ ارسال", "✅ Submit", lang), "proj_confirm") },
            new[] { new InlineButton(L("❌ انصراف", "❌ Cancel", lang), "proj_cancel") },
        };
        try { await _sender.SendTextMessageWithInlineKeyboardAsync(chatId, preview, kb, ct).ConfigureAwait(false); } catch { }
    }

    private async Task DoSubmitProject(long chatId, long userId, string? lang, int? editMsgId, CancellationToken ct)
    {
        if (_projRepo == null) return;
        var title = await _stateStore.GetFlowDataAsync(userId, "proj_title", ct).ConfigureAwait(false) ?? "";
        var desc = await _stateStore.GetFlowDataAsync(userId, "proj_desc", ct).ConfigureAwait(false);
        var cat = await _stateStore.GetFlowDataAsync(userId, "proj_category", ct).ConfigureAwait(false) ?? "other";
        var budgetStr = await _stateStore.GetFlowDataAsync(userId, "proj_budget", ct).ConfigureAwait(false) ?? "0";
        var skills = await _stateStore.GetFlowDataAsync(userId, "proj_skills", ct).ConfigureAwait(false);
        decimal.TryParse(budgetStr, out var budget);
        var user = await SafeGetUser(userId, ct);
        var displayName = $"{user?.FirstName} {user?.LastName}".Trim();
        if (string.IsNullOrEmpty(displayName)) displayName = user?.Username ?? $"User_{userId}";

        var dto = new StudentProjectDto(0, userId, title, desc, cat, budget, "IRR", null, skills, "pending_approval", null, null, null, displayName, default, null);
        await _projRepo.CreateAsync(dto, ct).ConfigureAwait(false);
        await _stateStore.ClearStateAsync(userId, ct).ConfigureAwait(false);
        await _stateStore.ClearAllFlowDataAsync(userId, ct).ConfigureAwait(false);
        await SafeDelete(chatId, editMsgId, ct);

        var msg = L("<b>✅ پروژه با موفقیت ثبت شد</b>\n\nپس از تأیید ادمین منتشر خواهد شد.",
                    "<b>✅ Project submitted successfully</b>\n\nIt will be published after admin approval.", lang);
        var kb = new List<IReadOnlyList<InlineButton>> { new[] { new InlineButton(L("🔙 بازگشت", "🔙 Back", lang), "proj_menu") } };
        try { await _sender.SendTextMessageWithInlineKeyboardAsync(chatId, msg, kb, ct).ConfigureAwait(false); } catch { }
    }

    private async Task BrowseProjects(long chatId, long userId, string? lang, int page, int? editMsgId, CancellationToken ct)
    {
        if (_projRepo == null) return;
        var projects = await _projRepo.ListAsync("approved", null, null, page, 10, ct).ConfigureAwait(false);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(L("<b>📋 پروژه‌های موجود</b>", "<b>📋 Available Projects</b>", lang));
        sb.AppendLine("━━━━━━━━━━━━━━━━━━━\n");
        if (projects.Count == 0) sb.AppendLine(L("📭 پروژه‌ای یافت نشد.", "📭 No projects found.", lang));
        var kb = new List<IReadOnlyList<InlineButton>>();
        foreach (var p in projects)
            kb.Add(new[] { new InlineButton($"📌 {p.Title[..Math.Min(30, p.Title.Length)]} — {p.Budget:N0}T", $"proj_detail:{p.Id}") });
        var nav = new List<InlineButton>();
        if (page > 0) nav.Add(new InlineButton("◀️", $"proj_browse_p:{page - 1}"));
        if (projects.Count == 10) nav.Add(new InlineButton("▶️", $"proj_browse_p:{page + 1}"));
        if (nav.Count > 0) kb.Add(nav);
        kb.Add(new[] { new InlineButton(L("🔙 بازگشت", "🔙 Back", lang), "proj_menu") });

        if (editMsgId.HasValue) { try { await _sender.EditMessageTextWithInlineKeyboardAsync(chatId, editMsgId.Value, sb.ToString(), kb, ct).ConfigureAwait(false); return; } catch { } }
        try { await _sender.RemoveReplyKeyboardSilentAsync(chatId, ct).ConfigureAwait(false); } catch { }
        try { await _sender.SendTextMessageWithInlineKeyboardAsync(chatId, sb.ToString(), kb, ct).ConfigureAwait(false); } catch { }
    }

    private async Task MyProjects(long chatId, long userId, string? lang, int page, int? editMsgId, CancellationToken ct)
    {
        if (_projRepo == null) return;
        var projects = await _projRepo.ListAsync(null, null, userId, page, 10, ct).ConfigureAwait(false);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(L("<b>📁 پروژه‌های من</b>", "<b>📁 My Projects</b>", lang));
        sb.AppendLine("━━━━━━━━━━━━━━━━━━━\n");
        if (projects.Count == 0) sb.AppendLine(L("📭 پروژه‌ای یافت نشد.", "📭 No projects found.", lang));
        var kb = new List<IReadOnlyList<InlineButton>>();
        foreach (var p in projects)
        {
            var statusIcon = p.Status == "approved" ? "🟢" : p.Status == "pending_approval" ? "🟡" : p.Status == "in_progress" ? "🔵" : "✅";
            kb.Add(new[] { new InlineButton($"{statusIcon} {p.Title[..Math.Min(30, p.Title.Length)]}", $"proj_detail:{p.Id}") });
        }
        kb.Add(new[] { new InlineButton(L("🔙 بازگشت", "🔙 Back", lang), "proj_menu") });

        if (editMsgId.HasValue) { try { await _sender.EditMessageTextWithInlineKeyboardAsync(chatId, editMsgId.Value, sb.ToString(), kb, ct).ConfigureAwait(false); return; } catch { } }
        try { await _sender.SendTextMessageWithInlineKeyboardAsync(chatId, sb.ToString(), kb, ct).ConfigureAwait(false); } catch { }
    }

    private async Task ShowDetail(long chatId, long userId, int projectId, string? lang, int? editMsgId, CancellationToken ct)
    {
        if (_projRepo == null) return;
        var p = await _projRepo.GetAsync(projectId, ct).ConfigureAwait(false);
        if (p == null) return;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(L($"<b>📌 {p.Title}</b>", $"<b>📌 {p.Title}</b>", lang));
        sb.AppendLine("━━━━━━━━━━━━━━━━━━━\n");
        sb.AppendLine(L($"📝 {p.Description}", $"📝 {p.Description}", lang));
        sb.AppendLine(L($"\n📁 دسته: {p.Category}", $"\n📁 Category: {p.Category}", lang));
        sb.AppendLine(L($"💰 بودجه: {p.Budget:N0} تومان", $"💰 Budget: {p.Budget:N0} Toman", lang));
        sb.AppendLine(L($"🛠 مهارت‌ها: {p.RequiredSkills}", $"🛠 Skills: {p.RequiredSkills}", lang));
        sb.AppendLine(L($"👤 ارسال‌کننده: {p.UserDisplayName}", $"👤 Posted by: {p.UserDisplayName}", lang));

        var kb = new List<IReadOnlyList<InlineButton>>();
        if (p.TelegramUserId != userId && p.Status == "approved")
            kb.Add(new[] { new InlineButton(L("📩 ارسال پیشنهاد", "📩 Submit Proposal", lang), $"proj_bid:{p.Id}") });
        kb.Add(new[] { new InlineButton(L("🔙 بازگشت", "🔙 Back", lang), "proj_browse") });

        if (editMsgId.HasValue) { try { await _sender.EditMessageTextWithInlineKeyboardAsync(chatId, editMsgId.Value, sb.ToString(), kb, ct).ConfigureAwait(false); return; } catch { } }
        try { await _sender.SendTextMessageWithInlineKeyboardAsync(chatId, sb.ToString(), kb, ct).ConfigureAwait(false); } catch { }
    }

    private async Task StartBid(long chatId, long userId, int projectId, string? lang, int? editMsgId, CancellationToken ct)
    {
        await SafeDelete(chatId, editMsgId, ct);
        await _stateStore.ClearAllFlowDataAsync(userId, ct).ConfigureAwait(false);
        await _stateStore.SetFlowDataAsync(userId, "proj_bid_pid", projectId.ToString(), ct).ConfigureAwait(false);
        await _stateStore.SetStateAsync(userId, "proj_bid_amount", ct).ConfigureAwait(false);
        await SendStep(chatId, L("مبلغ پیشنهادی (تومان):", "Proposed amount (Toman):", lang), 1, 3, lang, ct);
    }

    private async Task ShowBidPreview(long chatId, long userId, string? lang, CancellationToken ct)
    {
        var amount = await _stateStore.GetFlowDataAsync(userId, "proj_bid_amount", ct).ConfigureAwait(false) ?? "0";
        var duration = await _stateStore.GetFlowDataAsync(userId, "proj_bid_duration", ct).ConfigureAwait(false) ?? "";
        var letter = await _stateStore.GetFlowDataAsync(userId, "proj_bid_letter", ct).ConfigureAwait(false) ?? "";
        var preview = L($"<b>📋 پیش‌نمایش پیشنهاد</b>\n━━━━━━━━━━━━━━━━━━━\n\n💰 مبلغ: {amount} تومان\n⏱ مدت: {duration}\n📝 توضیحات: {letter}",
                        $"<b>📋 Proposal Preview</b>\n━━━━━━━━━━━━━━━━━━━\n\n💰 Amount: {amount} Toman\n⏱ Duration: {duration}\n📝 Cover: {letter}", lang);
        var kb = new List<IReadOnlyList<InlineButton>>
        {
            new[] { new InlineButton(L("✅ ارسال", "✅ Submit", lang), "proj_bid_confirm") },
            new[] { new InlineButton(L("❌ انصراف", "❌ Cancel", lang), "proj_bid_cancel") },
        };
        try { await _sender.SendTextMessageWithInlineKeyboardAsync(chatId, preview, kb, ct).ConfigureAwait(false); } catch { }
    }

    private async Task DoSubmitBid(long chatId, long userId, string? lang, int? editMsgId, CancellationToken ct)
    {
        if (_projBidRepo == null) return;
        var pidStr = await _stateStore.GetFlowDataAsync(userId, "proj_bid_pid", ct).ConfigureAwait(false) ?? "0";
        int.TryParse(pidStr, out var pid);
        var amountStr = await _stateStore.GetFlowDataAsync(userId, "proj_bid_amount", ct).ConfigureAwait(false) ?? "0";
        decimal.TryParse(amountStr, out var amount);
        var duration = await _stateStore.GetFlowDataAsync(userId, "proj_bid_duration", ct).ConfigureAwait(false);
        var letter = await _stateStore.GetFlowDataAsync(userId, "proj_bid_letter", ct).ConfigureAwait(false);
        var user = await SafeGetUser(userId, ct);
        var displayName = $"{user?.FirstName} {user?.LastName}".Trim();
        if (string.IsNullOrEmpty(displayName)) displayName = user?.Username ?? $"User_{userId}";

        await _projBidRepo.CreateAsync(new ProjectBidDto(0, pid, userId, displayName, amount, duration, letter, null, "pending", default), ct).ConfigureAwait(false);
        await _stateStore.ClearStateAsync(userId, ct).ConfigureAwait(false);
        await _stateStore.ClearAllFlowDataAsync(userId, ct).ConfigureAwait(false);
        await SafeDelete(chatId, editMsgId, ct);

        var msg = L("<b>✅ پیشنهاد شما ثبت شد</b>", "<b>✅ Your proposal has been submitted</b>", lang);
        var kb = new List<IReadOnlyList<InlineButton>> { new[] { new InlineButton(L("🔙 بازگشت", "🔙 Back", lang), "proj_menu") } };
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
