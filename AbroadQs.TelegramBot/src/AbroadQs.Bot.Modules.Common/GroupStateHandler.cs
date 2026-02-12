using AbroadQs.Bot.Contracts;

namespace AbroadQs.Bot.Modules.Common;

/// <summary>
/// Handles Exchange Groups: listing, filtering, and user submission.
/// Callback prefixes: grp_list, grp_filter, grp_submit
/// </summary>
public sealed class GroupStateHandler : IUpdateHandler
{
    private readonly IResponseSender _sender;
    private readonly ITelegramUserRepository _userRepo;
    private readonly IUserConversationStateStore _stateStore;
    private readonly IGroupRepository _groupRepo;
    private readonly IUserMessageStateRepository? _msgStateRepo;

    private const string BtnBack = "🔙 بازگشت";
    private const string BtnCancel = "❌ انصراف";

    public GroupStateHandler(
        IResponseSender sender,
        ITelegramUserRepository userRepo,
        IUserConversationStateStore stateStore,
        IGroupRepository groupRepo,
        IUserMessageStateRepository? msgStateRepo = null)
    {
        _sender = sender;
        _userRepo = userRepo;
        _stateStore = stateStore;
        _groupRepo = groupRepo;
        _msgStateRepo = msgStateRepo;
    }

    public string? Command => null;

    public bool CanHandle(BotUpdateContext context)
    {
        if (context.UserId == null) return false;
        if (context.IsCallbackQuery)
        {
            var cb = context.MessageText?.Trim() ?? "";
            return cb.StartsWith("grp_", StringComparison.Ordinal);
        }
        return !string.IsNullOrEmpty(context.MessageText);
    }

    public async Task<bool> HandleAsync(BotUpdateContext context, CancellationToken ct)
    {
        if (context.UserId == null) return false;
        var userId = context.UserId.Value;
        var chatId = context.ChatId;

        // ── Callback queries ──
        if (context.IsCallbackQuery)
        {
            var cb = context.MessageText?.Trim() ?? "";
            await SafeAnswerCallback(context.CallbackQueryId, ct);
            var editMsgId = context.CallbackMessageId;

            if (cb == "grp_list_all") { await ShowGroupList(chatId, null, null, null, editMsgId, ct); return true; }
            if (cb == "grp_filter_currency") { await ShowCurrencyFilter(chatId, editMsgId, ct); return true; }
            if (cb == "grp_filter_country") { await ShowCountryFilter(chatId, editMsgId, ct); return true; }
            if (cb.StartsWith("grp_fc:")) { await ShowGroupList(chatId, null, cb["grp_fc:".Length..], null, editMsgId, ct); return true; }
            if (cb.StartsWith("grp_fk:")) { await ShowGroupList(chatId, null, null, cb["grp_fk:".Length..], editMsgId, ct); return true; }
            if (cb == "grp_submit_start") { await StartGroupSubmission(chatId, userId, editMsgId, ct); return true; }
            if (cb == "grp_submit_cancel")
            {
                await _stateStore.ClearStateAsync(userId, ct).ConfigureAwait(false);
                await _stateStore.ClearAllFlowDataAsync(userId, ct).ConfigureAwait(false);
                await SafeDelete(chatId, editMsgId, ct);
                await ShowGroupsMenu(chatId, null, ct);
                return true;
            }
            if (cb == "grp_submit_confirm") { await DoSubmitGroup(chatId, userId, editMsgId, ct); return true; }
            if (cb == "grp_menu") { await ShowGroupsMenu(chatId, editMsgId, ct); return true; }

            return false;
        }

        // ── Text messages — only if user is in group submission flow ──
        var state = await _stateStore.GetStateAsync(userId, ct).ConfigureAwait(false);
        if (state == null || !state.StartsWith("grp_")) return false;

        var text = context.MessageText?.Trim() ?? "";
        if (string.IsNullOrEmpty(text)) return false;

        if (text == BtnCancel)
        {
            await CleanUserMsg(chatId, context.IncomingMessageId, ct);
            await DeletePrevBotMsg(chatId, userId, ct);
            await _stateStore.ClearStateAsync(userId, ct).ConfigureAwait(false);
            await _stateStore.ClearAllFlowDataAsync(userId, ct).ConfigureAwait(false);
            await RemoveReplyKbSilent(chatId, ct);
            await ShowGroupsMenu(chatId, null, ct);
            return true;
        }

        return state switch
        {
            "grp_submit_link" => await HandleLinkInput(chatId, userId, text, context.IncomingMessageId, ct),
            "grp_submit_type" => await HandleTypeInput(chatId, userId, text, context.IncomingMessageId, ct),
            "grp_submit_currency" => await HandleCurrencyInput(chatId, userId, text, context.IncomingMessageId, ct),
            "grp_submit_country" => await HandleCountryInput(chatId, userId, text, context.IncomingMessageId, ct),
            "grp_submit_desc" => await HandleDescInput(chatId, userId, text, context.IncomingMessageId, ct),
            _ => false
        };
    }

