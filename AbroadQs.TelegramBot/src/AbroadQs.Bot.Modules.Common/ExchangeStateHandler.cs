using AbroadQs.Bot.Contracts;

namespace AbroadQs.Bot.Modules.Common;

/// <summary>
/// Multi-step exchange request flow — ALL steps use Reply Keyboard with back button.
/// Flow: currency → type → delivery → (bank: account → country) → amount → rate → description → preview → confirm.
/// Clean chat: deletes user messages and previous bot messages at each step.
/// </summary>
public sealed class ExchangeStateHandler : IUpdateHandler
{
    private readonly IResponseSender _sender;
    private readonly ITelegramUserRepository _userRepo;
    private readonly IUserConversationStateStore _stateStore;
    private readonly IExchangeRepository _exchangeRepo;
    private readonly ISettingsRepository? _settingsRepo;
    private readonly IUserMessageStateRepository? _msgStateRepo;

    private const string CbConfirm = "exc_confirm";
    private const string CbCancel = "exc_cancel";
    private const string BtnBack = "بازگشت";
    private const string BtnCancel = "انصراف";
    private const string BtnSkipDesc = "بدون توضیحات";
    private const string BtnMarketRate = "نرخ بازار";
    private const string BtnCustomRate = "نرخ دلخواه";

    // 8 currencies for reply keyboard (including TRY and AFN)
    private static readonly (string code, string flag, string nameFa)[] Currencies =
    {
        ("USD", "🇺🇸", "دلار"),
        ("EUR", "🇪🇺", "یورو"),
        ("GBP", "🇬🇧", "پوند"),
        ("CAD", "🇨🇦", "دلار کانادا"),
        ("AED", "🇦🇪", "درهم"),
        ("TRY", "🇹🇷", "لیر"),
        ("AFN", "🇦🇫", "افغانی"),
        ("USDT", "💲", "تتر"),
    };

    // 20 popular countries for reply keyboard
    private static readonly (string code, string flag, string name)[] Countries =
    {
        ("nl", "🇳🇱", "هلند"),     ("de", "🇩🇪", "آلمان"),     ("us", "🇺🇸", "آمریکا"),
        ("gb", "🇬🇧", "انگلیس"),   ("fr", "🇫🇷", "فرانسه"),    ("ca", "🇨🇦", "کانادا"),
        ("tr", "🇹🇷", "ترکیه"),    ("it", "🇮🇹", "ایتالیا"),   ("es", "🇪🇸", "اسپانیا"),
        ("se", "🇸🇪", "سوئد"),     ("no", "🇳🇴", "نروژ"),      ("ch", "🇨🇭", "سوئیس"),
        ("be", "🇧🇪", "بلژیک"),    ("dk", "🇩🇰", "دانمارک"),   ("fi", "🇫🇮", "فنلاند"),
        ("ie", "🇮🇪", "ایرلند"),   ("ir", "🇮🇷", "ایران"),     ("hu", "🇭🇺", "مجارستان"),
        ("ee", "🇪🇪", "استونی"),   ("lt", "🇱🇹", "لیتوانی"),
    };

    public ExchangeStateHandler(
        IResponseSender sender,
        ITelegramUserRepository userRepo,
        IUserConversationStateStore stateStore,
        IExchangeRepository exchangeRepo,
        ISettingsRepository? settingsRepo = null,
        IUserMessageStateRepository? msgStateRepo = null)
    {
        _sender = sender;
        _userRepo = userRepo;
        _stateStore = stateStore;
        _exchangeRepo = exchangeRepo;
        _settingsRepo = settingsRepo;
        _msgStateRepo = msgStateRepo;
    }

    public string? Command => null;

    public bool CanHandle(BotUpdateContext context)
    {
        if (context.UserId == null) return false;
        if (context.IsCallbackQuery)
        {
            var cb = context.MessageText?.Trim() ?? "";
            return cb == CbConfirm || cb == CbCancel
                || cb.StartsWith("exc_del_msg:", StringComparison.Ordinal);
        }
        return !string.IsNullOrEmpty(context.MessageText);
    }

