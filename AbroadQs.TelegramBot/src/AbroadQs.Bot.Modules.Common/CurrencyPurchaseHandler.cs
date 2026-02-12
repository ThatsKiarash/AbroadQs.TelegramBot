using AbroadQs.Bot.Contracts;
using static AbroadQs.Bot.Contracts.BilingualHelper;

namespace AbroadQs.Bot.Modules.Common;

/// <summary>
/// Phase 8: Direct Currency Purchase / Crypto Wallets — manage crypto wallets, buy currency via wallet balance.
/// Callback prefix: cp_   States: cp_currency, cp_amount, cp_wallet_addr, cp_wallet_net
/// </summary>
public sealed class CurrencyPurchaseHandler : IUpdateHandler
{
    private readonly IResponseSender _sender;
    private readonly ITelegramUserRepository _userRepo;
    private readonly IUserConversationStateStore _stateStore;
    private readonly ICryptoWalletRepository? _cryptoRepo;
    private readonly IWalletRepository? _walletRepo;
    private readonly IUserMessageStateRepository? _msgStateRepo;

    public CurrencyPurchaseHandler(IResponseSender sender, ITelegramUserRepository userRepo,
        IUserConversationStateStore stateStore, ICryptoWalletRepository? cryptoRepo = null,
        IWalletRepository? walletRepo = null, IUserMessageStateRepository? msgStateRepo = null)
    {
        _sender = sender; _userRepo = userRepo; _stateStore = stateStore;
        _cryptoRepo = cryptoRepo; _walletRepo = walletRepo; _msgStateRepo = msgStateRepo;
    }

    public string? Command => null;

    public bool CanHandle(BotUpdateContext context)
    {
        if (context.UserId == null) return false;
        if (context.IsCallbackQuery)
            return (context.MessageText?.Trim() ?? "").StartsWith("cp_", StringComparison.Ordinal);
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

            if (cb == "cp_menu") { await ShowMenu(chatId, userId, lang, eid, ct); return true; }
            if (cb == "cp_buy") { await StartBuy(chatId, userId, lang, eid, ct); return true; }
            if (cb == "cp_wallets") { await ShowWallets(chatId, userId, lang, eid, ct); return true; }
            if (cb == "cp_add_wallet") { await StartAddWallet(chatId, userId, lang, eid, ct); return true; }
            if (cb.StartsWith("cp_select_cur:")) { await SelectCurrency(chatId, userId, cb["cp_select_cur:".Length..], lang, eid, ct); return true; }
            if (cb == "cp_confirm_buy") { await DoBuy(chatId, userId, lang, eid, ct); return true; }
            if (cb == "cp_confirm_wallet") { await DoAddWallet(chatId, userId, lang, eid, ct); return true; }
            if (cb.StartsWith("cp_del_wallet:")) { int.TryParse(cb["cp_del_wallet:".Length..], out var wid); await DeleteWallet(chatId, userId, wid, lang, eid, ct); return true; }
            if (cb == "cp_cancel") { await CancelFlow(chatId, userId, lang, eid, ct); return true; }
            if (cb == "cp_purchases") { await ShowPurchases(chatId, userId, lang, 0, eid, ct); return true; }
            if (cb.StartsWith("cp_purch_p:")) { int.TryParse(cb["cp_purch_p:".Length..], out var p); await ShowPurchases(chatId, userId, lang, p, eid, ct); return true; }
            return false;
        }

        var state = await _stateStore.GetStateAsync(userId, ct).ConfigureAwait(false);
        if (state == null || !state.StartsWith("cp_")) return false;
        var text = context.MessageText?.Trim() ?? "";
        if (string.IsNullOrEmpty(text)) return false;
        if (text.Contains(L("انصراف", "Cancel", lang))) { await CancelFlow(chatId, userId, lang, null, ct); await SafeDelete(chatId, context.IncomingMessageId, ct); return true; }