    // ═══════════════════════════════════════════════════════════════
    //  Show groups menu (entry point from DynamicStageHandler)
    // ═══════════════════════════════════════════════════════════════

    public async Task ShowGroupsMenu(long chatId, int? editMsgId, CancellationToken ct)
    {
        var text = "<b>👥 گروه‌های تبادل ارز</b>\n━━━━━━━━━━━━━━━━━━━\n\n" +
                   "از این بخش می‌توانید گروه‌های تبادل ارز را مشاهده و عضو شوید.\n" +
                   "همچنین می‌توانید گروه خودتان را برای تأیید ارسال کنید.";

        var kb = new List<IReadOnlyList<InlineButton>>
        {
            new[] { new InlineButton("📋 همه گروه‌ها", "grp_list_all") },
            new[] { new InlineButton("💱 فیلتر ارز", "grp_filter_currency"), new InlineButton("🌍 فیلتر کشور", "grp_filter_country") },
            new[] { new InlineButton("➕ ثبت گروه جدید", "grp_submit_start") },
            new[] { new InlineButton("🔙 بازگشت", "stage:student_exchange") },
        };

        if (editMsgId.HasValue)
        {
            try { await _sender.EditMessageTextWithInlineKeyboardAsync(chatId, editMsgId.Value, text, kb, ct).ConfigureAwait(false); return; }
            catch { }
        }
        await SafeSendInline(chatId, text, kb, ct);
    }

    private async Task ShowGroupList(long chatId, string? groupType, string? currencyCode, string? countryCode, int? editMsgId, CancellationToken ct)
    {
        var groups = await _groupRepo.ListGroupsAsync("approved", groupType, currencyCode, countryCode, ct).ConfigureAwait(false);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<b>👥 گروه‌های تبادل ارز</b>");
        sb.AppendLine("━━━━━━━━━━━━━━━━━━━\n");

        if (groups.Count == 0)
        {
            sb.AppendLine("📭 گروهی یافت نشد.");
        }

        var kb = new List<IReadOnlyList<InlineButton>>();
        foreach (var g in groups.Take(20))
        {
            var badge = g.IsOfficial ? "⭐ " : "";
            var label = $"{badge}{g.Name}";
            if (!string.IsNullOrEmpty(g.CurrencyCode))
                label += $" ({ExchangeStateHandler.GetCurrencyFlag(g.CurrencyCode)} {g.CurrencyCode})";
            kb.Add(new[] { new InlineButton(label, null, g.TelegramGroupLink) });
        }

        kb.Add(new[] { new InlineButton("🔙 بازگشت", "grp_menu") });

        if (editMsgId.HasValue)
        {
            try { await _sender.EditMessageTextWithInlineKeyboardAsync(chatId, editMsgId.Value, sb.ToString(), kb, ct).ConfigureAwait(false); return; }
            catch { }
        }
        await SafeSendInline(chatId, sb.ToString(), kb, ct);
    }

    private async Task ShowCurrencyFilter(long chatId, int? editMsgId, CancellationToken ct)
    {
        var text = "<b>💱 فیلتر بر اساس ارز</b>\n\nارز مورد نظر را انتخاب کنید:";
        var currencies = new[] { "USD", "EUR", "GBP", "CAD", "AED", "TRY", "AFN", "USDT" };
        var kb = new List<IReadOnlyList<InlineButton>>();
        for (int i = 0; i < currencies.Length; i += 3)
        {
            var row = new List<InlineButton>();
            for (int j = i; j < Math.Min(i + 3, currencies.Length); j++)
            {
                var c = currencies[j];
                row.Add(new InlineButton($"{ExchangeStateHandler.GetCurrencyFlag(c)} {c}", $"grp_fc:{c}"));
            }
            kb.Add(row);
        }
        kb.Add(new[] { new InlineButton("🔙 بازگشت", "grp_menu") });

        if (editMsgId.HasValue)
        {
            try { await _sender.EditMessageTextWithInlineKeyboardAsync(chatId, editMsgId.Value, text, kb, ct).ConfigureAwait(false); return; }
            catch { }
        }
        await SafeSendInline(chatId, text, kb, ct);
    }

