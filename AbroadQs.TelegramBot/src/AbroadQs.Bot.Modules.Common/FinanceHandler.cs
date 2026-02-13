using AbroadQs.Bot.Contracts;
using static AbroadQs.Bot.Contracts.BilingualHelper;

namespace AbroadQs.Bot.Modules.Common;

/// <summary>
/// Phase 2: Finance module — wallet balance, charge, transfer, history.
/// Callback prefixes: fin_
/// States: fin_charge_amount, fin_transfer_user, fin_transfer_amount, fin_transfer_confirm
/// </summary>
public sealed class FinanceHandler : IUpdateHandler
{
    private readonly IResponseSender _sender;
    private readonly ITelegramUserRepository _userRepo;
    private readonly IUserConversationStateStore _stateStore;
    private readonly IWalletRepository? _walletRepo;
    private readonly IPaymentGatewayService? _paymentGateway;
    private readonly IUserMessageStateRepository? _msgStateRepo;

    public FinanceHandler(
        IResponseSender sender, ITelegramUserRepository userRepo,
        IUserConversationStateStore stateStore,
        IWalletRepository? walletRepo = null, IPaymentGatewayService? paymentGateway = null,
        IUserMessageStateRepository? msgStateRepo = null)
    {
        _sender = sender; _userRepo = userRepo; _stateStore = stateStore;
        _walletRepo = walletRepo; _paymentGateway = paymentGateway; _msgStateRepo = msgStateRepo;
    }

    public string? Command => null;

    public bool CanHandle(BotUpdateContext context)
    {
        if (context.UserId == null) return false;
        if (context.IsCallbackQuery)
        {
            var cb = context.MessageText?.Trim() ?? "";
            return cb.StartsWith("fin_", StringComparison.Ordinal);
        }
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
            var editMsgId = context.CallbackMessageId;

            if (cb == "fin_menu") { await ShowFinanceMenu(chatId, userId, lang, editMsgId, ct); return true; }
            if (cb == "fin_balance") { await ShowBalance(chatId, userId, lang, editMsgId, ct); return true; }
            if (cb == "fin_charge") { await StartCharge(chatId, userId, lang, editMsgId, ct); return true; }
            if (cb == "fin_transfer") { await StartTransfer(chatId, userId, lang, editMsgId, ct); return true; }
            if (cb == "fin_history") { await ShowHistory(chatId, userId, lang, 0, editMsgId, ct); return true; }
            if (cb == "fin_payments") { await ShowPayments(chatId, userId, lang, 0, editMsgId, ct); return true; }
            if (cb.StartsWith("fin_hist_p:")) { int.TryParse(cb["fin_hist_p:".Length..], out var p); await ShowHistory(chatId, userId, lang, p, editMsgId, ct); return true; }
            if (cb.StartsWith("fin_pay_p:")) { int.TryParse(cb["fin_pay_p:".Length..], out var p); await ShowPayments(chatId, userId, lang, p, editMsgId, ct); return true; }
            if (cb == "fin_transfer_confirm") { await DoTransfer(chatId, userId, lang, editMsgId, ct); return true; }
            if (cb == "fin_transfer_cancel" || cb == "fin_charge_cancel")
            {
                await _stateStore.ClearStateAsync(userId, ct).ConfigureAwait(false);
                await _stateStore.ClearAllFlowDataAsync(userId, ct).ConfigureAwait(false);
                await ShowFinanceMenu(chatId, userId, lang, editMsgId, ct);
                return true;
            }
            return false;
        }

        // Text messages — only if user is in finance flow
        var state = await _stateStore.GetStateAsync(userId, ct).ConfigureAwait(false);
        if (state == null || !state.StartsWith("fin_")) return false;

        var text = context.MessageText?.Trim() ?? "";
        if (string.IsNullOrEmpty(text)) return false;