        return state switch
        {
            "cp_amount" => await HandleBuyAmount(chatId, userId, text, lang, context.IncomingMessageId, ct),
            "cp_wallet_addr" => await HandleWalletAddr(chatId, userId, text, lang, context.IncomingMessageId, ct),
            "cp_wallet_net" => await HandleWalletNet(chatId, userId, text, lang, context.IncomingMessageId, ct),
            _ => false
        };
    }

    public async Task ShowMenu(long chatId, long userId, string? lang, int? editMsgId, CancellationToken ct)
    {
        var text = L("<b>💱 خرید مستقیم ارز / کیف پول کریپتو</b>\n━━━━━━━━━━━━━━━━━━━\n\nخرید ارز مستقیم یا مدیریت کیف پول‌های کریپتو.",
                     "<b>💱 Direct Currency / Crypto Wallets</b>\n━━━━━━━━━━━━━━━━━━━\n\nBuy currency directly or manage crypto wallets.", lang);
        var kb = new List<IReadOnlyList<InlineButton>>
        {
            new[] { new InlineButton(L("💰 خرید ارز", "💰 Buy Currency", lang), "cp_buy") },
            new[] { new InlineButton(L("📂 کیف پول‌ها", "📂 Wallets", lang), "cp_wallets") },
            new[] { new InlineButton(L("📜 تاریخچه خرید", "📜 Purchase History", lang), "cp_purchases") },
            new[] { new InlineButton(L("🔙 بازگشت", "🔙 Back", lang), "stage:new_request") },
        };
        if (editMsgId.HasValue) { try { await _sender.EditMessageTextWithInlineKeyboardAsync(chatId, editMsgId.Value, text, kb, ct).ConfigureAwait(false); return; } catch { } }
        try { await _sender.RemoveReplyKeyboardSilentAsync(chatId, ct).ConfigureAwait(false); } catch { }
        try { await _sender.SendTextMessageWithInlineKeyboardAsync(chatId, text, kb, ct).ConfigureAwait(false); } catch { }
    }

    private async Task StartBuy(long chatId, long userId, string? lang, int? editMsgId, CancellationToken ct)
    {
        await SafeDelete(chatId, editMsgId, ct);
        await _stateStore.ClearAllFlowDataAsync(userId, ct).ConfigureAwait(false);
        var text = L("<b>💱 انتخاب ارز</b>\n━━━━━━━━━━━━━━━━━━━\n\nارز مورد نظر خود را انتخاب کنید:",
                     "<b>💱 Select Currency</b>\n━━━━━━━━━━━━━━━━━━━\n\nChoose the currency to buy:", lang);
        var currencies = new[] { "USD", "EUR", "GBP", "CAD", "AUD", "USDT", "BTC", "ETH" };
        var rows = new List<IReadOnlyList<InlineButton>>();
        for (var i = 0; i < currencies.Length; i += 2)
        {
            var row = new List<InlineButton> { new InlineButton(currencies[i], $"cp_select_cur:{currencies[i]}") };
            if (i + 1 < currencies.Length) row.Add(new InlineButton(currencies[i + 1], $"cp_select_cur:{currencies[i + 1]}"));
            rows.Add(row);
        }
        rows.Add(new[] { new InlineButton(L("❌ انصراف", "❌ Cancel", lang), "cp_cancel") });
        try { await _sender.SendTextMessageWithInlineKeyboardAsync(chatId, text, rows, ct).ConfigureAwait(false); } catch { }
    }

    private async Task SelectCurrency(long chatId, long userId, string currency, string? lang, int? editMsgId, CancellationToken ct)
    {
        await SafeDelete(chatId, editMsgId, ct);
        await _stateStore.SetFlowDataAsync(userId, "cp_currency", currency, ct).ConfigureAwait(false);
        await _stateStore.SetStateAsync(userId, "cp_amount", ct).ConfigureAwait(false);
        var msg = L($"<b>💱 خرید {currency}</b>\n━━━━━━━━━━━━━━━━━━━\n\nمقدار مورد نظر (واحد ارز) را وارد کنید:",
                    $"<b>💱 Buy {currency}</b>\n━━━━━━━━━━━━━━━━━━━\n\nEnter the amount (in {currency}):", lang);
        var kb = new List<IReadOnlyList<string>> { new[] { L("❌ انصراف", "❌ Cancel", lang) } };
        try { await _sender.SendTextMessageWithReplyKeyboardAsync(chatId, msg, kb, ct).ConfigureAwait(false); } catch { }
    }

    private async Task<bool> HandleBuyAmount(long chatId, long userId, string text, string? lang, int? userMsgId, CancellationToken ct)
    {
        await SafeDelete(chatId, userMsgId, ct);
        await DeletePrevBotMsg(chatId, userId, ct);
        if (!decimal.TryParse(text.Replace(",", ""), out var amount) || amount <= 0)
        {
            await _sender.SendTextMessageAsync(chatId, L("⚠️ عدد نامعتبر.", "⚠️ Invalid amount.", lang), ct).ConfigureAwait(false);
            return true;
        }
        await _stateStore.SetFlowDataAsync(userId, "cp_amount", amount.ToString(), ct).ConfigureAwait(false);
        await _stateStore.SetStateAsync(userId, "cp_preview", ct).ConfigureAwait(false);
        try { await _sender.RemoveReplyKeyboardSilentAsync(chatId, ct).ConfigureAwait(false); } catch { }

        var currency = await _stateStore.GetFlowDataAsync(userId, "cp_currency", ct).ConfigureAwait(false) ?? "?";
        // Simple price estimation (in a real system, use live exchange rate)
        var preview = L($"<b>📋 پیش‌نمایش خرید</b>\n━━━━━━━━━━━━━━━━━━━\n\n💱 ارز: {currency}\n💰 مقدار: {amount:N4}\n\n(قیمت نهایی پس از تأیید محاسبه می‌شود)",
                        $"<b>📋 Purchase Preview</b>\n━━━━━━━━━━━━━━━━━━━\n\n💱 Currency: {currency}\n💰 Amount: {amount:N4}\n\n(Final price will be calculated after confirmation)", lang);
        var kb = new List<IReadOnlyList<InlineButton>>
        {
            new[] { new InlineButton(L("✅ تأیید خرید", "✅ Confirm Purchase", lang), "cp_confirm_buy") },
            new[] { new InlineButton(L("❌ انصراف", "❌ Cancel", lang), "cp_cancel") },
        };
        try { await _sender.SendTextMessageWithInlineKeyboardAsync(chatId, preview, kb, ct).ConfigureAwait(false); } catch { }
        return true;
    }

    private async Task DoBuy(long chatId, long userId, string? lang, int? editMsgId, CancellationToken ct)
    {
        if (_cryptoRepo == null || _walletRepo == null) return;
        var currency = await _stateStore.GetFlowDataAsync(userId, "cp_currency", ct).ConfigureAwait(false) ?? "";
        var amtStr = await _stateStore.GetFlowDataAsync(userId, "cp_amount", ct).ConfigureAwait(false) ?? "0";
        decimal.TryParse(amtStr, out var amount);

        await _stateStore.ClearStateAsync(userId, ct).ConfigureAwait(false);
        await _stateStore.ClearAllFlowDataAsync(userId, ct).ConfigureAwait(false);
        await SafeDelete(chatId, editMsgId, ct);

        // Create the purchase record (status = pending, admin will process)
        await _cryptoRepo.CreatePurchaseAsync(new CurrencyPurchaseDto(0, userId, currency, amount, 0, "pending", null, default), ct).ConfigureAwait(false);

        var msg = L($"<b>✅ درخواست خرید {amount:N4} {currency} ثبت شد</b>\n\nپس از تأیید ادمین، مبلغ از کیف پول شما کسر و ارز واریز خواهد شد.",
                    $"<b>✅ Purchase request for {amount:N4} {currency} submitted</b>\n\nAfter admin approval, your wallet will be charged and currency delivered.", lang);
        var kb = new List<IReadOnlyList<InlineButton>> { new[] { new InlineButton(L("🔙 بازگشت", "🔙 Back", lang), "cp_menu") } };
        try { await _sender.SendTextMessageWithInlineKeyboardAsync(chatId, msg, kb, ct).ConfigureAwait(false); } catch { }
    }

    private async Task ShowWallets(long chatId, long userId, string? lang, int? editMsgId, CancellationToken ct)
    {
        if (_cryptoRepo == null) return;
        var wallets = await _cryptoRepo.ListWalletsAsync(userId, ct).ConfigureAwait(false);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(L("<b>📂 کیف پول‌های کریپتو</b>", "<b>📂 Crypto Wallets</b>", lang));
        sb.AppendLine("━━━━━━━━━━━━━━━━━━━\n");
        if (wallets.Count == 0) sb.AppendLine(L("📭 کیف پولی ثبت نشده.", "📭 No wallets registered.", lang));
        var kb = new List<IReadOnlyList<InlineButton>>();
        foreach (var w in wallets)
        {
            sb.AppendLine($"🔑 {w.CurrencySymbol} — {w.Network}\n   {w.WalletAddress[..Math.Min(20, w.WalletAddress.Length)]}...\n");
            kb.Add(new[] { new InlineButton(L($"🗑 حذف {w.CurrencySymbol}", $"🗑 Delete {w.CurrencySymbol}", lang), $"cp_del_wallet:{w.Id}") });
        }
        kb.Add(new[] { new InlineButton(L("➕ افزودن کیف پول", "➕ Add Wallet", lang), "cp_add_wallet") });
        kb.Add(new[] { new InlineButton(L("🔙 بازگشت", "🔙 Back", lang), "cp_menu") });

        if (editMsgId.HasValue) { try { await _sender.EditMessageTextWithInlineKeyboardAsync(chatId, editMsgId.Value, sb.ToString(), kb, ct).ConfigureAwait(false); return; } catch { } }
        try { await _sender.RemoveReplyKeyboardSilentAsync(chatId, ct).ConfigureAwait(false); } catch { }
        try { await _sender.SendTextMessageWithInlineKeyboardAsync(chatId, sb.ToString(), kb, ct).ConfigureAwait(false); } catch { }
    }

    private async Task StartAddWallet(long chatId, long userId, string? lang, int? editMsgId, CancellationToken ct)
    {
        await SafeDelete(chatId, editMsgId, ct);
        await _stateStore.ClearAllFlowDataAsync(userId, ct).ConfigureAwait(false);
        await _stateStore.SetStateAsync(userId, "cp_wallet_addr", ct).ConfigureAwait(false);
        var msg = L("<b>➕ افزودن کیف پول</b>\n━━━━━━━━━━━━━━━━━━━\n\nآدرس کیف پول خود را وارد کنید:",
                    "<b>➕ Add Wallet</b>\n━━━━━━━━━━━━━━━━━━━\n\nEnter your wallet address:", lang);
        var kb = new List<IReadOnlyList<string>> { new[] { L("❌ انصراف", "❌ Cancel", lang) } };
        try { await _sender.SendTextMessageWithReplyKeyboardAsync(chatId, msg, kb, ct).ConfigureAwait(false); } catch { }
    }

    private async Task<bool> HandleWalletAddr(long chatId, long userId, string text, string? lang, int? userMsgId, CancellationToken ct)
    {
        await SafeDelete(chatId, userMsgId, ct);
        await DeletePrevBotMsg(chatId, userId, ct);
        await _stateStore.SetFlowDataAsync(userId, "cp_wallet_addr", text, ct).ConfigureAwait(false);
        await _stateStore.SetStateAsync(userId, "cp_wallet_net", ct).ConfigureAwait(false);
        var msg = L("<b>شبکه کیف پول</b>\n━━━━━━━━━━━━━━━━━━━\n\nشبکه را انتخاب یا وارد کنید (مثلاً TRC20, ERC20, BTC, SOL):",
                    "<b>Wallet Network</b>\n━━━━━━━━━━━━━━━━━━━\n\nSelect or enter network (e.g. TRC20, ERC20, BTC, SOL):", lang);
        var kb = new List<IReadOnlyList<string>> { new[] { "TRC20", "ERC20", "BTC", "SOL" }, new[] { L("❌ انصراف", "❌ Cancel", lang) } };
        try { await _sender.SendTextMessageWithReplyKeyboardAsync(chatId, msg, kb, ct).ConfigureAwait(false); } catch { }
        return true;
    }

    private async Task<bool> HandleWalletNet(long chatId, long userId, string text, string? lang, int? userMsgId, CancellationToken ct)
    {
        await SafeDelete(chatId, userMsgId, ct);
        await DeletePrevBotMsg(chatId, userId, ct);
        await _stateStore.SetFlowDataAsync(userId, "cp_wallet_net", text, ct).ConfigureAwait(false);
        await _stateStore.SetStateAsync(userId, "cp_wallet_preview", ct).ConfigureAwait(false);
        try { await _sender.RemoveReplyKeyboardSilentAsync(chatId, ct).ConfigureAwait(false); } catch { }

        var addr = await _stateStore.GetFlowDataAsync(userId, "cp_wallet_addr", ct).ConfigureAwait(false) ?? "";
        var network = text;
        // Detect currency from network
        var cur = network.ToUpperInvariant() switch
        {
            "TRC20" or "ERC20" => "USDT",
            "BTC" => "BTC",
            "SOL" => "SOL",
            _ => "USDT"
        };
        await _stateStore.SetFlowDataAsync(userId, "cp_wallet_cur", cur, ct).ConfigureAwait(false);
        var preview = L($"<b>📋 پیش‌نمایش</b>\n━━━━━━━━━━━━━━━━━━━\n\n🔑 آدرس: {addr[..Math.Min(30, addr.Length)]}...\n🌐 شبکه: {network}\n💱 ارز: {cur}",
                        $"<b>📋 Preview</b>\n━━━━━━━━━━━━━━━━━━━\n\n🔑 Address: {addr[..Math.Min(30, addr.Length)]}...\n🌐 Network: {network}\n💱 Currency: {cur}", lang);
        var kb = new List<IReadOnlyList<InlineButton>>
        {
            new[] { new InlineButton(L("✅ ذخیره", "✅ Save", lang), "cp_confirm_wallet") },
            new[] { new InlineButton(L("❌ انصراف", "❌ Cancel", lang), "cp_cancel") },
        };
        try { await _sender.SendTextMessageWithInlineKeyboardAsync(chatId, preview, kb, ct).ConfigureAwait(false); } catch { }
        return true;
    }

    private async Task DoAddWallet(long chatId, long userId, string? lang, int? editMsgId, CancellationToken ct)
    {
        if (_cryptoRepo == null) return;
        var addr = await _stateStore.GetFlowDataAsync(userId, "cp_wallet_addr", ct).ConfigureAwait(false) ?? "";
        var net = await _stateStore.GetFlowDataAsync(userId, "cp_wallet_net", ct).ConfigureAwait(false) ?? "";
        var cur = await _stateStore.GetFlowDataAsync(userId, "cp_wallet_cur", ct).ConfigureAwait(false) ?? "USDT";
        await _stateStore.ClearStateAsync(userId, ct).ConfigureAwait(false);
        await _stateStore.ClearAllFlowDataAsync(userId, ct).ConfigureAwait(false);
        await SafeDelete(chatId, editMsgId, ct);

        await _cryptoRepo.CreateWalletAsync(new CryptoWalletDto(0, userId, cur, net, addr, null, default), ct).ConfigureAwait(false);
        var msg = L("<b>✅ کیف پول ذخیره شد</b>", "<b>✅ Wallet saved</b>", lang);
        var kb = new List<IReadOnlyList<InlineButton>> { new[] { new InlineButton(L("🔙 بازگشت", "🔙 Back", lang), "cp_wallets") } };
        try { await _sender.SendTextMessageWithInlineKeyboardAsync(chatId, msg, kb, ct).ConfigureAwait(false); } catch { }
    }

    private async Task DeleteWallet(long chatId, long userId, int walletId, string? lang, int? editMsgId, CancellationToken ct)
    {
        if (_cryptoRepo == null) return;
        await _cryptoRepo.DeleteWalletAsync(walletId, ct).ConfigureAwait(false);
        await ShowWallets(chatId, userId, lang, editMsgId, ct);
    }

    private async Task ShowPurchases(long chatId, long userId, string? lang, int page, int? editMsgId, CancellationToken ct)
    {
        if (_cryptoRepo == null) return;
        var purchases = await _cryptoRepo.ListPurchasesAsync(userId, page, 10, ct).ConfigureAwait(false);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(L("<b>📜 تاریخچه خریدها</b>", "<b>📜 Purchase History</b>", lang));
        sb.AppendLine("━━━━━━━━━━━━━━━━━━━\n");
        if (purchases.Count == 0) sb.AppendLine(L("📭 خریدی یافت نشد.", "📭 No purchases found.", lang));
        foreach (var p in purchases)
        {
            var statusIcon = p.Status switch { "completed" => "✅", "rejected" => "❌", _ => "🟡" };
            sb.AppendLine($"{statusIcon} {p.CurrencySymbol} — {p.Amount:N4} — {p.CreatedAt:yyyy/MM/dd}");
        }
        var kb = new List<IReadOnlyList<InlineButton>>();
        var nav = new List<InlineButton>();
        if (page > 0) nav.Add(new InlineButton("◀️", $"cp_purch_p:{page - 1}"));
        if (purchases.Count == 10) nav.Add(new InlineButton("▶️", $"cp_purch_p:{page + 1}"));
        if (nav.Count > 0) kb.Add(nav);
        kb.Add(new[] { new InlineButton(L("🔙 بازگشت", "🔙 Back", lang), "cp_menu") });

        if (editMsgId.HasValue) { try { await _sender.EditMessageTextWithInlineKeyboardAsync(chatId, editMsgId.Value, sb.ToString(), kb, ct).ConfigureAwait(false); return; } catch { } }
        try { await _sender.RemoveReplyKeyboardSilentAsync(chatId, ct).ConfigureAwait(false); } catch { }
        try { await _sender.SendTextMessageWithInlineKeyboardAsync(chatId, sb.ToString(), kb, ct).ConfigureAwait(false); } catch { }
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
