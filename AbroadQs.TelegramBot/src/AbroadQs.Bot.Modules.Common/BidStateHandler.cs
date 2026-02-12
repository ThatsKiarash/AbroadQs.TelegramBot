using AbroadQs.Bot.Contracts;

namespace AbroadQs.Bot.Modules.Common;

/// <summary>
/// Handles the bidding flow when a user wants to bid on a channel ad.
/// Entry: /start bid_{requestId}  or  callback bid_submit:{requestId}
/// Flow:  bid_amount → bid_rate → bid_message → bid_preview → bid_confirm
/// Also handles bid_accept/bid_reject callbacks from ad owners.
/// </summary>
public sealed class BidStateHandler : IUpdateHandler
{
    private readonly IResponseSender _sender;
    private readonly ITelegramUserRepository _userRepo;
    private readonly IUserConversationStateStore _stateStore;
    private readonly IExchangeRepository _exchangeRepo;
    private readonly IBidRepository _bidRepo;
    private readonly IUserMessageStateRepository? _msgStateRepo;
    private readonly ISettingsRepository? _settingsRepo;

    private const string BtnBack = "🔙 بازگشت";
    private const string BtnCancel = "❌ انصراف";
    private const string BtnSkipMsg = "بدون پیام";

    public BidStateHandler(
        IResponseSender sender,
        ITelegramUserRepository userRepo,
        IUserConversationStateStore stateStore,
        IExchangeRepository exchangeRepo,
        IBidRepository bidRepo,
        IUserMessageStateRepository? msgStateRepo = null,
        ISettingsRepository? settingsRepo = null)
    {
        _sender = sender;
        _userRepo = userRepo;
        _stateStore = stateStore;
        _exchangeRepo = exchangeRepo;
        _bidRepo = bidRepo;
        _msgStateRepo = msgStateRepo;
        _settingsRepo = settingsRepo;
    }

    public string? Command => null;