    private async Task ShowCountryFilter(long chatId, int? editMsgId, CancellationToken ct)
    {
        var text = "<b>🌍 فیلتر بر اساس کشور</b>\n\nکشور مورد نظر را انتخاب کنید:";
        var countries = new[] { ("nl", "🇳🇱 هلند"), ("de", "🇩🇪 آلمان"), ("us", "🇺🇸 آمریکا"), ("gb", "🇬🇧 انگلیس"), ("fr", "🇫🇷 فرانسه"), ("ca", "🇨🇦 کانادا"), ("tr", "🇹🇷 ترکیه"), ("ir", "🇮🇷 ایران") };
        var kb = new List<IReadOnlyList<InlineButton>>();
        for (int i = 0; i < countries.Length; i += 3)
        {
            var row = new List<InlineButton>();
            for (int j = i; j < Math.Min(i + 3, countries.Length); j++)
                row.Add(new InlineButton(countries[j].Item2, $"grp_fk:{countries[j].Item1}"));
            kb.Add(row);
        }
        kb.Add(new[] { new InlineButton("🔙 بازگشت", "grp_menu") });

        if (editMsgId.HasValue)
        {
            try { await _sender.EditMessageTextWithInlineKeyboardAsync(chatId, editMsgId.Value, text, kb, ct).ConfigureAwait(false); return; }
            catch { }
        }
        await SafeSendInline(chatId, text, kb, ct);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Group submission flow
    // ═══════════════════════════════════════════════════════════════

    private async Task StartGroupSubmission(long chatId, long userId, int? editMsgId, CancellationToken ct)
    {
        await SafeDelete(chatId, editMsgId, ct);
        await _stateStore.ClearAllFlowDataAsync(userId, ct).ConfigureAwait(false);
        await _stateStore.SetStateAsync(userId, "grp_submit_link", ct).ConfigureAwait(false);

        var msg = "<b>➕ ثبت گروه جدید — مرحله ۱ از ۴</b>\n━━━━━━━━━━━━━━━━━━━\n\n" +
                  "لینک گروه تلگرام را ارسال کنید:\n" +
                  "<i>مثال: https://t.me/mygroup یا @mygroup</i>";
        var kb = new List<IReadOnlyList<string>> { new[] { BtnCancel } };
        await SafeSendReplyKb(chatId, msg, kb, ct);
    }

    private async Task<bool> HandleLinkInput(long chatId, long userId, string text, int? userMsgId, CancellationToken ct)
    {
        if (!text.Contains("t.me/") && !text.StartsWith("@"))
        { await CleanUserMsg(chatId, userMsgId, ct); return true; }

        await _stateStore.SetFlowDataAsync(userId, "grp_link", text.Trim(), ct).ConfigureAwait(false);
        await CleanUserMsg(chatId, userMsgId, ct);
        await DeletePrevBotMsg(chatId, userId, ct);
        await _stateStore.SetStateAsync(userId, "grp_submit_type", ct).ConfigureAwait(false);

        var msg = "<b>➕ ثبت گروه — مرحله ۲ از ۴</b>\n━━━━━━━━━━━━━━━━━━━\n\n" +
                  "نوع گروه را مشخص کنید:";
        var kb = new List<IReadOnlyList<string>>
        {
            new[] { "💱 مخصوص ارز", "🌍 مخصوص کشور" },
            new[] { "📋 عمومی" },
            new[] { BtnCancel },
        };
        await SafeSendReplyKb(chatId, msg, kb, ct);
        return true;
    }

    private async Task<bool> HandleTypeInput(long chatId, long userId, string text, int? userMsgId, CancellationToken ct)
    {
        string? grpType = null;
        if (text.Contains("ارز")) grpType = "currency";
        else if (text.Contains("کشور")) grpType = "country";
        else if (text.Contains("عمومی")) grpType = "general";

        if (grpType == null) { await CleanUserMsg(chatId, userMsgId, ct); return true; }

        await _stateStore.SetFlowDataAsync(userId, "grp_type", grpType, ct).ConfigureAwait(false);
        await CleanUserMsg(chatId, userMsgId, ct);
        await DeletePrevBotMsg(chatId, userId, ct);

        if (grpType == "currency")
        {
            await _stateStore.SetStateAsync(userId, "grp_submit_currency", ct).ConfigureAwait(false);
            var msg = "<b>➕ ثبت گروه — مرحله ۳ از ۴</b>\n━━━━━━━━━━━━━━━━━━━\n\n" +
                      "کد ارز مرتبط را انتخاب کنید:";
            var currencies = new[] { "🇺🇸 USD", "🇪🇺 EUR", "🇬🇧 GBP", "🇨🇦 CAD", "🇦🇪 AED", "🇹🇷 TRY", "🇦🇫 AFN", "💲 USDT" };
            var kb = new List<IReadOnlyList<string>>
            {
                new[] { currencies[0], currencies[1], currencies[2] },
                new[] { currencies[3], currencies[4], currencies[5] },
                new[] { currencies[6], currencies[7] },
                new[] { BtnCancel },
            };
            await SafeSendReplyKb(chatId, msg, kb, ct);
        }
        else if (grpType == "country")
        {
            await _stateStore.SetStateAsync(userId, "grp_submit_country", ct).ConfigureAwait(false);
            var msg = "<b>➕ ثبت گروه — مرحله ۳ از ۴</b>\n━━━━━━━━━━━━━━━━━━━\n\n" +
                      "نام یا کد کشور مرتبط را تایپ کنید:";
            var kb = new List<IReadOnlyList<string>> { new[] { BtnCancel } };
            await SafeSendReplyKb(chatId, msg, kb, ct);
        }
        else
        {
            // General — skip to description
            await ShowDescStep(chatId, userId, ct);
        }
        return true;
    }

    private async Task<bool> HandleCurrencyInput(long chatId, long userId, string text, int? userMsgId, CancellationToken ct)
    {
        var codes = new[] { "USD", "EUR", "GBP", "CAD", "AED", "TRY", "AFN", "USDT" };
        var match = codes.FirstOrDefault(c => text.Contains(c, StringComparison.OrdinalIgnoreCase));
        if (match == null) { await CleanUserMsg(chatId, userMsgId, ct); return true; }

        await _stateStore.SetFlowDataAsync(userId, "grp_currency", match, ct).ConfigureAwait(false);
        await CleanUserMsg(chatId, userMsgId, ct);
        await DeletePrevBotMsg(chatId, userId, ct);
        await ShowDescStep(chatId, userId, ct);
        return true;
    }

    private async Task<bool> HandleCountryInput(long chatId, long userId, string text, int? userMsgId, CancellationToken ct)
    {
        await _stateStore.SetFlowDataAsync(userId, "grp_country", text.Trim(), ct).ConfigureAwait(false);
        await CleanUserMsg(chatId, userMsgId, ct);
        await DeletePrevBotMsg(chatId, userId, ct);
        await ShowDescStep(chatId, userId, ct);
        return true;
    }

    private async Task ShowDescStep(long chatId, long userId, CancellationToken ct)
    {
        await _stateStore.SetStateAsync(userId, "grp_submit_desc", ct).ConfigureAwait(false);
        var msg = "<b>➕ ثبت گروه — مرحله ۴ از ۴</b>\n━━━━━━━━━━━━━━━━━━━\n\n" +
                  "توضیح کوتاهی درباره گروه بنویسید:";
        var kb = new List<IReadOnlyList<string>> { new[] { BtnCancel } };
        await SafeSendReplyKb(chatId, msg, kb, ct);
    }

    private async Task<bool> HandleDescInput(long chatId, long userId, string text, int? userMsgId, CancellationToken ct)
    {
        await _stateStore.SetFlowDataAsync(userId, "grp_desc", text, ct).ConfigureAwait(false);
        await CleanUserMsg(chatId, userMsgId, ct);
        await DeletePrevBotMsg(chatId, userId, ct);

        // Show preview
        await _stateStore.SetStateAsync(userId, "grp_submit_preview", ct).ConfigureAwait(false);
        await RemoveReplyKbSilent(chatId, ct);

        var link = await _stateStore.GetFlowDataAsync(userId, "grp_link", ct).ConfigureAwait(false) ?? "";
        var grpType = await _stateStore.GetFlowDataAsync(userId, "grp_type", ct).ConfigureAwait(false) ?? "general";
        var currency = await _stateStore.GetFlowDataAsync(userId, "grp_currency", ct).ConfigureAwait(false);
        var country = await _stateStore.GetFlowDataAsync(userId, "grp_country", ct).ConfigureAwait(false);
        var typeFa = grpType == "currency" ? "مخصوص ارز" : grpType == "country" ? "مخصوص کشور" : "عمومی";

        var preview = $"<b>📋 پیش‌نمایش گروه</b>\n━━━━━━━━━━━━━━━━━━━\n\n" +
                      $"🔗 لینک: {link}\n" +
                      $"📁 نوع: {typeFa}\n" +
                      (!string.IsNullOrEmpty(currency) ? $"💱 ارز: {currency}\n" : "") +
                      (!string.IsNullOrEmpty(country) ? $"🌍 کشور: {country}\n" : "") +
                      $"📝 توضیحات: {text}\n\n" +
                      "<i>گروه شما پس از تأیید ادمین نمایش داده خواهد شد.</i>";

        var kb = new List<IReadOnlyList<InlineButton>>
        {
            new[] { new InlineButton("✅ ارسال برای تأیید", "grp_submit_confirm") },
            new[] { new InlineButton("❌ انصراف", "grp_submit_cancel") },
        };
        await SafeSendInline(chatId, preview, kb, ct);
        return true;
    }

    private async Task DoSubmitGroup(long chatId, long userId, int? triggerMsgId, CancellationToken ct)
    {
        var link = await _stateStore.GetFlowDataAsync(userId, "grp_link", ct).ConfigureAwait(false) ?? "";
        var grpType = await _stateStore.GetFlowDataAsync(userId, "grp_type", ct).ConfigureAwait(false) ?? "general";
        var currency = await _stateStore.GetFlowDataAsync(userId, "grp_currency", ct).ConfigureAwait(false);
        var country = await _stateStore.GetFlowDataAsync(userId, "grp_country", ct).ConfigureAwait(false);
        var desc = await _stateStore.GetFlowDataAsync(userId, "grp_desc", ct).ConfigureAwait(false);

        var dto = new ExchangeGroupDto(
            Id: 0, Name: desc ?? link, TelegramGroupId: null, TelegramGroupLink: link,
            GroupType: grpType, CurrencyCode: currency, CountryCode: country,
            Description: desc, MemberCount: 0, SubmittedByUserId: userId,
            Status: "pending", IsOfficial: false, CreatedAt: DateTimeOffset.UtcNow, UpdatedAt: null);

        await _groupRepo.CreateGroupAsync(dto, ct).ConfigureAwait(false);

        await _stateStore.ClearStateAsync(userId, ct).ConfigureAwait(false);
        await _stateStore.ClearAllFlowDataAsync(userId, ct).ConfigureAwait(false);
        await SafeDelete(chatId, triggerMsgId, ct);

        await SafeSendInline(chatId,
            "✅ <b>گروه شما با موفقیت ثبت شد</b>\n\nپس از تأیید ادمین در لیست گروه‌ها نمایش داده خواهد شد.",
            new List<IReadOnlyList<InlineButton>>
            {
                new[] { new InlineButton("👥 مشاهده گروه‌ها", "grp_menu") },
                new[] { new InlineButton("🏠 منوی اصلی", "stage:main_menu") },
            }, ct);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Helpers
    // ═══════════════════════════════════════════════════════════════

    private async Task SafeSendReplyKb(long chatId, string text, List<IReadOnlyList<string>> kb, CancellationToken ct)
    { try { await _sender.SendTextMessageWithReplyKeyboardAsync(chatId, text, kb, ct).ConfigureAwait(false); } catch { } }
    private async Task SafeSendInline(long chatId, string text, List<IReadOnlyList<InlineButton>> kb, CancellationToken ct)
    {
        try
        {
            // Always remove the reply keyboard first so the phone soft keyboard closes
            await RemoveReplyKbSilent(chatId, ct);
            await _sender.SendTextMessageWithInlineKeyboardAsync(chatId, text, kb, ct).ConfigureAwait(false);
        }
        catch { }
    }
    private async Task SafeDelete(long chatId, int? msgId, CancellationToken ct)
    { if (msgId.HasValue) try { await _sender.DeleteMessageAsync(chatId, msgId.Value, ct).ConfigureAwait(false); } catch { } }
    private async Task SafeAnswerCallback(string? id, CancellationToken ct)
    { if (id != null) try { await _sender.AnswerCallbackQueryAsync(id, null, ct).ConfigureAwait(false); } catch { } }
    private async Task CleanUserMsg(long chatId, int? msgId, CancellationToken ct) => await SafeDelete(chatId, msgId, ct);
    private async Task RemoveReplyKbSilent(long chatId, CancellationToken ct)
    { try { await _sender.RemoveReplyKeyboardSilentAsync(chatId, ct).ConfigureAwait(false); } catch { } }
    private async Task DeletePrevBotMsg(long chatId, long userId, CancellationToken ct)
    {
        if (_msgStateRepo == null) return;
        try { var s = await _msgStateRepo.GetUserMessageStateAsync(userId, ct).ConfigureAwait(false); if (s?.LastBotTelegramMessageId is > 0) await SafeDelete(chatId, (int)s.LastBotTelegramMessageId, ct); } catch { }
    }
}