        return state switch
        {
            "fin_charge_amount" => await HandleChargeAmount(chatId, userId, text, lang, context.IncomingMessageId, ct),
            "fin_transfer_user" => await HandleTransferUser(chatId, userId, text, lang, context.IncomingMessageId, ct),
            "fin_transfer_amount" => await HandleTransferAmount(chatId, userId, text, lang, context.IncomingMessageId, ct),
            _ => false
        };
    }

    /// <summary>
    /// Shows a brief inline message with balance. Main menu buttons are now a reply keyboard (managed by DB seed).
    /// Back button returns to the reply-kb finance menu (stage:finance).
    /// </summary>
    public async Task ShowFinanceMenu(long chatId, long userId, string? lang, int? editMsgId, CancellationToken ct)
    {
        var user = await SafeGetUser(userId, ct);
        var balance = _walletRepo != null ? await _walletRepo.GetBalanceAsync(userId, ct).ConfigureAwait(false) : 0;
        var txCount = 0;
        try { if (_walletRepo != null) { var txs = await _walletRepo.GetTransactionsAsync(userId, 0, 1, ct).ConfigureAwait(false); txCount = txs.Count; } } catch { }

        var name = user != null ? $"{user.FirstName} {user.LastName}".Trim() : "---";
        var kycStatus = user?.KycStatus ?? "not_started";
        var kycIcon = kycStatus switch { "approved" => "✅", "pending" => "⏳", _ => "❌" };
        var kycLabel = L(
            kycStatus switch { "approved" => "تایید شده", "pending" => "در انتظار تایید", _ => "تایید نشده" },
            kycStatus switch { "approved" => "Verified", "pending" => "Pending", _ => "Not Verified" }, lang);

        var text = L(
            $"<b>💰 امور مالی</b>\n━━━━━━━━━━━━━━━━━━━\n\n" +
            $"👤 نام: <b>{Esc(name)}</b>\n" +
            $"🔐 احراز هویت: {kycIcon} {kycLabel}\n" +
            $"💳 موجودی: <b>{balance:N0}</b> تومان\n" +
            (txCount > 0 ? $"📊 تراکنش‌ها: {txCount}+\n" : "") +
            $"\n<i>از دکمه‌های زیر استفاده کنید:</i>",
            $"<b>💰 Finance</b>\n━━━━━━━━━━━━━━━━━━━\n\n" +
            $"👤 Name: <b>{Esc(name)}</b>\n" +
            $"🔐 Verification: {kycIcon} {kycLabel}\n" +
            $"💳 Balance: <b>{balance:N0}</b> Toman\n" +
            (txCount > 0 ? $"📊 Transactions: {txCount}+\n" : "") +
            $"\n<i>Use the buttons below:</i>", lang);

        var kb = new List<IReadOnlyList<InlineButton>>
        {
            new[] { new InlineButton(L("🔙 بازگشت", "🔙 Back", lang), "stage:finance") },
        };

        if (editMsgId.HasValue)
        { try { await _sender.EditMessageTextWithInlineKeyboardAsync(chatId, editMsgId.Value, text, kb, ct).ConfigureAwait(false); return; } catch { } }
        await SafeSendInline(chatId, text, kb, ct);
    }

    private static string Esc(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    public async Task HandleCallbackAction(long chatId, long userId, string action, int? editMsgId, CancellationToken ct)
    {
        var user = await SafeGetUser(userId, ct);
        var lang = user?.PreferredLanguage;
        switch (action)
        {
            case "fin_balance": await ShowBalance(chatId, userId, lang, editMsgId, ct); break;
            case "fin_charge": await StartCharge(chatId, userId, lang, editMsgId, ct); break;
            case "fin_transfer": await StartTransfer(chatId, userId, lang, editMsgId, ct); break;
            case "fin_history": await ShowHistory(chatId, userId, lang, 0, editMsgId, ct); break;
        }
    }

    private async Task ShowBalance(long chatId, long userId, string? lang, int? editMsgId, CancellationToken ct)
    {
        var balance = _walletRepo != null ? await _walletRepo.GetBalanceAsync(userId, ct).ConfigureAwait(false) : 0;
        var text = L($"<b>💳 موجودی کیف پول</b>\n━━━━━━━━━━━━━━━━━━━\n\n💰 موجودی: <b>{balance:N0}</b> تومان",
                     $"<b>💳 Wallet Balance</b>\n━━━━━━━━━━━━━━━━━━━\n\n💰 Balance: <b>{balance:N0}</b> Toman", lang);
        var kb = new List<IReadOnlyList<InlineButton>>
        {
            new[] { new InlineButton(L("💵 شارژ کیف پول", "💵 Charge Wallet", lang), "fin_charge") },
            new[] { new InlineButton(L("🔙 بازگشت", "🔙 Back", lang), "fin_menu") },
        };
        if (editMsgId.HasValue)
        { try { await _sender.EditMessageTextWithInlineKeyboardAsync(chatId, editMsgId.Value, text, kb, ct).ConfigureAwait(false); return; } catch { } }
        await SafeSendInline(chatId, text, kb, ct);
    }

    private async Task StartCharge(long chatId, long userId, string? lang, int? editMsgId, CancellationToken ct)
    {
        await SafeDelete(chatId, editMsgId, ct);
        await _stateStore.SetStateAsync(userId, "fin_charge_amount", ct).ConfigureAwait(false);
        var msg = L("<b>💵 شارژ کیف پول</b>\n━━━━━━━━━━━━━━━━━━━\n\nمبلغ مورد نظر (تومان) را وارد کنید:",
                    "<b>💵 Charge Wallet</b>\n━━━━━━━━━━━━━━━━━━━\n\nEnter the amount (Toman):", lang);
        var kb = new List<IReadOnlyList<string>> { new[] { "50,000", "100,000", "500,000" }, new[] { L("❌ انصراف", "❌ Cancel", lang) } };
        await SafeSendReplyKb(chatId, msg, kb, ct);
    }

    private async Task<bool> HandleChargeAmount(long chatId, long userId, string text, string? lang, int? userMsgId, CancellationToken ct)
    {
        if (text.Contains(L("انصراف", "Cancel", lang)))
        {
            await CleanAndCancel(chatId, userId, userMsgId, lang, ct); return true;
        }
        if (!decimal.TryParse(text.Replace(",", "").Replace("٫", ""), out var amount) || amount < 1000)
        {
            await SafeDelete(chatId, userMsgId, ct);
            var errMsg = L("⚠️ حداقل مبلغ شارژ ۱,۰۰۰ تومان است. مبلغ را دوباره وارد کنید:",
                           "⚠️ Minimum charge is 1,000 Toman. Please enter the amount again:", lang);
            await _sender.SendTextMessageAsync(chatId, errMsg, ct).ConfigureAwait(false);
            return true;
        }

        await SafeDelete(chatId, userMsgId, ct);
        await DeletePrevBotMsg(chatId, userId, ct);
        await _stateStore.ClearStateAsync(userId, ct).ConfigureAwait(false);
        await RemoveReplyKbSilent(chatId, ct);

        if (_paymentGateway != null && _walletRepo != null)
        {
            // Amount is in Toman; BitPay expects Rials (1 Toman = 10 Rial)
            var amountRials = (long)amount * 10L;
            var result = await _paymentGateway.CreatePaymentAsync(userId, amountRials, "wallet_charge", null, "/api/payment/callback", ct).ConfigureAwait(false);
            if (result.Success && !string.IsNullOrEmpty(result.PaymentUrl))
            {
                var msg = L($"<b>💳 پرداخت</b>\n━━━━━━━━━━━━━━━━━━━\n\n💰 مبلغ: <b>{amount:N0}</b> تومان\n\nلطفاً روی دکمه زیر کلیک کنید:",
                            $"<b>💳 Payment</b>\n━━━━━━━━━━━━━━━━━━━\n\n💰 Amount: <b>{amount:N0}</b> Toman\n\nClick the button below:", lang);
                var kb = new List<IReadOnlyList<InlineButton>>
                {
                    new[] { new InlineButton(L("💳 پرداخت آنلاین", "💳 Pay Online", lang), null, result.PaymentUrl) },
                    new[] { new InlineButton(L("🔙 بازگشت", "🔙 Back", lang), "fin_menu") },
                };
                await SafeSendInline(chatId, msg, kb, ct);
            }
            else
            {
                await _sender.SendTextMessageAsync(chatId, L("⚠️ خطا در ایجاد لینک پرداخت", "⚠️ Error creating payment link", lang), ct).ConfigureAwait(false);
            }
        }
        else
        {
            await _sender.SendTextMessageAsync(chatId, L("⚠️ درگاه پرداخت فعال نیست", "⚠️ Payment gateway not configured", lang), ct).ConfigureAwait(false);
        }
        return true;
    }

    private async Task StartTransfer(long chatId, long userId, string? lang, int? editMsgId, CancellationToken ct)
    {
        await SafeDelete(chatId, editMsgId, ct);
        await _stateStore.ClearAllFlowDataAsync(userId, ct).ConfigureAwait(false);
        await _stateStore.SetStateAsync(userId, "fin_transfer_user", ct).ConfigureAwait(false);
        var msg = L("<b>🔄 انتقال به کاربر دیگر</b>\n━━━━━━━━━━━━━━━━━━━\n\nشناسه عددی تلگرام یا یوزرنیم مقصد را وارد کنید:",
                    "<b>🔄 Transfer to User</b>\n━━━━━━━━━━━━━━━━━━━\n\nEnter the recipient Telegram ID or username:", lang);
        var kb = new List<IReadOnlyList<string>> { new[] { L("❌ انصراف", "❌ Cancel", lang) } };
        await SafeSendReplyKb(chatId, msg, kb, ct);
    }

    private async Task<bool> HandleTransferUser(long chatId, long userId, string text, string? lang, int? userMsgId, CancellationToken ct)
    {
        if (text.Contains(L("انصراف", "Cancel", lang))) { await CleanAndCancel(chatId, userId, userMsgId, lang, ct); return true; }
        await SafeDelete(chatId, userMsgId, ct);
        await DeletePrevBotMsg(chatId, userId, ct);
        await _stateStore.SetFlowDataAsync(userId, "fin_recipient", text.Trim().TrimStart('@'), ct).ConfigureAwait(false);
        await _stateStore.SetStateAsync(userId, "fin_transfer_amount", ct).ConfigureAwait(false);

        var balance = _walletRepo != null ? await _walletRepo.GetBalanceAsync(userId, ct).ConfigureAwait(false) : 0;
        var msg = L($"<b>🔄 مبلغ انتقال</b>\n━━━━━━━━━━━━━━━━━━━\n\n💳 موجودی: <b>{balance:N0}</b> تومان\n\nمبلغ (تومان) را وارد کنید:",
                    $"<b>🔄 Transfer Amount</b>\n━━━━━━━━━━━━━━━━━━━\n\n💳 Balance: <b>{balance:N0}</b> Toman\n\nEnter the amount (Toman):", lang);
        var kb = new List<IReadOnlyList<string>> { new[] { L("❌ انصراف", "❌ Cancel", lang) } };
        await SafeSendReplyKb(chatId, msg, kb, ct);
        return true;
    }

    private async Task<bool> HandleTransferAmount(long chatId, long userId, string text, string? lang, int? userMsgId, CancellationToken ct)
    {
        if (text.Contains(L("انصراف", "Cancel", lang))) { await CleanAndCancel(chatId, userId, userMsgId, lang, ct); return true; }
        if (!decimal.TryParse(text.Replace(",", "").Replace("٫", ""), out var amount) || amount <= 0)
        { await SafeDelete(chatId, userMsgId, ct); return true; }

        await SafeDelete(chatId, userMsgId, ct);
        await DeletePrevBotMsg(chatId, userId, ct);
        await _stateStore.SetFlowDataAsync(userId, "fin_amount", amount.ToString("F0"), ct).ConfigureAwait(false);
        await _stateStore.SetStateAsync(userId, "fin_transfer_confirm", ct).ConfigureAwait(false);
        await RemoveReplyKbSilent(chatId, ct);

        var recipient = await _stateStore.GetFlowDataAsync(userId, "fin_recipient", ct).ConfigureAwait(false) ?? "";
        var preview = L($"<b>📋 تأیید انتقال</b>\n━━━━━━━━━━━━━━━━━━━\n\n👤 مقصد: <b>{recipient}</b>\n💰 مبلغ: <b>{amount:N0}</b> تومان\n\nآیا مطمئن هستید؟",
                        $"<b>📋 Confirm Transfer</b>\n━━━━━━━━━━━━━━━━━━━\n\n👤 Recipient: <b>{recipient}</b>\n💰 Amount: <b>{amount:N0}</b> Toman\n\nAre you sure?", lang);
        var kb = new List<IReadOnlyList<InlineButton>>
        {
            new[] { new InlineButton(L("✅ تأیید", "✅ Confirm", lang), "fin_transfer_confirm") },
            new[] { new InlineButton(L("❌ انصراف", "❌ Cancel", lang), "fin_transfer_cancel") },
        };
        await SafeSendInline(chatId, preview, kb, ct);
        return true;
    }

    private async Task DoTransfer(long chatId, long userId, string? lang, int? editMsgId, CancellationToken ct)
    {
        var recipientStr = await _stateStore.GetFlowDataAsync(userId, "fin_recipient", ct).ConfigureAwait(false) ?? "";
        var amountStr = await _stateStore.GetFlowDataAsync(userId, "fin_amount", ct).ConfigureAwait(false) ?? "0";
        decimal.TryParse(amountStr, out var amount);

        await _stateStore.ClearStateAsync(userId, ct).ConfigureAwait(false);
        await _stateStore.ClearAllFlowDataAsync(userId, ct).ConfigureAwait(false);
        await SafeDelete(chatId, editMsgId, ct);

        if (_walletRepo == null) return;

        // Resolve recipient
        long recipientId = 0;
        if (long.TryParse(recipientStr, out var rid)) recipientId = rid;
        else
        {
            var recipientUser = await _userRepo.GetByTelegramUserIdAsync(0, ct).ConfigureAwait(false); // placeholder
            // Try to find by username — iterate all users (simple approach for now)
            var allUsers = await _userRepo.ListAllAsync(ct).ConfigureAwait(false);
            var found = allUsers.FirstOrDefault(u => string.Equals(u.Username, recipientStr, StringComparison.OrdinalIgnoreCase));
            if (found != null) recipientId = found.TelegramUserId;
        }

        if (recipientId == 0 || recipientId == userId)
        {
            await _sender.SendTextMessageAsync(chatId, L("⚠️ کاربر مقصد یافت نشد یا نامعتبر است.", "⚠️ Recipient not found or invalid.", lang), ct).ConfigureAwait(false);
            return;
        }

        var balance = await _walletRepo.GetBalanceAsync(userId, ct).ConfigureAwait(false);
        if (balance < amount)
        {
            await _sender.SendTextMessageAsync(chatId, L("⚠️ موجودی کافی نیست.", "⚠️ Insufficient balance.", lang), ct).ConfigureAwait(false);
            return;
        }

        await _walletRepo.DebitAsync(userId, amount, L($"انتقال به {recipientId}", $"Transfer to {recipientId}", lang), null, ct).ConfigureAwait(false);
        await _walletRepo.CreditAsync(recipientId, amount, L($"دریافت از {userId}", $"Received from {userId}", lang), null, ct).ConfigureAwait(false);

        var msg = L($"<b>✅ انتقال با موفقیت انجام شد</b>\n━━━━━━━━━━━━━━━━━━━\n\n💰 مبلغ: <b>{amount:N0}</b> تومان\n👤 مقصد: {recipientStr}",
                    $"<b>✅ Transfer Successful</b>\n━━━━━━━━━━━━━━━━━━━\n\n💰 Amount: <b>{amount:N0}</b> Toman\n👤 Recipient: {recipientStr}", lang);
        var kb = new List<IReadOnlyList<InlineButton>> { new[] { new InlineButton(L("🔙 بازگشت", "🔙 Back", lang), "fin_menu") } };
        await SafeSendInline(chatId, msg, kb, ct);

        // Notify recipient
        try { await _sender.SendTextMessageAsync(recipientId, L($"💰 مبلغ <b>{amount:N0}</b> تومان به کیف پول شما واریز شد.", $"💰 <b>{amount:N0}</b> Toman has been credited to your wallet.", lang), ct).ConfigureAwait(false); } catch { }
    }

    private async Task ShowHistory(long chatId, long userId, string? lang, int page, int? editMsgId, CancellationToken ct)
    {
        var txns = _walletRepo != null ? await _walletRepo.GetTransactionsAsync(userId, page, 10, ct).ConfigureAwait(false) : Array.Empty<WalletTransactionDto>();
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(L("<b>📜 تاریخچه تراکنش‌ها</b>", "<b>📜 Transaction History</b>", lang));
        sb.AppendLine("━━━━━━━━━━━━━━━━━━━\n");
        if (txns.Count == 0) sb.AppendLine(L("📭 تراکنشی یافت نشد.", "📭 No transactions found.", lang));
        foreach (var t in txns)
        {
            var icon = t.Amount >= 0 ? "🟢" : "🔴";
            sb.AppendLine($"{icon} <b>{t.Amount:N0}</b> — {t.Description ?? "-"}");
            sb.AppendLine($"   {t.CreatedAt:yyyy/MM/dd HH:mm}\n");
        }
        var kb = new List<IReadOnlyList<InlineButton>>();
        var nav = new List<InlineButton>();
        if (page > 0) nav.Add(new InlineButton("◀️", $"fin_hist_p:{page - 1}"));
        if (txns.Count == 10) nav.Add(new InlineButton("▶️", $"fin_hist_p:{page + 1}"));
        if (nav.Count > 0) kb.Add(nav);
        kb.Add(new[] { new InlineButton(L("🔙 بازگشت", "🔙 Back", lang), "fin_menu") });

        if (editMsgId.HasValue)
        { try { await _sender.EditMessageTextWithInlineKeyboardAsync(chatId, editMsgId.Value, sb.ToString(), kb, ct).ConfigureAwait(false); return; } catch { } }
        await SafeSendInline(chatId, sb.ToString(), kb, ct);
    }

    private async Task ShowPayments(long chatId, long userId, string? lang, int page, int? editMsgId, CancellationToken ct)
    {
        var payments = _walletRepo != null ? await _walletRepo.GetPaymentsAsync(userId, page, 10, ct).ConfigureAwait(false) : Array.Empty<PaymentDto>();
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(L("<b>💳 تاریخچه پرداخت‌ها</b>", "<b>💳 Payment History</b>", lang));
        sb.AppendLine("━━━━━━━━━━━━━━━━━━━\n");
        if (payments.Count == 0) sb.AppendLine(L("📭 پرداختی یافت نشد.", "📭 No payments found.", lang));
        foreach (var p in payments)
        {
            var statusIcon = p.Status == "success" ? "✅" : p.Status == "pending" ? "🟡" : "❌";
            sb.AppendLine($"{statusIcon} <b>{p.Amount:N0}</b> — {p.Purpose ?? "-"}");
            sb.AppendLine($"   {p.CreatedAt:yyyy/MM/dd HH:mm}\n");
        }
        var kb = new List<IReadOnlyList<InlineButton>>();
        var nav = new List<InlineButton>();
        if (page > 0) nav.Add(new InlineButton("◀️", $"fin_pay_p:{page - 1}"));
        if (payments.Count == 10) nav.Add(new InlineButton("▶️", $"fin_pay_p:{page + 1}"));
        if (nav.Count > 0) kb.Add(nav);
        kb.Add(new[] { new InlineButton(L("🔙 بازگشت", "🔙 Back", lang), "fin_menu") });

        if (editMsgId.HasValue)
        { try { await _sender.EditMessageTextWithInlineKeyboardAsync(chatId, editMsgId.Value, sb.ToString(), kb, ct).ConfigureAwait(false); return; } catch { } }
        await SafeSendInline(chatId, sb.ToString(), kb, ct);
    }

    // ── Helpers ──
    private async Task CleanAndCancel(long chatId, long userId, int? userMsgId, string? lang, CancellationToken ct)
    {
        await SafeDelete(chatId, userMsgId, ct);
        await DeletePrevBotMsg(chatId, userId, ct);
        await _stateStore.ClearStateAsync(userId, ct).ConfigureAwait(false);
        await _stateStore.ClearAllFlowDataAsync(userId, ct).ConfigureAwait(false);
        await RemoveReplyKbSilent(chatId, ct);
        await ShowFinanceMenu(chatId, userId, lang, null, ct);
    }

    private async Task<TelegramUserDto?> SafeGetUser(long userId, CancellationToken ct)
    { try { return await _userRepo.GetByTelegramUserIdAsync(userId, ct).ConfigureAwait(false); } catch { return null; } }
    private async Task SafeSendReplyKb(long chatId, string text, List<IReadOnlyList<string>> kb, CancellationToken ct)
    { try { await _sender.SendTextMessageWithReplyKeyboardAsync(chatId, text, kb, ct).ConfigureAwait(false); } catch { } }
    private async Task SafeSendInline(long chatId, string text, List<IReadOnlyList<InlineButton>> kb, CancellationToken ct)
    { try { await RemoveReplyKbSilent(chatId, ct); await _sender.SendTextMessageWithInlineKeyboardAsync(chatId, text, kb, ct).ConfigureAwait(false); } catch { } }
    private async Task SafeDelete(long chatId, int? msgId, CancellationToken ct)
    { if (msgId.HasValue) try { await _sender.DeleteMessageAsync(chatId, msgId.Value, ct).ConfigureAwait(false); } catch { } }
    private async Task SafeAnswerCallback(string? id, CancellationToken ct)
    { if (id != null) try { await _sender.AnswerCallbackQueryAsync(id, null, ct).ConfigureAwait(false); } catch { } }
    private async Task RemoveReplyKbSilent(long chatId, CancellationToken ct)
    { try { await _sender.RemoveReplyKeyboardSilentAsync(chatId, ct).ConfigureAwait(false); } catch { } }
    private async Task DeletePrevBotMsg(long chatId, long userId, CancellationToken ct)
    { if (_msgStateRepo == null) return; try { var s = await _msgStateRepo.GetUserMessageStateAsync(userId, ct).ConfigureAwait(false); if (s?.LastBotTelegramMessageId is > 0) await SafeDelete(chatId, (int)s.LastBotTelegramMessageId, ct); } catch { } }
}