    public bool CanHandle(BotUpdateContext context)
    {
        if (context.UserId == null) return false;
        if (context.IsCallbackQuery)
        {
            var cb = context.MessageText?.Trim() ?? "";
            return cb.StartsWith("bid_accept:", StringComparison.Ordinal)
                || cb.StartsWith("bid_reject:", StringComparison.Ordinal)
                || cb.StartsWith("bid_view:", StringComparison.Ordinal)
                || cb == "bid_confirm"
                || cb == "bid_cancel";
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

            if (cb == "bid_cancel")
            {
                var st = await _stateStore.GetStateAsync(userId, ct).ConfigureAwait(false);
                if (st == null || !st.StartsWith("bid_")) return false;
                await _stateStore.ClearStateAsync(userId, ct).ConfigureAwait(false);
                await _stateStore.ClearAllFlowDataAsync(userId, ct).ConfigureAwait(false);
                await SafeDelete(chatId, context.CallbackMessageId, ct);
                await SafeSendInline(chatId, "❌ ارسال پیشنهاد لغو شد.",
                    new List<IReadOnlyList<InlineButton>> { new[] { new InlineButton("🏠 منوی اصلی", "stage:main_menu") } }, ct);
                return true;
            }

            if (cb == "bid_confirm")
            {
                var st = await _stateStore.GetStateAsync(userId, ct).ConfigureAwait(false);
                if (st != "bid_preview") return false;
                try { await DoSubmitBid(chatId, userId, context.CallbackMessageId, ct); }
                catch
                {
                    await _stateStore.ClearStateAsync(userId, ct).ConfigureAwait(false);
                    await _stateStore.ClearAllFlowDataAsync(userId, ct).ConfigureAwait(false);
                    await SafeSendInline(chatId, "⚠️ خطایی در ثبت پیشنهاد رخ داد. لطفاً دوباره تلاش کنید.",
                        new List<IReadOnlyList<InlineButton>> { new[] { new InlineButton("🏠 منوی اصلی", "stage:main_menu") } }, ct);
                }
                return true;
            }

            // Ad owner accepts a bid
            if (cb.StartsWith("bid_accept:"))
            {
                var bidIdStr = cb["bid_accept:".Length..];
                if (!int.TryParse(bidIdStr, out var bidId)) return false;
                await HandleBidAccept(chatId, userId, bidId, context.CallbackMessageId, ct);
                return true;
            }

            // Ad owner rejects a bid
            if (cb.StartsWith("bid_reject:"))
            {
                var bidIdStr = cb["bid_reject:".Length..];
                if (!int.TryParse(bidIdStr, out var bidId)) return false;
                await _bidRepo.UpdateBidStatusAsync(bidId, "rejected", ct).ConfigureAwait(false);
                await SafeDelete(chatId, context.CallbackMessageId, ct);
                await SafeSendInline(chatId, "✅ پیشنهاد رد شد.",
                    new List<IReadOnlyList<InlineButton>> { new[] { new InlineButton("🏠 منوی اصلی", "stage:main_menu") } }, ct);
                return true;
            }

            // View all bids for a request
            if (cb.StartsWith("bid_view:"))
            {
                var reqIdStr = cb["bid_view:".Length..];
                if (!int.TryParse(reqIdStr, out var reqId)) return false;
                await ShowBidsForRequest(chatId, userId, reqId, context.CallbackMessageId, ct);
                return true;
            }

            return false;
        }

        // ── Text messages — only if user is in bid flow ──
        var state = await _stateStore.GetStateAsync(userId, ct).ConfigureAwait(false);
        if (state == null || !state.StartsWith("bid_")) return false;

        var text = context.MessageText?.Trim() ?? "";
        if (string.IsNullOrEmpty(text)) return false;

        if (text == BtnCancel)
        {
            await CleanUserMsg(chatId, context.IncomingMessageId, ct);
            await DeletePrevBotMsg(chatId, userId, ct);
            await _stateStore.ClearStateAsync(userId, ct).ConfigureAwait(false);
            await _stateStore.ClearAllFlowDataAsync(userId, ct).ConfigureAwait(false);
            await RemoveReplyKbSilent(chatId, ct);
            await SafeSendInline(chatId, "❌ ارسال پیشنهاد لغو شد.",
                new List<IReadOnlyList<InlineButton>> { new[] { new InlineButton("🏠 منوی اصلی", "stage:main_menu") } }, ct);
            return true;
        }

        if (text == BtnBack)
        {
            await CleanUserMsg(chatId, context.IncomingMessageId, ct);
            await DeletePrevBotMsg(chatId, userId, ct);
            await GoBack(chatId, userId, state, ct);
            return true;
        }

        return state switch
        {
            "bid_amount" => await HandleBidAmountInput(chatId, userId, text, context.IncomingMessageId, ct),
            "bid_rate" => await HandleBidRateInput(chatId, userId, text, context.IncomingMessageId, ct),
            "bid_message" => await HandleBidMessageInput(chatId, userId, text, context.IncomingMessageId, ct),
            _ => false
        };
    }

    // ═══════════════════════════════════════════════════════════════
    //  Start bid flow — called from /start bid_{requestId}
    // ═══════════════════════════════════════════════════════════════