    public async Task<bool> HandleAsync(BotUpdateContext context, CancellationToken ct)
    {
        if (context.UserId == null) return false;
        var userId = context.UserId.Value;
        var chatId = context.ChatId;

        // ── Callback queries (only confirm, cancel, delete msg) ──
        if (context.IsCallbackQuery)
        {
            var cb = context.MessageText?.Trim() ?? "";
            await SafeAnswerCallback(context.CallbackQueryId, null, ct);

            // Delete message and redirect to main menu
            if (cb.StartsWith("exc_del_msg:"))
            {
                await SafeDelete(chatId, context.CallbackMessageId, ct);
                await _stateStore.ClearStateAsync(userId, ct).ConfigureAwait(false);
                await _stateStore.SetReplyStageAsync(userId, "main_menu", ct).ConfigureAwait(false);
                // Show a small inline redirect to main menu
                await SafeSendInline(chatId, "پیام حذف شد.",
                    new List<IReadOnlyList<InlineButton>>
                    {
                        new[] { new InlineButton("بازگشت به منوی اصلی", "stage:main_menu") },
                    }, ct);
                return true;
            }

            if (cb == CbCancel)
            {
                var st = await _stateStore.GetStateAsync(userId, ct).ConfigureAwait(false);
                if (st == null || !st.StartsWith("exc_")) return false;
                await DoCancelAsync(chatId, userId, context.CallbackMessageId, ct);
                return true;
            }

            if (cb == CbConfirm)
            {
                var st = await _stateStore.GetStateAsync(userId, ct).ConfigureAwait(false);
                if (st != "exc_preview") return false;
                await DoConfirmAsync(chatId, userId, context.CallbackMessageId, ct);
                return true;
            }

            return false;
        }

        // ── Text messages — only process if user is in exchange flow ──
        var state = await _stateStore.GetStateAsync(userId, ct).ConfigureAwait(false);
        if (state == null || !state.StartsWith("exc_")) return false;

        var text = context.MessageText?.Trim() ?? "";
        if (string.IsNullOrEmpty(text)) return false;

        // Handle cancel button from any step
        if (text == BtnCancel)
        {
            await CleanUserMsg(chatId, context.IncomingMessageId, ct);
            await DeletePrevBotMsg(chatId, userId, ct);
            await DoCancelAsync(chatId, userId, null, ct);
            return true;
        }

        // Handle back button from any step
        if (text == BtnBack)
        {
            await CleanUserMsg(chatId, context.IncomingMessageId, ct);
            await DeletePrevBotMsg(chatId, userId, ct);
            await GoBackAsync(chatId, userId, state, ct);
            return true;
        }

        // ── Step handlers ──
        switch (state)
        {
            case "exc_currency": return await HandleCurrencyInput(chatId, userId, text, context.IncomingMessageId, ct);
            case "exc_type": return await HandleTypeInput(chatId, userId, text, context.IncomingMessageId, ct);
            case "exc_delivery": return await HandleDeliveryInput(chatId, userId, text, context.IncomingMessageId, ct);
            case "exc_account": return await HandleAccountInput(chatId, userId, text, context.IncomingMessageId, ct);
            case "exc_country": return await HandleCountryInput(chatId, userId, text, context.IncomingMessageId, ct);
            case "exc_amount": return await HandleAmountInput(chatId, userId, text, context.IncomingMessageId, ct);
            case "exc_rate": return await HandleRateInput(chatId, userId, text, context.IncomingMessageId, ct);
            case "exc_rate_custom": return await HandleRateCustomInput(chatId, userId, text, context.IncomingMessageId, ct);
            case "exc_desc": return await HandleDescInput(chatId, userId, text, context.IncomingMessageId, ct);
            default: return false;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  Back button logic
    // ═══════════════════════════════════════════════════════════════

    private async Task GoBackAsync(long chatId, long userId, string currentState, CancellationToken ct)
    {
        var delivery = await _stateStore.GetFlowDataAsync(userId, "delivery", ct).ConfigureAwait(false) ?? "";
        switch (currentState)
        {
            case "exc_type":
                await ShowCurrencyStep(chatId, userId, ct);
                break;
            case "exc_delivery":
                await ShowTypeStep(chatId, userId, ct);
                break;
            case "exc_account":
                await ShowDeliveryStep(chatId, userId, ct);
                break;
            case "exc_country":
                await ShowAccountStep(chatId, userId, ct);
                break;
            case "exc_amount":
                if (delivery == "bank")
                    await ShowCountryStep(chatId, userId, ct);
                else
                    await ShowDeliveryStep(chatId, userId, ct);
                break;
            case "exc_rate":
            case "exc_rate_custom":
                await ShowAmountStep(chatId, userId, ct);
                break;
            case "exc_desc":
                await ShowRateStep(chatId, userId, ct);
                break;
            case "exc_preview":
                await ShowDescStep(chatId, userId, ct);
                break;
            default:
                await ShowCurrencyStep(chatId, userId, ct);
                break;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  Start flow — called from DynamicStageHandler
    // ═══════════════════════════════════════════════════════════════

    public async Task StartExchangeFlow(long chatId, long userId, string txType, CancellationToken ct)
    {
        await _stateStore.ClearAllFlowDataAsync(userId, ct).ConfigureAwait(false);
        await _stateStore.SetFlowDataAsync(userId, "tx_type", txType, ct).ConfigureAwait(false);

        var user = await SafeGetUser(userId, ct);
        var displayName = $"{user?.FirstName} {user?.LastName}".Trim();
        if (string.IsNullOrEmpty(displayName)) displayName = user?.Username ?? $"User_{userId}";
        await _stateStore.SetFlowDataAsync(userId, "display_name", displayName, ct).ConfigureAwait(false);

        await ShowCurrencyStep(chatId, userId, ct);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Step 1: Currency — Reply Keyboard, 8 currencies
    // ═══════════════════════════════════════════════════════════════

    private async Task ShowCurrencyStep(long chatId, long userId, CancellationToken ct)
    {
        await _stateStore.SetStateAsync(userId, "exc_currency", ct).ConfigureAwait(false);

        var msg = "<b>انتخاب ارز</b>\n\nارز مورد نظر خود را انتخاب کنید:";

        var kb = new List<IReadOnlyList<string>>();
        // 4 per row
        for (int i = 0; i < Currencies.Length; i += 4)
        {
            var row = new List<string>();
            for (int j = i; j < Math.Min(i + 4, Currencies.Length); j++)
                row.Add($"{Currencies[j].flag} {Currencies[j].nameFa}");
            kb.Add(row);
        }
        kb.Add(new[] { BtnCancel });

        await SafeSendReplyKb(chatId, msg, kb, ct);
    }

    private async Task<bool> HandleCurrencyInput(long chatId, long userId, string text, int? userMsgId, CancellationToken ct)
    {
        var match = Currencies.FirstOrDefault(c => text.Contains(c.nameFa) || text.Contains(c.code, StringComparison.OrdinalIgnoreCase));
        if (match.code == null)
        {
            await CleanUserMsg(chatId, userMsgId, ct);
            return true;
        }

        await _stateStore.SetFlowDataAsync(userId, "currency", match.code, ct).ConfigureAwait(false);
        await CleanUserMsg(chatId, userMsgId, ct);
        await DeletePrevBotMsg(chatId, userId, ct);

        // If tx_type is already known (not "ask"), skip type step
        var existingType = await _stateStore.GetFlowDataAsync(userId, "tx_type", ct).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(existingType) && existingType != "ask")
            await ShowDeliveryStep(chatId, userId, ct);
        else
            await ShowTypeStep(chatId, userId, ct);
        return true;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Step 2: Transaction Type — Reply Keyboard
    // ═══════════════════════════════════════════════════════════════

    private async Task ShowTypeStep(long chatId, long userId, CancellationToken ct)
    {
        await _stateStore.SetStateAsync(userId, "exc_type", ct).ConfigureAwait(false);
        var currency = await _stateStore.GetFlowDataAsync(userId, "currency", ct).ConfigureAwait(false) ?? "";
        var flag = GetCurrencyFlag(currency);
        var currFa = GetCurrencyNameFa(currency);

        var msg = $"<b>نوع معامله</b>\n\n{flag} {currFa} — خرید یا فروش؟";

        var kb = new List<IReadOnlyList<string>>
        {
            new[] { "خرید", "فروش" },
            new[] { "تبادل" },
            new[] { BtnBack, BtnCancel },
        };

        await SafeSendReplyKb(chatId, msg, kb, ct);
    }

    private async Task<bool> HandleTypeInput(long chatId, long userId, string text, int? userMsgId, CancellationToken ct)
    {
        string? type = null;
        if (text.Contains("خرید")) type = "buy";
        else if (text.Contains("فروش")) type = "sell";
        else if (text.Contains("تبادل")) type = "exchange";

        if (type == null)
        {
            await CleanUserMsg(chatId, userMsgId, ct);
            return true;
        }

        await _stateStore.SetFlowDataAsync(userId, "tx_type", type, ct).ConfigureAwait(false);
        await CleanUserMsg(chatId, userMsgId, ct);
        await DeletePrevBotMsg(chatId, userId, ct);
        await ShowDeliveryStep(chatId, userId, ct);
        return true;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Step 3: Delivery Method — Reply Keyboard
    // ═══════════════════════════════════════════════════════════════

    private async Task ShowDeliveryStep(long chatId, long userId, CancellationToken ct)
    {
        await _stateStore.SetStateAsync(userId, "exc_delivery", ct).ConfigureAwait(false);

        var msg = "<b>روش تحویل</b>\n\nنحوه تحویل ارز را انتخاب کنید:";

        var kb = new List<IReadOnlyList<string>>
        {
            new[] { "حواله بانکی" },
            new[] { "پی‌پال", "اسکناس" },
            new[] { BtnBack, BtnCancel },
        };

        await SafeSendReplyKb(chatId, msg, kb, ct);
    }

    private async Task<bool> HandleDeliveryInput(long chatId, long userId, string text, int? userMsgId, CancellationToken ct)
    {
        string? method = null;
        if (text.Contains("بانکی") || text.Contains("حواله")) method = "bank";
        else if (text.Contains("پی‌پال") || text.Contains("پیپال")) method = "paypal";
        else if (text.Contains("اسکناس") || text.Contains("نقد")) method = "cash";

        if (method == null)
        {
            await CleanUserMsg(chatId, userMsgId, ct);
            return true;
        }

        await _stateStore.SetFlowDataAsync(userId, "delivery", method, ct).ConfigureAwait(false);
        await CleanUserMsg(chatId, userMsgId, ct);
        await DeletePrevBotMsg(chatId, userId, ct);

        if (method == "bank")
            await ShowAccountStep(chatId, userId, ct);
        else
            await ShowAmountStep(chatId, userId, ct);
        return true;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Step 4a: Account Type (bank only) — Reply Keyboard
    // ═══════════════════════════════════════════════════════════════

    private async Task ShowAccountStep(long chatId, long userId, CancellationToken ct)
    {
        await _stateStore.SetStateAsync(userId, "exc_account", ct).ConfigureAwait(false);

        var msg = "<b>نوع حساب</b>\n\nحساب مقصد شخصی است یا شرکتی؟";

        var kb = new List<IReadOnlyList<string>>
        {
            new[] { "شخصی", "شرکتی" },
            new[] { BtnBack, BtnCancel },
        };

        await SafeSendReplyKb(chatId, msg, kb, ct);
    }

    private async Task<bool> HandleAccountInput(long chatId, long userId, string text, int? userMsgId, CancellationToken ct)
    {
        string? accType = null;
        if (text.Contains("شخصی")) accType = "personal";
        else if (text.Contains("شرکتی")) accType = "company";

        if (accType == null)
        {
            await CleanUserMsg(chatId, userMsgId, ct);
            return true;
        }

        await _stateStore.SetFlowDataAsync(userId, "account_type", accType, ct).ConfigureAwait(false);
        await CleanUserMsg(chatId, userMsgId, ct);
        await DeletePrevBotMsg(chatId, userId, ct);
        await ShowCountryStep(chatId, userId, ct);
        return true;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Step 4b: Country (bank only) — Reply Keyboard, 20 countries
    // ═══════════════════════════════════════════════════════════════

    private async Task ShowCountryStep(long chatId, long userId, CancellationToken ct)
    {
        await _stateStore.SetStateAsync(userId, "exc_country", ct).ConfigureAwait(false);

        var msg = "<b>کشور مقصد</b>\n\nحساب بانکی در کدام کشور است؟";

        var kb = new List<IReadOnlyList<string>>();
        for (int i = 0; i < Countries.Length; i += 4)
        {
            var row = new List<string>();
            for (int j = i; j < Math.Min(i + 4, Countries.Length); j++)
                row.Add($"{Countries[j].flag} {Countries[j].name}");
            kb.Add(row);
        }
        kb.Add(new[] { "سایر" });
        kb.Add(new[] { BtnBack, BtnCancel });

        await SafeSendReplyKb(chatId, msg, kb, ct);
    }

    private async Task<bool> HandleCountryInput(long chatId, long userId, string text, int? userMsgId, CancellationToken ct)
    {
        string? countryName = null;

        if (text == "سایر") countryName = "سایر";
        else
        {
            var match = Countries.FirstOrDefault(c => text.Contains(c.name));
            if (match.code != null)
                countryName = match.name;
        }

        if (countryName == null)
        {
            await CleanUserMsg(chatId, userMsgId, ct);
            return true;
        }

        await _stateStore.SetFlowDataAsync(userId, "country", countryName, ct).ConfigureAwait(false);
        await CleanUserMsg(chatId, userMsgId, ct);
        await DeletePrevBotMsg(chatId, userId, ct);
        await ShowAmountStep(chatId, userId, ct);
        return true;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Step 5: Amount — Show current rate + Reply Keyboard presets
    // ═══════════════════════════════════════════════════════════════

    private async Task ShowAmountStep(long chatId, long userId, CancellationToken ct)
    {
        await _stateStore.SetStateAsync(userId, "exc_amount", ct).ConfigureAwait(false);
        var currency = await _stateStore.GetFlowDataAsync(userId, "currency", ct).ConfigureAwait(false) ?? "";
        var flag = GetCurrencyFlag(currency);
        var currFa = GetCurrencyNameFa(currency);

        // Show current rate
        var rateInfo = "";
        try
        {
            var cachedRate = await _exchangeRepo.GetRateAsync(currency, ct).ConfigureAwait(false);
            if (cachedRate != null && cachedRate.Rate > 0)
                rateInfo = $"\n\nنرخ لحظه‌ای {flag} {currFa}: <b>{cachedRate.Rate:N0}</b> تومان";
        }
        catch { }

        var msg = $"<b>مقدار ارز</b>\n\nچه مقدار {flag} {currFa} مد نظر دارید؟{rateInfo}\n\n" +
                  "یکی از مقادیر زیر را بزنید یا عدد دلخواه تایپ کنید:";

        var kb = new List<IReadOnlyList<string>>
        {
            new[] { "100", "200", "500" },
            new[] { "1,000", "2,000", "5,000" },
            new[] { "10,000", "50,000" },
            new[] { BtnBack, BtnCancel },
        };

        await SafeSendReplyKb(chatId, msg, kb, ct);
    }

    private async Task<bool> HandleAmountInput(long chatId, long userId, string text, int? userMsgId, CancellationToken ct)
    {
        if (!decimal.TryParse(text.Replace(",", "").Replace("٫", ""), out var amount) || amount <= 0)
        {
            await CleanUserMsg(chatId, userMsgId, ct);
            return true;
        }

        await _stateStore.SetFlowDataAsync(userId, "amount", amount.ToString("F0"), ct).ConfigureAwait(false);
        await CleanUserMsg(chatId, userMsgId, ct);
        await DeletePrevBotMsg(chatId, userId, ct);
        await ShowRateStep(chatId, userId, ct);
        return true;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Step 6: Rate — 2 buttons: market rate / custom rate
    // ═══════════════════════════════════════════════════════════════

    private async Task ShowRateStep(long chatId, long userId, CancellationToken ct)
    {
        await _stateStore.SetStateAsync(userId, "exc_rate", ct).ConfigureAwait(false);
        var currency = await _stateStore.GetFlowDataAsync(userId, "currency", ct).ConfigureAwait(false) ?? "";
        var flag = GetCurrencyFlag(currency);
        var currFa = GetCurrencyNameFa(currency);
        var amountStr = await _stateStore.GetFlowDataAsync(userId, "amount", ct).ConfigureAwait(false) ?? "0";
        decimal.TryParse(amountStr, out var amount);

        var msg = $"<b>نرخ پیشنهادی</b>\n\nنرخ مورد نظر خود را برای هر واحد {flag} {currFa} انتخاب کنید:";
        decimal marketRate = 0;

        try
        {
            var cachedRate = await _exchangeRepo.GetRateAsync(currency, ct).ConfigureAwait(false);
            if (cachedRate != null && cachedRate.Rate > 0)
            {
                marketRate = cachedRate.Rate;
                var min10 = Math.Round(marketRate * 0.90m, 0);
                var max10 = Math.Round(marketRate * 1.10m, 0);
                var total = amount * marketRate;

                msg = $"<b>نرخ پیشنهادی</b>\n\n" +
                      $"نرخ بازار {flag} {currFa}: <b>{marketRate:N0}</b> تومان\n" +
                      $"محدوده مجاز: {min10:N0} تا {max10:N0} تومان\n\n" +
                      $"{amount:N0} {flag} x {marketRate:N0} = <b>{total:N0}</b> تومان\n\n" +
                      "نرخ بازار را انتخاب کنید یا نرخ دلخواه وارد کنید:";

                // Store market rate for quick use
                await _stateStore.SetFlowDataAsync(userId, "market_rate", marketRate.ToString("F0"), ct).ConfigureAwait(false);
            }
        }
        catch { }

        var kb = new List<IReadOnlyList<string>>
        {
            new[] { BtnMarketRate, BtnCustomRate },
            new[] { BtnBack, BtnCancel },
        };

        await SafeSendReplyKb(chatId, msg, kb, ct);
    }

    private async Task<bool> HandleRateInput(long chatId, long userId, string text, int? userMsgId, CancellationToken ct)
    {
        // User chose "market rate"
        if (text == BtnMarketRate)
        {
            var mktStr = await _stateStore.GetFlowDataAsync(userId, "market_rate", ct).ConfigureAwait(false) ?? "";
            if (decimal.TryParse(mktStr, out var mktRate) && mktRate > 0)
            {
                await _stateStore.SetFlowDataAsync(userId, "rate", mktRate.ToString("F0"), ct).ConfigureAwait(false);
                await CleanUserMsg(chatId, userMsgId, ct);
                await DeletePrevBotMsg(chatId, userId, ct);
                await ShowDescStep(chatId, userId, ct);
                return true;
            }
            // No market rate available — fallback to custom
        }

        // User chose "custom rate" — show input prompt
        if (text == BtnCustomRate)
        {
            await CleanUserMsg(chatId, userMsgId, ct);
            await DeletePrevBotMsg(chatId, userId, ct);
            await ShowCustomRateInput(chatId, userId, ct);
            return true;
        }

        // If they typed a number directly, treat as custom rate
        if (decimal.TryParse(text.Replace(",", "").Replace("٫", ""), out var directRate) && directRate > 0)
        {
            return await ValidateAndSaveRate(chatId, userId, directRate, userMsgId, ct);
        }

        await CleanUserMsg(chatId, userMsgId, ct);
        return true;
    }

    private async Task ShowCustomRateInput(long chatId, long userId, CancellationToken ct)
    {
        await _stateStore.SetStateAsync(userId, "exc_rate_custom", ct).ConfigureAwait(false);
        var currency = await _stateStore.GetFlowDataAsync(userId, "currency", ct).ConfigureAwait(false) ?? "";
        var flag = GetCurrencyFlag(currency);
        var currFa = GetCurrencyNameFa(currency);

        var msg = $"<b>وارد کردن نرخ دلخواه</b>\n\nنرخ پیشنهادی خود را (تومان) برای هر واحد {flag} {currFa} تایپ کنید:";

        try
        {
            var cachedRate = await _exchangeRepo.GetRateAsync(currency, ct).ConfigureAwait(false);
            if (cachedRate != null && cachedRate.Rate > 0)
            {
                var min10 = Math.Round(cachedRate.Rate * 0.90m, 0);
                var max10 = Math.Round(cachedRate.Rate * 1.10m, 0);
                msg = $"<b>وارد کردن نرخ دلخواه</b>\n\n" +
                      $"نرخ بازار: <b>{cachedRate.Rate:N0}</b> تومان\n" +
                      $"محدوده مجاز: <b>{min10:N0}</b> تا <b>{max10:N0}</b> تومان\n\n" +
                      "نرخ مورد نظر خود را تایپ کنید:";
            }
        }
        catch { }

        var kb = new List<IReadOnlyList<string>>
        {
            new[] { BtnBack, BtnCancel },
        };

        await SafeSendReplyKb(chatId, msg, kb, ct);
    }

    private async Task<bool> HandleRateCustomInput(long chatId, long userId, string text, int? userMsgId, CancellationToken ct)
    {
        if (!decimal.TryParse(text.Replace(",", "").Replace("٫", ""), out var rate) || rate <= 0)
        {
            await CleanUserMsg(chatId, userMsgId, ct);
            return true;
        }

        return await ValidateAndSaveRate(chatId, userId, rate, userMsgId, ct);
    }

    private async Task<bool> ValidateAndSaveRate(long chatId, long userId, decimal rate, int? userMsgId, CancellationToken ct)
    {
        var currency = await _stateStore.GetFlowDataAsync(userId, "currency", ct).ConfigureAwait(false) ?? "";
        try
        {
            var cachedRate = await _exchangeRepo.GetRateAsync(currency, ct).ConfigureAwait(false);
            if (cachedRate != null && cachedRate.Rate > 0)
            {
                var min10 = Math.Round(cachedRate.Rate * 0.90m, 0);
                var max10 = Math.Round(cachedRate.Rate * 1.10m, 0);

                if (rate < min10 || rate > max10)
                {
                    await CleanUserMsg(chatId, userMsgId, ct);
                    await DeletePrevBotMsg(chatId, userId, ct);

                    var errMsg = $"<b>نرخ خارج از محدوده مجاز</b>\n\n" +
                                 $"نرخ شما: <b>{rate:N0}</b> تومان\n" +
                                 $"محدوده مجاز: <b>{min10:N0}</b> تا <b>{max10:N0}</b> تومان\n\n" +
                                 "لطفا نرخی در محدوده مجاز وارد کنید:";

                    var kb = new List<IReadOnlyList<string>>
                    {
                        new[] { BtnBack, BtnCancel },
                    };
                    // Stay in custom rate state
                    await _stateStore.SetStateAsync(userId, "exc_rate_custom", ct).ConfigureAwait(false);
                    await SafeSendReplyKb(chatId, errMsg, kb, ct);
                    return true;
                }
            }
        }
        catch { }

        await _stateStore.SetFlowDataAsync(userId, "rate", rate.ToString("F0"), ct).ConfigureAwait(false);
        await CleanUserMsg(chatId, userMsgId, ct);
        await DeletePrevBotMsg(chatId, userId, ct);
        await ShowDescStep(chatId, userId, ct);
        return true;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Step 7: Description (optional) — Reply Keyboard
    // ═══════════════════════════════════════════════════════════════

    private async Task ShowDescStep(long chatId, long userId, CancellationToken ct)
    {
        await _stateStore.SetStateAsync(userId, "exc_desc", ct).ConfigureAwait(false);

        var msg = "<b>توضیحات (اختیاری)</b>\n\n" +
                  "توضیحات اضافی خود را تایپ کنید یا رد کنید.";

        var kb = new List<IReadOnlyList<string>>
        {
            new[] { BtnSkipDesc },
            new[] { BtnBack, BtnCancel },
        };

        await SafeSendReplyKb(chatId, msg, kb, ct);
    }

    private async Task<bool> HandleDescInput(long chatId, long userId, string text, int? userMsgId, CancellationToken ct)
    {
        var desc = text == BtnSkipDesc ? "" : text;
        await _stateStore.SetFlowDataAsync(userId, "description", desc, ct).ConfigureAwait(false);
        await CleanUserMsg(chatId, userMsgId, ct);
        await DeletePrevBotMsg(chatId, userId, ct);
        await ShowPreviewStep(chatId, userId, ct);
        return true;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Step 8: Preview — Inline Keyboard for confirm/cancel
    // ═══════════════════════════════════════════════════════════════

    private async Task ShowPreviewStep(long chatId, long userId, CancellationToken ct)
    {
        await _stateStore.SetStateAsync(userId, "exc_preview", ct).ConfigureAwait(false);
        await RemoveReplyKbSilent(chatId, ct);

        var currency = await _stateStore.GetFlowDataAsync(userId, "currency", ct).ConfigureAwait(false) ?? "";
        var txType = await _stateStore.GetFlowDataAsync(userId, "tx_type", ct).ConfigureAwait(false) ?? "buy";
        var delivery = await _stateStore.GetFlowDataAsync(userId, "delivery", ct).ConfigureAwait(false) ?? "";
        var accountType = await _stateStore.GetFlowDataAsync(userId, "account_type", ct).ConfigureAwait(false);
        var country = await _stateStore.GetFlowDataAsync(userId, "country", ct).ConfigureAwait(false);
        var amountStr = await _stateStore.GetFlowDataAsync(userId, "amount", ct).ConfigureAwait(false) ?? "0";
        var rateStr = await _stateStore.GetFlowDataAsync(userId, "rate", ct).ConfigureAwait(false) ?? "0";
        var description = await _stateStore.GetFlowDataAsync(userId, "description", ct).ConfigureAwait(false);
        var displayName = await _stateStore.GetFlowDataAsync(userId, "display_name", ct).ConfigureAwait(false) ?? $"User_{userId}";

        decimal.TryParse(amountStr, out var amount);
        decimal.TryParse(rateStr, out var rate);

        decimal feePercent = 0;
        try
        {
            if (_settingsRepo != null)
            {
                var feeStr = await _settingsRepo.GetValueAsync("exchange_fee_percent", ct).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(feeStr)) decimal.TryParse(feeStr, out feePercent);
            }
        }
        catch { }

        var subtotal = amount * rate;
        var feeAmount = subtotal * feePercent / 100m;
        var totalAmount = txType == "buy" ? subtotal + feeAmount : subtotal - feeAmount;
        if (feePercent == 0) { feeAmount = 0; totalAmount = subtotal; }

        await _stateStore.SetFlowDataAsync(userId, "fee_percent", feePercent.ToString("F2"), ct).ConfigureAwait(false);
        await _stateStore.SetFlowDataAsync(userId, "fee_amount", feeAmount.ToString("F0"), ct).ConfigureAwait(false);
        await _stateStore.SetFlowDataAsync(userId, "total_amount", totalAmount.ToString("F0"), ct).ConfigureAwait(false);

        var flag = GetCurrencyFlag(currency);
        var currFa = GetCurrencyNameFa(currency);
        var txFa = txType == "buy" ? "خرید" : txType == "sell" ? "فروش" : "تبادل";

        var deliveryFa = delivery switch
        {
            "bank" => accountType == "company"
                ? $"حواله بانکی شرکتی{(country != null ? $" — {country}" : "")}"
                : $"حواله بانکی شخصی{(country != null ? $" — {country}" : "")}",
            "paypal" => "پی‌پال",
            "cash" => "اسکناس",
            _ => delivery
        };

        var marketComp = "";
        try
        {
            var cachedRate = await _exchangeRepo.GetRateAsync(currency, ct).ConfigureAwait(false);
            if (cachedRate != null && cachedRate.Rate > 0)
            {
                var diff = rate - cachedRate.Rate;
                var pct = diff / cachedRate.Rate * 100;
                var sign = diff >= 0 ? "+" : "";
                marketComp = $" ({sign}{pct:F1}%)";
            }
        }
        catch { }

        var preview = $"<b>پیش‌نمایش درخواست {txFa}</b>\n" +
                      "━━━━━━━━━━━━━━━━━━━\n\n" +
                      $"{displayName}\n" +
                      $"{flag} <b>{amount:N0}</b> {currFa}\n" +
                      $"نرخ: <b>{rate:N0}</b> تومان{marketComp}\n" +
                      $"تحویل: {deliveryFa}\n" +
                      (!string.IsNullOrEmpty(description) ? $"توضیحات: {description}\n" : "") +
                      "\n━━━━━━━━━━━━━━━━━━━\n" +
                      $"{amount:N0} x {rate:N0} = {subtotal:N0} تومان\n" +
                      (feePercent > 0
                          ? $"کارمزد ({feePercent:F1}%): {(txType == "buy" ? "+" : "-")}{feeAmount:N0} تومان\n"
                          : "") +
                      $"<b>مبلغ نهایی: {totalAmount:N0} تومان</b>\n\n" +
                      "<i>با تایید، درخواست جهت بررسی ارسال می‌شود.</i>";

        var inlineKb = new List<IReadOnlyList<InlineButton>>
        {
            new[] { new InlineButton("تایید و ارسال", CbConfirm) },
            new[] { new InlineButton("انصراف", CbCancel) },
        };

        await SafeSendInline(chatId, preview, inlineKb, ct);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Confirm: Save to DB + notify
    // ═══════════════════════════════════════════════════════════════

    private async Task DoConfirmAsync(long chatId, long userId, int? triggerMsgId, CancellationToken ct)
    {
        var currency = await _stateStore.GetFlowDataAsync(userId, "currency", ct).ConfigureAwait(false) ?? "";
        var txType = await _stateStore.GetFlowDataAsync(userId, "tx_type", ct).ConfigureAwait(false) ?? "buy";
        var delivery = await _stateStore.GetFlowDataAsync(userId, "delivery", ct).ConfigureAwait(false) ?? "";
        var accountType = await _stateStore.GetFlowDataAsync(userId, "account_type", ct).ConfigureAwait(false);
        var country = await _stateStore.GetFlowDataAsync(userId, "country", ct).ConfigureAwait(false);
        var amountStr = await _stateStore.GetFlowDataAsync(userId, "amount", ct).ConfigureAwait(false) ?? "0";
        var rateStr = await _stateStore.GetFlowDataAsync(userId, "rate", ct).ConfigureAwait(false) ?? "0";
        var description = await _stateStore.GetFlowDataAsync(userId, "description", ct).ConfigureAwait(false);
        var displayName = await _stateStore.GetFlowDataAsync(userId, "display_name", ct).ConfigureAwait(false) ?? $"User_{userId}";
        var feePercentStr = await _stateStore.GetFlowDataAsync(userId, "fee_percent", ct).ConfigureAwait(false) ?? "0";
        var feeAmountStr = await _stateStore.GetFlowDataAsync(userId, "fee_amount", ct).ConfigureAwait(false) ?? "0";
        var totalAmountStr = await _stateStore.GetFlowDataAsync(userId, "total_amount", ct).ConfigureAwait(false) ?? "0";

        decimal.TryParse(amountStr, out var amount);
        decimal.TryParse(rateStr, out var rate);
        decimal.TryParse(feePercentStr, out var feePercent);
        decimal.TryParse(feeAmountStr, out var feeAmount);
        decimal.TryParse(totalAmountStr, out var totalAmount);

        var requestNumber = await _exchangeRepo.GetNextRequestNumberAsync(ct).ConfigureAwait(false);

        var dto = new ExchangeRequestDto(
            Id: 0, RequestNumber: requestNumber, TelegramUserId: userId,
            Currency: currency, TransactionType: txType, DeliveryMethod: delivery,
            AccountType: accountType, Country: country, Amount: amount, ProposedRate: rate,
            Description: string.IsNullOrEmpty(description) ? null : description,
            FeePercent: feePercent, FeeAmount: feeAmount, TotalAmount: totalAmount,
            Status: "pending_approval", ChannelMessageId: null, AdminNote: null,
            UserDisplayName: displayName, CreatedAt: DateTimeOffset.UtcNow, UpdatedAt: null);

        await _exchangeRepo.CreateRequestAsync(dto, ct).ConfigureAwait(false);

        await _stateStore.ClearStateAsync(userId, ct).ConfigureAwait(false);
        await _stateStore.ClearAllFlowDataAsync(userId, ct).ConfigureAwait(false);
        await SafeDelete(chatId, triggerMsgId, ct);

        var flag = GetCurrencyFlag(currency);
        var currFa = GetCurrencyNameFa(currency);

        var msg = $"<b>درخواست ثبت شد</b>\n\n" +
                  $"شماره: #{requestNumber}\n" +
                  $"{flag} {amount:N0} {currFa} — {rate:N0} تومان\n" +
                  $"مبلغ نهایی: <b>{totalAmount:N0}</b> تومان\n\n" +
                  "در انتظار بررسی — نتیجه اطلاع داده می‌شود.";

        var kb = new List<IReadOnlyList<InlineButton>>
        {
            new[] { new InlineButton("حذف پیام", "exc_del_msg:0") },
            new[] { new InlineButton("بازگشت به منوی اصلی", "stage:main_menu") },
        };

        await SafeSendInline(chatId, msg, kb, ct);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Cancel
    // ═══════════════════════════════════════════════════════════════

    private async Task DoCancelAsync(long chatId, long userId, int? triggerMsgId, CancellationToken ct)
    {
        await _stateStore.ClearStateAsync(userId, ct).ConfigureAwait(false);
        await _stateStore.ClearAllFlowDataAsync(userId, ct).ConfigureAwait(false);
        await SafeDelete(chatId, triggerMsgId, ct);
        await RemoveReplyKbSilent(chatId, ct);

        await SafeSendInline(chatId, "درخواست لغو شد.",
            new List<IReadOnlyList<InlineButton>>
            {
                new[] { new InlineButton("حذف پیام", "exc_del_msg:0") },
                new[] { new InlineButton("بازگشت به منوی اصلی", "stage:main_menu") },
            }, ct);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Notification buttons — used from Program.cs
    // ═══════════════════════════════════════════════════════════════

    public static List<IReadOnlyList<InlineButton>> NotificationButtons(bool isFa, int? channelMsgId = null) => new()
    {
        new[] { new InlineButton(isFa ? "حذف پیام" : "Delete", "exc_del_msg:0") },
    };

    // ═══════════════════════════════════════════════════════════════
    //  Helpers
    // ═══════════════════════════════════════════════════════════════

    private async Task SafeSendReplyKb(long chatId, string text, List<IReadOnlyList<string>> kb, CancellationToken ct)
    { try { await _sender.SendTextMessageWithReplyKeyboardAsync(chatId, text, kb, ct).ConfigureAwait(false); } catch { } }

    private async Task SafeSendInline(long chatId, string text, List<IReadOnlyList<InlineButton>> kb, CancellationToken ct)
    { try { await _sender.SendTextMessageWithInlineKeyboardAsync(chatId, text, kb, ct).ConfigureAwait(false); } catch { } }

    private async Task SafeDelete(long chatId, int? msgId, CancellationToken ct)
    { if (msgId.HasValue) try { await _sender.DeleteMessageAsync(chatId, msgId.Value, ct).ConfigureAwait(false); } catch { } }

    private async Task SafeAnswerCallback(string? id, string? text, CancellationToken ct)
    { if (id != null) try { await _sender.AnswerCallbackQueryAsync(id, text, ct).ConfigureAwait(false); } catch { } }

    private async Task<TelegramUserDto?> SafeGetUser(long userId, CancellationToken ct)
    { try { return await _userRepo.GetByTelegramUserIdAsync(userId, ct).ConfigureAwait(false); } catch { return null; } }

    private async Task CleanUserMsg(long chatId, int? msgId, CancellationToken ct)
    { await SafeDelete(chatId, msgId, ct); }

    private async Task RemoveReplyKbSilent(long chatId, CancellationToken ct)
    { try { await _sender.RemoveReplyKeyboardSilentAsync(chatId, ct).ConfigureAwait(false); } catch { } }

    private async Task DeletePrevBotMsg(long chatId, long userId, CancellationToken ct)
    {
        if (_msgStateRepo == null) return;
        try
        {
            var s = await _msgStateRepo.GetUserMessageStateAsync(userId, ct).ConfigureAwait(false);
            if (s?.LastBotTelegramMessageId is > 0)
                await SafeDelete(chatId, (int)s.LastBotTelegramMessageId, ct);
        }
        catch { }
    }

    private static bool IsFa(TelegramUserDto? u) => (u?.PreferredLanguage ?? "fa") == "fa";

    // ═══════════════════════════════════════════════════════════════
    //  Currency/Country helpers (public for Program.cs)
    // ═══════════════════════════════════════════════════════════════

    public static string GetCurrencyFlag(string code) => code.ToUpperInvariant() switch
    {
        "USD" => "🇺🇸", "EUR" => "🇪🇺", "GBP" => "🇬🇧", "CAD" => "🇨🇦",
        "SEK" => "🇸🇪", "CHF" => "🇨🇭", "TRY" => "🇹🇷", "NOK" => "🇳🇴",
        "AUD" => "🇦🇺", "DKK" => "🇩🇰", "AED" => "🇦🇪", "INR" => "🇮🇳",
        "AFN" => "🇦🇫", "USDT" => "💲", _ => ""
    };

    public static string GetCurrencyNameFa(string code) => code.ToUpperInvariant() switch
    {
        "USD" => "دلار آمریکا", "EUR" => "یورو", "GBP" => "پوند انگلیس",
        "CAD" => "دلار کانادا", "SEK" => "کرون سوئد", "CHF" => "فرانک سوییس",
        "TRY" => "لیر ترکیه", "NOK" => "کرون نروژ", "AUD" => "دلار استرالیا",
        "DKK" => "کرون دانمارک", "AED" => "درهم امارات", "INR" => "روپیه هند",
        "AFN" => "افغانی", "USDT" => "تتر", _ => code
    };

    internal static string GetCurrencyNameEn(string code) => code.ToUpperInvariant() switch
    {
        "USD" => "US Dollar", "EUR" => "Euro", "GBP" => "British Pound",
        "CAD" => "Canadian Dollar", "SEK" => "Swedish Krona", "CHF" => "Swiss Franc",
        "TRY" => "Turkish Lira", "NOK" => "Norwegian Krone", "AUD" => "Australian Dollar",
        "DKK" => "Danish Krone", "AED" => "UAE Dirham", "INR" => "Indian Rupee",
        "AFN" => "Afghan Afghani", "USDT" => "Tether", _ => code
    };

    private static string GetCountryName(string code) => code switch
    {
        "nl" => "هلند", "de" => "آلمان", "us" => "آمریکا",
        "es" => "اسپانیا", "it" => "ایتالیا", "ir" => "ایران",
        "fr" => "فرانسه", "be" => "بلژیک", "lt" => "لیتوانی",
        "se" => "سوئد", "gb" => "انگلیس", "fi" => "فنلاند",
        "ie" => "ایرلند", "ca" => "کانادا", "no" => "نروژ",
        "hu" => "مجارستان", "ch" => "سوئیس", "ee" => "استونی",
        "dk" => "دانمارک", "tr" => "ترکیه", _ => code
    };
}