    public async Task StartBidFlow(long chatId, long userId, int requestId, CancellationToken ct)
    {
        var request = await _exchangeRepo.GetRequestAsync(requestId, ct).ConfigureAwait(false);
        if (request == null || request.Status != "approved")
        {
            await SafeSendInline(chatId, "⚠️ این آگهی دیگر فعال نیست.",
                new List<IReadOnlyList<InlineButton>> { new[] { new InlineButton("🏠 منوی اصلی", "stage:main_menu") } }, ct);
            return;
        }

        if (request.TelegramUserId == userId)
        {
            await SafeSendInline(chatId, "⚠️ نمی‌توانید برای آگهی خودتان پیشنهاد بدهید.",
                new List<IReadOnlyList<InlineButton>> { new[] { new InlineButton("🏠 منوی اصلی", "stage:main_menu") } }, ct);
            return;
        }

        await _stateStore.ClearAllFlowDataAsync(userId, ct).ConfigureAwait(false);
        await _stateStore.SetFlowDataAsync(userId, "bid_request_id", requestId.ToString(), ct).ConfigureAwait(false);

        var user = await SafeGetUser(userId, ct);
        var displayName = $"{user?.FirstName} {user?.LastName}".Trim();
        if (string.IsNullOrEmpty(displayName)) displayName = user?.Username ?? $"User_{userId}";
        await _stateStore.SetFlowDataAsync(userId, "bid_display_name", displayName, ct).ConfigureAwait(false);

        await ShowBidAmountStep(chatId, userId, request, ct);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Steps
    // ═══════════════════════════════════════════════════════════════

    private async Task ShowBidAmountStep(long chatId, long userId, ExchangeRequestDto request, CancellationToken ct)
    {
        await _stateStore.SetStateAsync(userId, "bid_amount", ct).ConfigureAwait(false);
        var flag = ExchangeStateHandler.GetCurrencyFlag(request.Currency);
        var currFa = ExchangeStateHandler.GetCurrencyNameFa(request.Currency);
        var txFa = request.TransactionType == "buy" ? "خرید" : request.TransactionType == "sell" ? "فروش" : "تبادل";

        var msg = $"<b>📩 ارسال پیشنهاد — مرحله ۱ از ۳</b>\n━━━━━━━━━━━━━━━━━━━\n\n" +
                  $"آگهی: #{request.RequestNumber} — {txFa} {flag} {request.Amount:N0} {currFa}\n" +
                  $"نرخ آگهی‌دهنده: <b>{request.ProposedRate:N0}</b> تومان\n\n" +
                  "مقدار ارز پیشنهادی خود را وارد کنید:";

        var kb = new List<IReadOnlyList<string>>
        {
            new[] { request.Amount.ToString("N0") }, // Suggest same amount
            new[] { BtnCancel },
        };
        await SafeSendReplyKb(chatId, msg, kb, ct);
    }

    private async Task<bool> HandleBidAmountInput(long chatId, long userId, string text, int? userMsgId, CancellationToken ct)
    {
        if (!decimal.TryParse(text.Replace(",", "").Replace("٫", ""), out var amount) || amount <= 0)
        { await CleanUserMsg(chatId, userMsgId, ct); return true; }

        await _stateStore.SetFlowDataAsync(userId, "bid_amount", amount.ToString("F0"), ct).ConfigureAwait(false);
        await CleanUserMsg(chatId, userMsgId, ct);
        await DeletePrevBotMsg(chatId, userId, ct);
        await ShowBidRateStep(chatId, userId, ct);
        return true;
    }

    private async Task ShowBidRateStep(long chatId, long userId, CancellationToken ct)
    {
        await _stateStore.SetStateAsync(userId, "bid_rate", ct).ConfigureAwait(false);
        var reqIdStr = await _stateStore.GetFlowDataAsync(userId, "bid_request_id", ct).ConfigureAwait(false) ?? "0";
        int.TryParse(reqIdStr, out var reqId);
        var request = await _exchangeRepo.GetRequestAsync(reqId, ct).ConfigureAwait(false);

        var msg = $"<b>📩 ارسال پیشنهاد — مرحله ۲ از ۳</b>\n━━━━━━━━━━━━━━━━━━━\n\n" +
                  $"نرخ آگهی‌دهنده: <b>{request?.ProposedRate:N0}</b> تومان\n\n" +
                  "نرخ پیشنهادی خود (تومان به ازای هر واحد) را وارد کنید:";

        var kb = new List<IReadOnlyList<string>>
        {
            new[] { request?.ProposedRate.ToString("N0") ?? "0" }, // Suggest ad rate
            new[] { BtnBack, BtnCancel },
        };
        await SafeSendReplyKb(chatId, msg, kb, ct);
    }

    private async Task<bool> HandleBidRateInput(long chatId, long userId, string text, int? userMsgId, CancellationToken ct)
    {
        if (!decimal.TryParse(text.Replace(",", "").Replace("٫", ""), out var rate) || rate <= 0)
        { await CleanUserMsg(chatId, userMsgId, ct); return true; }

        await _stateStore.SetFlowDataAsync(userId, "bid_rate", rate.ToString("F0"), ct).ConfigureAwait(false);
        await CleanUserMsg(chatId, userMsgId, ct);
        await DeletePrevBotMsg(chatId, userId, ct);
        await ShowBidMessageStep(chatId, userId, ct);
        return true;
    }

    private async Task ShowBidMessageStep(long chatId, long userId, CancellationToken ct)
    {
        await _stateStore.SetStateAsync(userId, "bid_message", ct).ConfigureAwait(false);

        var msg = "<b>📩 ارسال پیشنهاد — مرحله ۳ از ۳</b>\n━━━━━━━━━━━━━━━━━━━\n\n" +
                  "پیام اختیاری خود را بنویسید یا رد شوید:\n" +
                  "<i>مثلاً: آماده‌ام برای انجام فوری، شماره تماس: ...</i>";

        var kb = new List<IReadOnlyList<string>>
        {
            new[] { BtnSkipMsg },
            new[] { BtnBack, BtnCancel },
        };
        await SafeSendReplyKb(chatId, msg, kb, ct);
    }

    private async Task<bool> HandleBidMessageInput(long chatId, long userId, string text, int? userMsgId, CancellationToken ct)
    {
        var bidMsg = text == BtnSkipMsg ? "" : text;
        await _stateStore.SetFlowDataAsync(userId, "bid_message", bidMsg, ct).ConfigureAwait(false);
        await CleanUserMsg(chatId, userMsgId, ct);
        await DeletePrevBotMsg(chatId, userId, ct);
        await ShowBidPreview(chatId, userId, ct);
        return true;
    }

    private async Task ShowBidPreview(long chatId, long userId, CancellationToken ct)
    {
        await _stateStore.SetStateAsync(userId, "bid_preview", ct).ConfigureAwait(false);
        await RemoveReplyKbSilent(chatId, ct);

        var reqIdStr = await _stateStore.GetFlowDataAsync(userId, "bid_request_id", ct).ConfigureAwait(false) ?? "0";
        var amountStr = await _stateStore.GetFlowDataAsync(userId, "bid_amount", ct).ConfigureAwait(false) ?? "0";
        var rateStr = await _stateStore.GetFlowDataAsync(userId, "bid_rate", ct).ConfigureAwait(false) ?? "0";
        var bidMsg = await _stateStore.GetFlowDataAsync(userId, "bid_message", ct).ConfigureAwait(false) ?? "";
        int.TryParse(reqIdStr, out var reqId);
        decimal.TryParse(amountStr, out var amount);
        decimal.TryParse(rateStr, out var rate);

        var request = await _exchangeRepo.GetRequestAsync(reqId, ct).ConfigureAwait(false);
        var flag = ExchangeStateHandler.GetCurrencyFlag(request?.Currency ?? "");
        var currFa = ExchangeStateHandler.GetCurrencyNameFa(request?.Currency ?? "");

        var preview = $"<b>📋 پیش‌نمایش پیشنهاد</b>\n━━━━━━━━━━━━━━━━━━━\n\n" +
                      $"آگهی: #{request?.RequestNumber}\n" +
                      $"💰 مقدار: <b>{amount:N0}</b> {flag} {currFa}\n" +
                      $"💲 نرخ: <b>{rate:N0}</b> تومان\n" +
                      $"💵 مبلغ کل: <b>{amount * rate:N0}</b> تومان\n" +
                      (!string.IsNullOrEmpty(bidMsg) ? $"📝 پیام: {bidMsg}\n" : "") +
                      "\n<i>با زدن «تایید» پیشنهاد شما در کانال منتشر می‌شود.</i>";

        var kb = new List<IReadOnlyList<InlineButton>>
        {
            new[] { new InlineButton("✅ تایید و ارسال پیشنهاد", "bid_confirm") },
            new[] { new InlineButton("❌ انصراف", "bid_cancel") },
        };
        await SafeSendInline(chatId, preview, kb, ct);
    }

    private async Task DoSubmitBid(long chatId, long userId, int? triggerMsgId, CancellationToken ct)
    {
        var reqIdStr = await _stateStore.GetFlowDataAsync(userId, "bid_request_id", ct).ConfigureAwait(false) ?? "0";
        var amountStr = await _stateStore.GetFlowDataAsync(userId, "bid_amount", ct).ConfigureAwait(false) ?? "0";
        var rateStr = await _stateStore.GetFlowDataAsync(userId, "bid_rate", ct).ConfigureAwait(false) ?? "0";
        var bidMsg = await _stateStore.GetFlowDataAsync(userId, "bid_message", ct).ConfigureAwait(false) ?? "";
        var displayName = await _stateStore.GetFlowDataAsync(userId, "bid_display_name", ct).ConfigureAwait(false) ?? $"User_{userId}";
        int.TryParse(reqIdStr, out var reqId);
        decimal.TryParse(amountStr, out var amount);
        decimal.TryParse(rateStr, out var rate);

        var bidDto = new AdBidDto(
            Id: 0, ExchangeRequestId: reqId, BidderTelegramUserId: userId,
            BidderDisplayName: displayName, BidAmount: amount, BidRate: rate,
            Message: string.IsNullOrEmpty(bidMsg) ? null : bidMsg,
            Status: "pending", ChannelReplyMessageId: null, CreatedAt: DateTimeOffset.UtcNow);

        var savedBid = await _bidRepo.CreateBidAsync(bidDto, ct).ConfigureAwait(false);

        await _stateStore.ClearStateAsync(userId, ct).ConfigureAwait(false);
        await _stateStore.ClearAllFlowDataAsync(userId, ct).ConfigureAwait(false);
        await SafeDelete(chatId, triggerMsgId, ct);

        var request = await _exchangeRepo.GetRequestAsync(reqId, ct).ConfigureAwait(false);
        var flag = ExchangeStateHandler.GetCurrencyFlag(request?.Currency ?? "");
        var currFa = ExchangeStateHandler.GetCurrencyNameFa(request?.Currency ?? "");
        var bidCount = await _bidRepo.GetBidCountForRequestAsync(reqId, ct).ConfigureAwait(false);

        var msg = $"<b>✅ پیشنهاد شما ثبت شد</b>\n━━━━━━━━━━━━━━━━━━━\n\n" +
                  $"آگهی: #{request?.RequestNumber}\n" +
                  $"💰 مقدار: <b>{amount:N0}</b> {flag} {currFa}\n" +
                  $"💲 نرخ: <b>{rate:N0}</b> تومان\n" +
                  $"📊 تعداد پیشنهادات: {bidCount}\n\n" +
                  "پیشنهاد شما در کانال منتشر خواهد شد.\n" +
                  "اگر آگهی‌دهنده پیشنهاد شما را بپذیرد، از طریق ربات مطلع خواهید شد.";

        var kb = new List<IReadOnlyList<InlineButton>>
        {
            new[] { new InlineButton("🗑 حذف پیام", "exc_del_msg:0") },
            new[] { new InlineButton("🏠 منوی اصلی", "stage:main_menu") },
        };
        await SafeSendInline(chatId, msg, kb, ct);

        // Update channel post with bid count (Phase 1.1)
        await UpdateChannelPostBidCount(reqId, ct).ConfigureAwait(false);

        // Notify the ad owner about the new bid
        if (request != null)
        {
            var ownerMsg = $"<b>📩 پیشنهاد جدید برای آگهی #{request.RequestNumber}</b>\n━━━━━━━━━━━━━━━━━━━\n\n" +
                           $"👤 از: {displayName}\n" +
                           $"💰 مقدار: <b>{amount:N0}</b> {flag} {currFa}\n" +
                           $"💲 نرخ: <b>{rate:N0}</b> تومان\n" +
                           $"💵 مبلغ کل: <b>{amount * rate:N0}</b> تومان\n" +
                           (!string.IsNullOrEmpty(bidMsg) ? $"📝 پیام: {bidMsg}\n" : "") +
                           $"\n📊 کل پیشنهادات: {bidCount}";

            var ownerKb = new List<IReadOnlyList<InlineButton>>
            {
                new[] { new InlineButton("✅ پذیرش", $"bid_accept:{savedBid.Id}"), new InlineButton("❌ رد", $"bid_reject:{savedBid.Id}") },
                new[] { new InlineButton("📋 مشاهده همه پیشنهادات", $"bid_view:{reqId}") },
            };
            await SafeSendInline(request.TelegramUserId, ownerMsg, ownerKb, ct);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  Bid Accept
    // ═══════════════════════════════════════════════════════════════

    private async Task HandleBidAccept(long chatId, long userId, int bidId, int? triggerMsgId, CancellationToken ct)
    {
        var bid = await _bidRepo.GetBidAsync(bidId, ct).ConfigureAwait(false);
        if (bid == null) return;

        var request = await _exchangeRepo.GetRequestAsync(bid.ExchangeRequestId, ct).ConfigureAwait(false);
        if (request == null || request.TelegramUserId != userId) return;

        // Accept this bid, reject all others
        await _bidRepo.UpdateBidStatusAsync(bidId, "accepted", ct).ConfigureAwait(false);
        var allBids = await _bidRepo.GetBidsForRequestAsync(bid.ExchangeRequestId, ct).ConfigureAwait(false);
        foreach (var other in allBids.Where(b => b.Id != bidId && b.Status == "pending"))
            await _bidRepo.UpdateBidStatusAsync(other.Id, "rejected", ct).ConfigureAwait(false);

        // Update request status
        await _exchangeRepo.UpdateStatusAsync(bid.ExchangeRequestId, "matched", null, null, ct).ConfigureAwait(false);

        await SafeDelete(chatId, triggerMsgId, ct);

        var flag = ExchangeStateHandler.GetCurrencyFlag(request.Currency);
        var currFa = ExchangeStateHandler.GetCurrencyNameFa(request.Currency);

        // Notify ad owner
        var ownerMsg = $"<b>🤝 مچ شد!</b>\n━━━━━━━━━━━━━━━━━━━\n\n" +
                       $"آگهی: #{request.RequestNumber} — {flag} {request.Amount:N0} {currFa}\n\n" +
                       $"طرف مقابل: <b>{bid.BidderDisplayName}</b>\n" +
                       $"نرخ توافقی: <b>{bid.BidRate:N0}</b> تومان\n" +
                       $"مبلغ: <b>{bid.BidAmount * bid.BidRate:N0}</b> تومان\n\n" +
                       "پشتیبانی به زودی با شما تماس خواهد گرفت.";
        await SafeSendInline(chatId, ownerMsg,
            new List<IReadOnlyList<InlineButton>> { new[] { new InlineButton("🏠 منوی اصلی", "stage:main_menu") } }, ct);

        // Notify bidder
        var bidderMsg = $"<b>🎉 پیشنهاد شما پذیرفته شد!</b>\n━━━━━━━━━━━━━━━━━━━\n\n" +
                        $"آگهی: #{request.RequestNumber} — {flag} {request.Amount:N0} {currFa}\n\n" +
                        $"طرف مقابل: <b>{request.UserDisplayName}</b>\n" +
                        $"نرخ توافقی: <b>{bid.BidRate:N0}</b> تومان\n\n" +
                        "پشتیبانی به زودی با شما تماس خواهد گرفت.";
        await SafeSendInline(bid.BidderTelegramUserId, bidderMsg,
            new List<IReadOnlyList<InlineButton>> { new[] { new InlineButton("🏠 منوی اصلی", "stage:main_menu") } }, ct);

        // Notify rejected bidders
        foreach (var other in allBids.Where(b => b.Id != bidId && b.Status != "accepted"))
        {
            var rejectMsg = $"⚠️ متأسفانه پیشنهاد شما برای آگهی #{request.RequestNumber} پذیرفته نشد.\nآگهی‌دهنده پیشنهاد دیگری را انتخاب کرده است.";
            try { await _sender.SendTextMessageAsync(other.BidderTelegramUserId, rejectMsg, ct).ConfigureAwait(false); } catch { }
        }

        // Update channel post: remove bid button, mark as closed
        await UpdateChannelPostClosed(request, bid, ct);
    }

    /// <summary>
    /// Phase 1.1: Updates the channel post to show current bid count.
    /// </summary>
    private async Task UpdateChannelPostBidCount(int requestId, CancellationToken ct)
    {
        try
        {
            var request = await _exchangeRepo.GetRequestAsync(requestId, ct).ConfigureAwait(false);
            if (request?.ChannelMessageId == null || _settingsRepo == null) return;

            var channelId = await _settingsRepo.GetValueAsync("exchange_channel_id", ct).ConfigureAwait(false);
            if (string.IsNullOrEmpty(channelId) || !long.TryParse(channelId, out var chatId) || chatId == 0) return;

            var bidCount = await _bidRepo.GetBidCountForRequestAsync(requestId, ct).ConfigureAwait(false);
            var currFa = ExchangeStateHandler.GetCurrencyNameFa(request.Currency);
            var flag = ExchangeStateHandler.GetCurrencyFlag(request.Currency);
            var txFa = request.TransactionType == "buy" ? "خرید" : request.TransactionType == "sell" ? "فروش" : "تبادل";
            var roleFa = request.TransactionType == "buy" ? "خریدار" : request.TransactionType == "sell" ? "فروشنده" : "متقاضی تبادل";
            var deliveryFa = request.DeliveryMethod switch { "bank" => "حواله بانکی", "paypal" => "پی‌پال", "cash" => "اسکناس", _ => request.DeliveryMethod };

            var text = $"📢 <b>آگهی {txFa} ارز</b>\n\n" +
                $"💎 {roleFa}: <b>{request.UserDisplayName}</b>\n" +
                $"💰 مبلغ: <b>{request.Amount:N0}</b> {flag} {currFa}\n" +
                $"💲 نرخ پیشنهادی: <b>{request.ProposedRate:N0}</b> تومان\n" +
                $"🏦 نوع حواله: {deliveryFa}\n" +
                (!string.IsNullOrEmpty(request.Description) ? $"📝 توضیحات: {request.Description}\n" : "") +
                $"\n📊 پیشنهادات: {bidCount}";

            var botUsername = await _settingsRepo.GetValueAsync("bot_username", ct).ConfigureAwait(false) ?? "AbroadQsBot";
            var kb = new List<IReadOnlyList<InlineButton>>
            {
                new[] { new InlineButton($"📩 ارسال پیشنهاد ({bidCount})", null, $"https://t.me/{botUsername}?start=bid_{requestId}") },
            };

            await _sender.EditMessageTextWithInlineKeyboardAsync(chatId, request.ChannelMessageId.Value, text, kb, ct).ConfigureAwait(false);
        }
        catch { /* swallow channel edit failures */ }
    }

    /// <summary>
    /// Edits the channel post for a matched request: removes bid button, adds "closed" label.
    /// </summary>
    private async Task UpdateChannelPostClosed(ExchangeRequestDto request, AdBidDto acceptedBid, CancellationToken ct)
    {
        if (request.ChannelMessageId == null || _settingsRepo == null) return;
        try
        {
            var channelId = await _settingsRepo.GetValueAsync("exchange_channel_id", ct).ConfigureAwait(false);
            if (string.IsNullOrEmpty(channelId)) return;

            long chatId = 0;
            if (long.TryParse(channelId, out var cid)) chatId = cid;
            if (chatId == 0) return; // Can't edit via @username with IResponseSender

            var currFa = ExchangeStateHandler.GetCurrencyNameFa(request.Currency);
            var txFa = request.TransactionType == "buy" ? "خرید" : request.TransactionType == "sell" ? "فروش" : "تبادل";
            var roleFa = request.TransactionType == "buy" ? "خریدار" : request.TransactionType == "sell" ? "فروشنده" : "متقاضی تبادل";
            var deliveryFa = request.DeliveryMethod switch
            {
                "bank" => "حواله بانکی",
                "paypal" => "پی‌پال",
                "cash" => "اسکناس",
                _ => request.DeliveryMethod
            };

            var closedText = $"✅ <b>بسته شده — پیشنهاد پذیرفته شد</b>\n\n" +
                $"💎 {roleFa}: <b>{request.UserDisplayName}</b>\n" +
                $"💰 مبلغ: <b>{request.Amount:N0}</b> {currFa}\n" +
                $"💲 نرخ توافقی: <b>{acceptedBid.BidRate:N0}</b> تومان\n" +
                $"🏦 نوع حواله: {deliveryFa}\n\n" +
                "🔒 این آگهی بسته شده است.";

            // Edit with empty keyboard (removes the bid button)
            await _sender.EditMessageTextWithInlineKeyboardAsync(chatId, request.ChannelMessageId.Value, closedText,
                Array.Empty<IReadOnlyList<InlineButton>>(), ct).ConfigureAwait(false);
        }
        catch { /* swallow channel edit failures */ }
    }

    private async Task ShowBidsForRequest(long chatId, long userId, int requestId, int? editMsgId, CancellationToken ct)
    {
        var bids = await _bidRepo.GetBidsForRequestAsync(requestId, ct).ConfigureAwait(false);
        var request = await _exchangeRepo.GetRequestAsync(requestId, ct).ConfigureAwait(false);
        if (request == null || request.TelegramUserId != userId) return;

        var flag = ExchangeStateHandler.GetCurrencyFlag(request.Currency);
        var currFa = ExchangeStateHandler.GetCurrencyNameFa(request.Currency);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"<b>📋 پیشنهادات آگهی #{request.RequestNumber}</b>");
        sb.AppendLine("━━━━━━━━━━━━━━━━━━━\n");

        if (bids.Count == 0)
        {
            sb.AppendLine("📭 هنوز پیشنهادی ثبت نشده است.");
        }
        else
        {
            foreach (var b in bids)
            {
                var statusIcon = b.Status == "accepted" ? "✅" : b.Status == "rejected" ? "❌" : "🟡";
                sb.AppendLine($"{statusIcon} <b>{b.BidderDisplayName}</b>");
                sb.AppendLine($"   💰 {b.BidAmount:N0} {flag} — 💲 {b.BidRate:N0} T");
                if (!string.IsNullOrEmpty(b.Message)) sb.AppendLine($"   📝 {b.Message}");
                sb.AppendLine();
            }
        }

        var kb = new List<IReadOnlyList<InlineButton>>();
        // Add accept/reject buttons for pending bids
        foreach (var b in bids.Where(b => b.Status == "pending"))
        {
            kb.Add(new[]
            {
                new InlineButton($"✅ پذیرش {b.BidderDisplayName}", $"bid_accept:{b.Id}"),
                new InlineButton($"❌ رد", $"bid_reject:{b.Id}"),
            });
        }
        kb.Add(new[] { new InlineButton("🏠 منوی اصلی", "stage:main_menu") });

        if (editMsgId.HasValue)
        {
            try { await _sender.EditMessageTextWithInlineKeyboardAsync(chatId, editMsgId.Value, sb.ToString(), kb, ct).ConfigureAwait(false); return; }
            catch { }
        }
        await SafeSendInline(chatId, sb.ToString(), kb, ct);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Back navigation
    // ═══════════════════════════════════════════════════════════════

    private async Task GoBack(long chatId, long userId, string state, CancellationToken ct)
    {
        var reqIdStr = await _stateStore.GetFlowDataAsync(userId, "bid_request_id", ct).ConfigureAwait(false) ?? "0";
        int.TryParse(reqIdStr, out var reqId);
        var request = await _exchangeRepo.GetRequestAsync(reqId, ct).ConfigureAwait(false);

        switch (state)
        {
            case "bid_rate":
                if (request != null) await ShowBidAmountStep(chatId, userId, request, ct);
                break;
            case "bid_message":
                await ShowBidRateStep(chatId, userId, ct);
                break;
            default:
                if (request != null) await ShowBidAmountStep(chatId, userId, request, ct);
                break;
        }
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
    private async Task<TelegramUserDto?> SafeGetUser(long userId, CancellationToken ct)
    { try { return await _userRepo.GetByTelegramUserIdAsync(userId, ct).ConfigureAwait(false); } catch { return null; } }
    private async Task CleanUserMsg(long chatId, int? msgId, CancellationToken ct)
    { await SafeDelete(chatId, msgId, ct); }
    private async Task RemoveReplyKbSilent(long chatId, CancellationToken ct)
    { try { await _sender.RemoveReplyKeyboardSilentAsync(chatId, ct).ConfigureAwait(false); } catch { } }
    private async Task DeletePrevBotMsg(long chatId, long userId, CancellationToken ct)
    {
        if (_msgStateRepo == null) return;
        try { var s = await _msgStateRepo.GetUserMessageStateAsync(userId, ct).ConfigureAwait(false); if (s?.LastBotTelegramMessageId is > 0) await SafeDelete(chatId, (int)s.LastBotTelegramMessageId, ct); } catch { }
    }
}
