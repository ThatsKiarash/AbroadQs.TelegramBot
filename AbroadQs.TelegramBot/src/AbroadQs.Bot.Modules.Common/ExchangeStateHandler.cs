using AbroadQs.Bot.Contracts;

namespace AbroadQs.Bot.Modules.Common;

/// <summary>
/// Multi-step exchange request flow with DIFFERENTIATED steps per transaction type.
///
/// Buy/Sell flow:  currency → amount → delivery → [delivery-specific] → rate → desc → preview → confirm
///   Bank:   account type → country → IBAN (opt) → bank name (opt)
///   PayPal: paypal email
///   Cash:   country → city → meeting preference
///
/// Exchange/Swap flow (in-person only):
///   source currency → dest currency → amount → source country → dest country → city → meeting → rate (ratio) → desc → preview → confirm
/// </summary>
public sealed class ExchangeStateHandler : IUpdateHandler
{
    private readonly IResponseSender _sender;
    private readonly ITelegramUserRepository _userRepo;
    private readonly IUserConversationStateStore _stateStore;
    private readonly IExchangeRepository _exchangeRepo;
    private readonly ISettingsRepository? _settingsRepo;
    private readonly IUserMessageStateRepository? _msgStateRepo;
    private readonly IBotStageRepository? _stageRepo;
    private readonly IPermissionRepository? _permRepo;
    private readonly IWalletRepository? _walletRepo;

    private const string CbConfirm = "exc_confirm";
    private const string CbCancel = "exc_cancel";
    private const string BtnBack = "🔙 بازگشت";
    private const string BtnCancel = "❌ انصراف";
    private const string BtnSkipDesc = "بدون توضیحات";
    private const string BtnMarketRate = "نرخ بازار";
    private const string BtnCustomRate = "نرخ دلخواه";
    private const string BtnSkipIban = "بدون IBAN";
    private const string BtnSkipBank = "بدون نام بانک";

    // 8 currencies for reply keyboard
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
        IUserMessageStateRepository? msgStateRepo = null,
        IBotStageRepository? stageRepo = null,
        IPermissionRepository? permRepo = null,
        IWalletRepository? walletRepo = null)
    {
        _sender = sender;
        _userRepo = userRepo;
        _stateStore = stateStore;
        _exchangeRepo = exchangeRepo;
        _settingsRepo = settingsRepo;
        _msgStateRepo = msgStateRepo;
        _stageRepo = stageRepo;
        _permRepo = permRepo;
        _walletRepo = walletRepo;
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

        // ── Callback queries ──
        if (context.IsCallbackQuery)
        {
            var cb = context.MessageText?.Trim() ?? "";
            await SafeAnswerCallback(context.CallbackQueryId, null, ct);

            // ── Stale inline cleanup: if user is on main_menu and not in exchange flow, delete old inline messages ──
            var currentState = await _stateStore.GetStateAsync(userId, ct).ConfigureAwait(false);
            var isInExchangeFlow = currentState != null && currentState.StartsWith("exc_");
            if (!isInExchangeFlow && (cb == CbConfirm || cb == CbCancel))
            {
                await SafeDelete(chatId, context.CallbackMessageId, ct);
                return true;
            }

            if (cb.StartsWith("exc_del_msg:"))
            {
                // Just delete the message and show main menu directly
                await SafeDelete(chatId, context.CallbackMessageId, ct);
                await _stateStore.ClearStateAsync(userId, ct).ConfigureAwait(false);
                await SendMainMenuAsync(chatId, userId, ct);
                return true;
            }

            if (cb == CbCancel)
            {
                var st = await _stateStore.GetStateAsync(userId, ct).ConfigureAwait(false);
                if (st == null || !st.StartsWith("exc_")) return false;
                try { await DoCancelAsync(chatId, userId, context.CallbackMessageId, ct); }
                catch
                {
                    await _stateStore.ClearStateAsync(userId, ct).ConfigureAwait(false);
                    await _stateStore.ClearAllFlowDataAsync(userId, ct).ConfigureAwait(false);
                    await _sender.SendTextMessageAsync(chatId, "⚠️ خطایی رخ داد. لطفاً دوباره تلاش کنید.", ct).ConfigureAwait(false);
                    await SendMainMenuAsync(chatId, userId, ct);
                }
                return true;
            }

            if (cb == CbConfirm)
            {
                var st = await _stateStore.GetStateAsync(userId, ct).ConfigureAwait(false);
                if (st != "exc_preview") return false;
                try { await DoConfirmAsync(chatId, userId, context.CallbackMessageId, ct); }
                catch
                {
                    await _stateStore.ClearStateAsync(userId, ct).ConfigureAwait(false);
                    await _stateStore.ClearAllFlowDataAsync(userId, ct).ConfigureAwait(false);
                    await _sender.SendTextMessageAsync(chatId, "⚠️ خطایی در ثبت درخواست رخ داد. لطفاً دوباره تلاش کنید.", ct).ConfigureAwait(false);
                    await SendMainMenuAsync(chatId, userId, ct);
                }
                return true;
            }

            return false;
        }

        // ── Text messages — only if user is in exchange flow ──
        var state = await _stateStore.GetStateAsync(userId, ct).ConfigureAwait(false);
        if (state == null || !state.StartsWith("exc_")) return false;

        var text = context.MessageText?.Trim() ?? "";
        if (string.IsNullOrEmpty(text)) return false;

        if (text == BtnCancel)
        {
            await CleanUserMsg(chatId, context.IncomingMessageId, ct);
            await DeletePrevBotMsg(chatId, userId, ct);
            await DoCancelAsync(chatId, userId, null, ct);
            return true;
        }

        if (text == BtnBack)
        {
            await CleanUserMsg(chatId, context.IncomingMessageId, ct);
            await DeletePrevBotMsg(chatId, userId, ct);
            await GoBackAsync(chatId, userId, state, ct);
            return true;
        }

        // ── Step handlers ──
        return state switch
        {
            "exc_currency" => await HandleCurrencyInput(chatId, userId, text, context.IncomingMessageId, ct),
            "exc_currency_dest" => await HandleCurrencyDestInput(chatId, userId, text, context.IncomingMessageId, ct),
            "exc_type" => await HandleTypeInput(chatId, userId, text, context.IncomingMessageId, ct),
            "exc_delivery" => await HandleDeliveryInput(chatId, userId, text, context.IncomingMessageId, ct),
            "exc_account" => await HandleAccountInput(chatId, userId, text, context.IncomingMessageId, ct),
            "exc_country" => await HandleCountryInput(chatId, userId, text, context.IncomingMessageId, ct),
            "exc_country_dest" => await HandleCountryDestInput(chatId, userId, text, context.IncomingMessageId, ct),
            "exc_city" => await HandleCityInput(chatId, userId, text, context.IncomingMessageId, ct),
            "exc_meeting" => await HandleMeetingInput(chatId, userId, text, context.IncomingMessageId, ct),
            "exc_paypal_email" => await HandlePaypalEmailInput(chatId, userId, text, context.IncomingMessageId, ct),
            "exc_iban" => await HandleIbanInput(chatId, userId, text, context.IncomingMessageId, ct),
            "exc_bank_name" => await HandleBankNameInput(chatId, userId, text, context.IncomingMessageId, ct),
            "exc_amount" => await HandleAmountInput(chatId, userId, text, context.IncomingMessageId, ct),
            "exc_rate" => await HandleRateInput(chatId, userId, text, context.IncomingMessageId, ct),
            "exc_rate_custom" => await HandleRateCustomInput(chatId, userId, text, context.IncomingMessageId, ct),
            "exc_desc" => await HandleDescInput(chatId, userId, text, context.IncomingMessageId, ct),
            _ => false
        };
    }

    // ═══════════════════════════════════════════════════════════════
    //  Dynamic step numbering
    // ═══════════════════════════════════════════════════════════════

    private async Task<(int current, int total)> GetStepInfo(long userId, string stepName, CancellationToken ct)
    {
        var txType = await _stateStore.GetFlowDataAsync(userId, "tx_type", ct).ConfigureAwait(false) ?? "buy";
        var delivery = await _stateStore.GetFlowDataAsync(userId, "delivery", ct).ConfigureAwait(false) ?? "";

        if (txType == "exchange")
        {
            // Exchange: src_currency → dest_currency → amount → src_country → dest_country → city → meeting → rate → desc → preview
            var steps = new[] { "exc_currency", "exc_currency_dest", "exc_amount", "exc_country", "exc_country_dest", "exc_city", "exc_meeting", "exc_rate", "exc_desc" };
            var idx = Array.IndexOf(steps, stepName);
            return (idx >= 0 ? idx + 1 : 1, steps.Length);
        }

        // Buy/Sell: currency → amount → delivery → [delivery-specific] → rate → desc → preview
        // Always compute based on chosen delivery; if not yet chosen, use bank (longest) so total doesn't jump.
        var effectiveDelivery = string.IsNullOrEmpty(delivery) ? "bank" : delivery;
        var buySellSteps = new List<string> { "exc_currency", "exc_amount", "exc_delivery" };
        if (effectiveDelivery == "bank")
        {
            buySellSteps.AddRange(new[] { "exc_account", "exc_country", "exc_iban", "exc_bank_name" });
        }
        else if (effectiveDelivery == "paypal")
        {
            buySellSteps.Add("exc_paypal_email");
        }
        else if (effectiveDelivery == "cash")
        {
            buySellSteps.AddRange(new[] { "exc_country", "exc_city", "exc_meeting" });
        }
        buySellSteps.AddRange(new[] { "exc_rate", "exc_desc" });

        var i = buySellSteps.IndexOf(stepName);
        return (i >= 0 ? i + 1 : 1, buySellSteps.Count);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Back button logic
    // ═══════════════════════════════════════════════════════════════

    private async Task GoBackAsync(long chatId, long userId, string currentState, CancellationToken ct)
    {
        var txType = await _stateStore.GetFlowDataAsync(userId, "tx_type", ct).ConfigureAwait(false) ?? "buy";
        var delivery = await _stateStore.GetFlowDataAsync(userId, "delivery", ct).ConfigureAwait(false) ?? "";

        if (txType == "exchange")
        {
            switch (currentState)
            {
                case "exc_currency_dest": await ShowCurrencyStep(chatId, userId, ct); break;
                case "exc_amount": await ShowCurrencyDestStep(chatId, userId, ct); break;
                case "exc_country": await ShowAmountStep(chatId, userId, ct); break;
                case "exc_country_dest": await ShowCountryStep(chatId, userId, ct); break;
                case "exc_city": await ShowCountryDestStep(chatId, userId, ct); break;
                case "exc_meeting": await ShowCityStep(chatId, userId, ct); break;
                case "exc_rate": case "exc_rate_custom": await ShowMeetingStep(chatId, userId, ct); break;
                case "exc_desc": await ShowRateStep(chatId, userId, ct); break;
                case "exc_preview": await ShowDescStep(chatId, userId, ct); break;
                default: await ShowCurrencyStep(chatId, userId, ct); break;
            }
            return;
        }

        // Buy/Sell back logic
        switch (currentState)
        {
            case "exc_type": await ShowCurrencyStep(chatId, userId, ct); break;
            case "exc_amount":
                // If tx_type was pre-set (not "ask"), go back to currency
                var existingType = await _stateStore.GetFlowDataAsync(userId, "tx_type", ct).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(existingType) && existingType != "ask")
                    await ShowCurrencyStep(chatId, userId, ct);
                else
                    await ShowTypeStep(chatId, userId, ct);
                break;
            case "exc_delivery": await ShowAmountStep(chatId, userId, ct); break;
            case "exc_account": await ShowDeliveryStep(chatId, userId, ct); break;
            case "exc_country":
                if (delivery == "bank") await ShowAccountStep(chatId, userId, ct);
                else await ShowDeliveryStep(chatId, userId, ct);
                break;
            case "exc_iban": await ShowCountryStep(chatId, userId, ct); break;
            case "exc_bank_name": await ShowIbanStep(chatId, userId, ct); break;
            case "exc_paypal_email": await ShowDeliveryStep(chatId, userId, ct); break;
            case "exc_city":
                if (delivery == "cash") await ShowCountryStep(chatId, userId, ct);
                else await ShowDeliveryStep(chatId, userId, ct);
                break;
            case "exc_meeting": await ShowCityStep(chatId, userId, ct); break;
            case "exc_rate": case "exc_rate_custom":
                if (delivery == "bank") await ShowBankNameStep(chatId, userId, ct);
                else if (delivery == "paypal") await ShowPaypalEmailStep(chatId, userId, ct);
                else if (delivery == "cash") await ShowMeetingStep(chatId, userId, ct);
                else await ShowDeliveryStep(chatId, userId, ct);
                break;
            case "exc_desc": await ShowRateStep(chatId, userId, ct); break;
            case "exc_preview": await ShowDescStep(chatId, userId, ct); break;
            default: await ShowCurrencyStep(chatId, userId, ct); break;
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
    //  STEP: Source Currency
    // ═══════════════════════════════════════════════════════════════

    private async Task ShowCurrencyStep(long chatId, long userId, CancellationToken ct)
    {
        await _stateStore.SetStateAsync(userId, "exc_currency", ct).ConfigureAwait(false);
        var txType = await _stateStore.GetFlowDataAsync(userId, "tx_type", ct).ConfigureAwait(false) ?? "buy";
        var (step, total) = await GetStepInfo(userId, "exc_currency", ct);
        var txLabel = TxLabel(txType);

        var header = txType == "exchange"
            ? $"<b>📌 مرحله {step} از {total} — ارز مبدأ</b>"
            : $"<b>📌 مرحله {step} از {total} — انتخاب ارز</b>";

        var msg = header + "\n━━━━━━━━━━━━━━━━━━━\n\n" +
                  (txType == "exchange"
                      ? "ارزی که می‌خواهید <b>بدهید</b> را انتخاب کنید:"
                      : $"شما در حال ثبت درخواست <b>{txLabel}</b> ارز هستید.\nارز مورد نظر خود را انتخاب کنید:");

        await SafeSendReplyKb(chatId, msg, BuildCurrencyKeyboard(), ct);
    }

    private async Task<bool> HandleCurrencyInput(long chatId, long userId, string text, int? userMsgId, CancellationToken ct)
    {
        var match = Currencies.FirstOrDefault(c => text.Contains(c.nameFa) || text.Contains(c.code, StringComparison.OrdinalIgnoreCase));
        if (match.code == null) { await CleanUserMsg(chatId, userMsgId, ct); return true; }

        await _stateStore.SetFlowDataAsync(userId, "currency", match.code, ct).ConfigureAwait(false);
        await CleanUserMsg(chatId, userMsgId, ct);
        await DeletePrevBotMsg(chatId, userId, ct);

        var txType = await _stateStore.GetFlowDataAsync(userId, "tx_type", ct).ConfigureAwait(false) ?? "buy";

        if (txType == "exchange")
        {
            await ShowCurrencyDestStep(chatId, userId, ct);
        }
        else
        {
            var existingType = await _stateStore.GetFlowDataAsync(userId, "tx_type", ct).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(existingType) && existingType != "ask")
                await ShowAmountStep(chatId, userId, ct);
            else
                await ShowTypeStep(chatId, userId, ct);
        }
        return true;
    }

    // ═══════════════════════════════════════════════════════════════
    //  STEP: Destination Currency (exchange only)
    // ═══════════════════════════════════════════════════════════════

    private async Task ShowCurrencyDestStep(long chatId, long userId, CancellationToken ct)
    {
        await _stateStore.SetStateAsync(userId, "exc_currency_dest", ct).ConfigureAwait(false);
        var srcCurrency = await _stateStore.GetFlowDataAsync(userId, "currency", ct).ConfigureAwait(false) ?? "";
        var (step, total) = await GetStepInfo(userId, "exc_currency_dest", ct);
        var srcFlag = GetCurrencyFlag(srcCurrency);
        var srcFa = GetCurrencyNameFa(srcCurrency);

        var msg = $"<b>📌 مرحله {step} از {total} — ارز مقصد</b>\n" +
                  "━━━━━━━━━━━━━━━━━━━\n\n" +
                  $"ارز مبدأ: {srcFlag} <b>{srcFa}</b>\n\n" +
                  "ارزی که می‌خواهید <b>دریافت کنید</b> را انتخاب کنید:";

        await SafeSendReplyKb(chatId, msg, BuildCurrencyKeyboard(), ct);
    }

    private async Task<bool> HandleCurrencyDestInput(long chatId, long userId, string text, int? userMsgId, CancellationToken ct)
    {
        var match = Currencies.FirstOrDefault(c => text.Contains(c.nameFa) || text.Contains(c.code, StringComparison.OrdinalIgnoreCase));
        if (match.code == null) { await CleanUserMsg(chatId, userMsgId, ct); return true; }

        var srcCurrency = await _stateStore.GetFlowDataAsync(userId, "currency", ct).ConfigureAwait(false) ?? "";
        if (string.Equals(match.code, srcCurrency, StringComparison.OrdinalIgnoreCase))
        {
            await CleanUserMsg(chatId, userMsgId, ct);
            return true; // Can't swap same currency
        }

        await _stateStore.SetFlowDataAsync(userId, "currency_dest", match.code, ct).ConfigureAwait(false);
        await CleanUserMsg(chatId, userMsgId, ct);
        await DeletePrevBotMsg(chatId, userId, ct);
        // Exchange: set delivery to "cash" (in-person only)
        await _stateStore.SetFlowDataAsync(userId, "delivery", "cash", ct).ConfigureAwait(false);
        await ShowAmountStep(chatId, userId, ct);
        return true;
    }

    // ═══════════════════════════════════════════════════════════════
    //  STEP: Transaction Type (buy/sell only, skipped if pre-set)
    // ═══════════════════════════════════════════════════════════════

    private async Task ShowTypeStep(long chatId, long userId, CancellationToken ct)
    {
        await _stateStore.SetStateAsync(userId, "exc_type", ct).ConfigureAwait(false);
        var currency = await _stateStore.GetFlowDataAsync(userId, "currency", ct).ConfigureAwait(false) ?? "";
        var flag = GetCurrencyFlag(currency);
        var currFa = GetCurrencyNameFa(currency);

        var msg = $"<b>📌 نوع معامله</b>\n" +
                  "━━━━━━━━━━━━━━━━━━━\n\n" +
                  $"ارز انتخابی: {flag} <b>{currFa}</b>\n\n" +
                  "• <b>خرید</b> — دریافت ارز و پرداخت تومان\n" +
                  "• <b>فروش</b> — ارائه ارز و دریافت تومان\n" +
                  "• <b>تبادل</b> — معاوضه ارز با کاربر دیگر";

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

        if (type == null) { await CleanUserMsg(chatId, userMsgId, ct); return true; }

        await _stateStore.SetFlowDataAsync(userId, "tx_type", type, ct).ConfigureAwait(false);
        await CleanUserMsg(chatId, userMsgId, ct);
        await DeletePrevBotMsg(chatId, userId, ct);

        if (type == "exchange")
        {
            await ShowCurrencyDestStep(chatId, userId, ct);
        }
        else
        {
            await ShowAmountStep(chatId, userId, ct);
        }
        return true;
    }

    // ═══════════════════════════════════════════════════════════════
    //  STEP: Amount
    // ═══════════════════════════════════════════════════════════════

    private async Task ShowAmountStep(long chatId, long userId, CancellationToken ct)
    {
        await _stateStore.SetStateAsync(userId, "exc_amount", ct).ConfigureAwait(false);
        var currency = await _stateStore.GetFlowDataAsync(userId, "currency", ct).ConfigureAwait(false) ?? "";
        var txType = await _stateStore.GetFlowDataAsync(userId, "tx_type", ct).ConfigureAwait(false) ?? "buy";
        var flag = GetCurrencyFlag(currency);
        var currFa = GetCurrencyNameFa(currency);
        var (step, total) = await GetStepInfo(userId, "exc_amount", ct);

        var rateInfo = "";
        try
        {
            var cachedRate = await _exchangeRepo.GetRateAsync(currency, ct).ConfigureAwait(false);
            if (cachedRate != null && cachedRate.Rate > 0)
                rateInfo = $"\nنرخ لحظه‌ای بازار: <b>{cachedRate.Rate:N0}</b> تومان\n";
        }
        catch { }

        string header;
        if (txType == "exchange")
        {
            var destCurr = await _stateStore.GetFlowDataAsync(userId, "currency_dest", ct).ConfigureAwait(false) ?? "";
            var destFlag = GetCurrencyFlag(destCurr);
            var destFa = GetCurrencyNameFa(destCurr);
            header = $"<b>📌 مرحله {step} از {total} — مقدار ارز</b>\n" +
                     "━━━━━━━━━━━━━━━━━━━\n\n" +
                     $"تبادل: {flag} {currFa} ➡️ {destFlag} {destFa}\n" + rateInfo;
        }
        else
        {
            var txFa = TxLabel(txType);
            header = $"<b>📌 مرحله {step} از {total} — مقدار ارز</b>\n" +
                     "━━━━━━━━━━━━━━━━━━━\n\n" +
                     $"{txFa} {flag} <b>{currFa}</b>\n" + rateInfo;
        }

        var msg = header + "\nچه مقدار ارز مد نظر دارید؟\n" +
                  $"<i>مقدار به واحد {currFa} وارد شود</i>";

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
        { await CleanUserMsg(chatId, userMsgId, ct); return true; }

        await _stateStore.SetFlowDataAsync(userId, "amount", amount.ToString("F0"), ct).ConfigureAwait(false);
        await CleanUserMsg(chatId, userMsgId, ct);
        await DeletePrevBotMsg(chatId, userId, ct);

        var txType = await _stateStore.GetFlowDataAsync(userId, "tx_type", ct).ConfigureAwait(false) ?? "buy";
        if (txType == "exchange")
            await ShowCountryStep(chatId, userId, ct); // Exchange: source country
        else
            await ShowDeliveryStep(chatId, userId, ct);
        return true;
    }

    // ═══════════════════════════════════════════════════════════════
    //  STEP: Delivery Method (buy/sell only)
    // ═══════════════════════════════════════════════════════════════

    private async Task ShowDeliveryStep(long chatId, long userId, CancellationToken ct)
    {
        await _stateStore.SetStateAsync(userId, "exc_delivery", ct).ConfigureAwait(false);
        var (step, total) = await GetStepInfo(userId, "exc_delivery", ct);
        var currency = await _stateStore.GetFlowDataAsync(userId, "currency", ct).ConfigureAwait(false) ?? "";
        var txType = await _stateStore.GetFlowDataAsync(userId, "tx_type", ct).ConfigureAwait(false) ?? "buy";
        var flag = GetCurrencyFlag(currency);
        var currFa = GetCurrencyNameFa(currency);
        var txFa = TxLabel(txType);

        var msg = $"<b>📌 مرحله {step} از {total} — روش تحویل</b>\n" +
                  "━━━━━━━━━━━━━━━━━━━\n\n" +
                  $"{txFa} {flag} {currFa}\n\n" +
                  "• <b>حواله بانکی</b> — انتقال SWIFT/SEPA\n" +
                  "• <b>پی‌پال</b> — انتقال PayPal\n" +
                  "• <b>اسکناس</b> — تحویل حضوری نقدی";

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

        if (method == null) { await CleanUserMsg(chatId, userMsgId, ct); return true; }

        await _stateStore.SetFlowDataAsync(userId, "delivery", method, ct).ConfigureAwait(false);
        await CleanUserMsg(chatId, userMsgId, ct);
        await DeletePrevBotMsg(chatId, userId, ct);

        // Route to delivery-specific steps
        switch (method)
        {
            case "bank": await ShowAccountStep(chatId, userId, ct); break;
            case "paypal": await ShowPaypalEmailStep(chatId, userId, ct); break;
            case "cash": await ShowCountryStep(chatId, userId, ct); break;
        }
        return true;
    }

    // ═══════════════════════════════════════════════════════════════
    //  STEP: Account Type (bank only)
    // ═══════════════════════════════════════════════════════════════

    private async Task ShowAccountStep(long chatId, long userId, CancellationToken ct)
    {
        await _stateStore.SetStateAsync(userId, "exc_account", ct).ConfigureAwait(false);
        var (step, total) = await GetStepInfo(userId, "exc_account", ct);

        var msg = $"<b>📌 مرحله {step} از {total} — نوع حساب بانکی</b>\n" +
                  "━━━━━━━━━━━━━━━━━━━\n\n" +
                  "• <b>شخصی</b> — حساب به نام شخص حقیقی\n" +
                  "• <b>شرکتی</b> — حساب به نام شرکت یا مؤسسه";

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

        if (accType == null) { await CleanUserMsg(chatId, userMsgId, ct); return true; }

        await _stateStore.SetFlowDataAsync(userId, "account_type", accType, ct).ConfigureAwait(false);
        await CleanUserMsg(chatId, userMsgId, ct);
        await DeletePrevBotMsg(chatId, userId, ct);
        await ShowCountryStep(chatId, userId, ct);
        return true;
    }

    // ═══════════════════════════════════════════════════════════════
    //  STEP: Country (bank/cash/exchange)
    // ═══════════════════════════════════════════════════════════════

    private async Task ShowCountryStep(long chatId, long userId, CancellationToken ct)
    {
        await _stateStore.SetStateAsync(userId, "exc_country", ct).ConfigureAwait(false);
        var txType = await _stateStore.GetFlowDataAsync(userId, "tx_type", ct).ConfigureAwait(false) ?? "buy";
        var delivery = await _stateStore.GetFlowDataAsync(userId, "delivery", ct).ConfigureAwait(false) ?? "";
        var (step, total) = await GetStepInfo(userId, "exc_country", ct);

        string label;
        if (txType == "exchange")
            label = "کشور مبدأ (محل تحویل ارز مبدأ):";
        else if (delivery == "bank")
            label = txType == "buy" ? "حساب بانکی مقصد در کدام کشور است؟" : "حساب بانکی مبدأ در کدام کشور است؟";
        else
            label = "تحویل حضوری در کدام کشور انجام می‌شود؟";

        var msg = $"<b>📌 مرحله {step} از {total} — کشور</b>\n" +
                  "━━━━━━━━━━━━━━━━━━━\n\n" + label;

        await SafeSendReplyKb(chatId, msg, BuildCountryKeyboard(), ct);
    }

    private async Task<bool> HandleCountryInput(long chatId, long userId, string text, int? userMsgId, CancellationToken ct)
    {
        string? countryName = text == "سایر" ? "سایر" : Countries.FirstOrDefault(c => text.Contains(c.name)).name;
        if (countryName == null) { await CleanUserMsg(chatId, userMsgId, ct); return true; }

        await _stateStore.SetFlowDataAsync(userId, "country", countryName, ct).ConfigureAwait(false);
        await CleanUserMsg(chatId, userMsgId, ct);
        await DeletePrevBotMsg(chatId, userId, ct);

        var txType = await _stateStore.GetFlowDataAsync(userId, "tx_type", ct).ConfigureAwait(false) ?? "buy";
        var delivery = await _stateStore.GetFlowDataAsync(userId, "delivery", ct).ConfigureAwait(false) ?? "";

        if (txType == "exchange")
            await ShowCountryDestStep(chatId, userId, ct);
        else if (delivery == "bank")
            await ShowIbanStep(chatId, userId, ct);
        else // cash
            await ShowCityStep(chatId, userId, ct);
        return true;
    }

    // ═══════════════════════════════════════════════════════════════
    //  STEP: Destination Country (exchange only)
    // ═══════════════════════════════════════════════════════════════

    private async Task ShowCountryDestStep(long chatId, long userId, CancellationToken ct)
    {
        await _stateStore.SetStateAsync(userId, "exc_country_dest", ct).ConfigureAwait(false);
        var (step, total) = await GetStepInfo(userId, "exc_country_dest", ct);

        var msg = $"<b>📌 مرحله {step} از {total} — کشور مقصد</b>\n" +
                  "━━━━━━━━━━━━━━━━━━━\n\n" +
                  "محل دریافت ارز مقصد در کدام کشور است؟";

        await SafeSendReplyKb(chatId, msg, BuildCountryKeyboard(), ct);
    }

    private async Task<bool> HandleCountryDestInput(long chatId, long userId, string text, int? userMsgId, CancellationToken ct)
    {
        string? countryName = text == "سایر" ? "سایر" : Countries.FirstOrDefault(c => text.Contains(c.name)).name;
        if (countryName == null) { await CleanUserMsg(chatId, userMsgId, ct); return true; }

        await _stateStore.SetFlowDataAsync(userId, "country_dest", countryName, ct).ConfigureAwait(false);
        await CleanUserMsg(chatId, userMsgId, ct);
        await DeletePrevBotMsg(chatId, userId, ct);
        await ShowCityStep(chatId, userId, ct);
        return true;
    }

    // ═══════════════════════════════════════════════════════════════
    //  STEP: City (cash/exchange)
    // ═══════════════════════════════════════════════════════════════

    private async Task ShowCityStep(long chatId, long userId, CancellationToken ct)
    {
        await _stateStore.SetStateAsync(userId, "exc_city", ct).ConfigureAwait(false);
        var (step, total) = await GetStepInfo(userId, "exc_city", ct);

        var msg = $"<b>📌 مرحله {step} از {total} — شهر</b>\n" +
                  "━━━━━━━━━━━━━━━━━━━\n\n" +
                  "نام شهر محل ملاقات حضوری را تایپ کنید:\n" +
                  "<i>مثال: آمستردام، برلین، استانبول</i>";

        var kb = new List<IReadOnlyList<string>> { new[] { BtnBack, BtnCancel } };
        await SafeSendReplyKb(chatId, msg, kb, ct);
    }

    private async Task<bool> HandleCityInput(long chatId, long userId, string text, int? userMsgId, CancellationToken ct)
    {
        if (text.Length > 100) { await CleanUserMsg(chatId, userMsgId, ct); return true; }

        await _stateStore.SetFlowDataAsync(userId, "city", text, ct).ConfigureAwait(false);
        await CleanUserMsg(chatId, userMsgId, ct);
        await DeletePrevBotMsg(chatId, userId, ct);
        await ShowMeetingStep(chatId, userId, ct);
        return true;
    }

    // ═══════════════════════════════════════════════════════════════
    //  STEP: Meeting Preference (cash/exchange)
    // ═══════════════════════════════════════════════════════════════

    private async Task ShowMeetingStep(long chatId, long userId, CancellationToken ct)
    {
        await _stateStore.SetStateAsync(userId, "exc_meeting", ct).ConfigureAwait(false);
        var (step, total) = await GetStepInfo(userId, "exc_meeting", ct);

        var msg = $"<b>📌 مرحله {step} از {total} — ترجیح ملاقات</b>\n" +
                  "━━━━━━━━━━━━━━━━━━━\n\n" +
                  "توضیحات ملاقات حضوری را بنویسید:\n" +
                  "<i>مثال: ترجیحاً مرکز شهر، ساعت عصر، محل عمومی</i>";

        var kb = new List<IReadOnlyList<string>>
        {
            new[] { "بدون ترجیح خاص" },
            new[] { BtnBack, BtnCancel },
        };
        await SafeSendReplyKb(chatId, msg, kb, ct);
    }

    private async Task<bool> HandleMeetingInput(long chatId, long userId, string text, int? userMsgId, CancellationToken ct)
    {
        var pref = text == "بدون ترجیح خاص" ? "" : text;
        await _stateStore.SetFlowDataAsync(userId, "meeting_preference", pref, ct).ConfigureAwait(false);
        await CleanUserMsg(chatId, userMsgId, ct);
        await DeletePrevBotMsg(chatId, userId, ct);

        var txType = await _stateStore.GetFlowDataAsync(userId, "tx_type", ct).ConfigureAwait(false) ?? "buy";
        await ShowRateStep(chatId, userId, ct);
        return true;
    }

    // ═══════════════════════════════════════════════════════════════
    //  STEP: PayPal Email (paypal only)
    // ═══════════════════════════════════════════════════════════════

    private async Task ShowPaypalEmailStep(long chatId, long userId, CancellationToken ct)
    {
        await _stateStore.SetStateAsync(userId, "exc_paypal_email", ct).ConfigureAwait(false);
        var (step, total) = await GetStepInfo(userId, "exc_paypal_email", ct);

        var msg = $"<b>📌 مرحله {step} از {total} — ایمیل پی‌پال</b>\n" +
                  "━━━━━━━━━━━━━━━━━━━\n\n" +
                  "آدرس ایمیل حساب PayPal خود را وارد کنید:\n" +
                  "<i>مثال: user@example.com</i>";

        var kb = new List<IReadOnlyList<string>> { new[] { BtnBack, BtnCancel } };
        await SafeSendReplyKb(chatId, msg, kb, ct);
    }

    private async Task<bool> HandlePaypalEmailInput(long chatId, long userId, string text, int? userMsgId, CancellationToken ct)
    {
        if (!text.Contains('@') || !text.Contains('.'))
        { await CleanUserMsg(chatId, userMsgId, ct); return true; }

        await _stateStore.SetFlowDataAsync(userId, "paypal_email", text.Trim(), ct).ConfigureAwait(false);
        await CleanUserMsg(chatId, userMsgId, ct);
        await DeletePrevBotMsg(chatId, userId, ct);
        await ShowRateStep(chatId, userId, ct);
        return true;
    }

    // ═══════════════════════════════════════════════════════════════
    //  STEP: IBAN (bank only, optional)
    // ═══════════════════════════════════════════════════════════════

    private async Task ShowIbanStep(long chatId, long userId, CancellationToken ct)
    {
        await _stateStore.SetStateAsync(userId, "exc_iban", ct).ConfigureAwait(false);
        var (step, total) = await GetStepInfo(userId, "exc_iban", ct);

        var msg = $"<b>📌 مرحله {step} از {total} — شماره IBAN (اختیاری)</b>\n" +
                  "━━━━━━━━━━━━━━━━━━━\n\n" +
                  "شماره IBAN حساب بانکی را وارد کنید یا رد شوید:\n" +
                  "<i>مثال: NL91ABNA0417164300</i>";

        var kb = new List<IReadOnlyList<string>>
        {
            new[] { BtnSkipIban },
            new[] { BtnBack, BtnCancel },
        };
        await SafeSendReplyKb(chatId, msg, kb, ct);
    }

    private async Task<bool> HandleIbanInput(long chatId, long userId, string text, int? userMsgId, CancellationToken ct)
    {
        var iban = text == BtnSkipIban ? "" : text.Trim().Replace(" ", "");
        await _stateStore.SetFlowDataAsync(userId, "iban", iban, ct).ConfigureAwait(false);
        await CleanUserMsg(chatId, userMsgId, ct);
        await DeletePrevBotMsg(chatId, userId, ct);
        await ShowBankNameStep(chatId, userId, ct);
        return true;
    }

    // ═══════════════════════════════════════════════════════════════
    //  STEP: Bank Name (bank only, optional)
    // ═══════════════════════════════════════════════════════════════

    private async Task ShowBankNameStep(long chatId, long userId, CancellationToken ct)
    {
        await _stateStore.SetStateAsync(userId, "exc_bank_name", ct).ConfigureAwait(false);
        var (step, total) = await GetStepInfo(userId, "exc_bank_name", ct);

        var msg = $"<b>📌 مرحله {step} از {total} — نام بانک (اختیاری)</b>\n" +
                  "━━━━━━━━━━━━━━━━━━━\n\n" +
                  "نام بانک را وارد کنید یا رد شوید:\n" +
                  "<i>مثال: ING، Rabobank، Bank Melli</i>";

        var kb = new List<IReadOnlyList<string>>
        {
            new[] { BtnSkipBank },
            new[] { BtnBack, BtnCancel },
        };
        await SafeSendReplyKb(chatId, msg, kb, ct);
    }

    private async Task<bool> HandleBankNameInput(long chatId, long userId, string text, int? userMsgId, CancellationToken ct)
    {
        var bankName = text == BtnSkipBank ? "" : text.Trim();
        await _stateStore.SetFlowDataAsync(userId, "bank_name", bankName, ct).ConfigureAwait(false);
        await CleanUserMsg(chatId, userMsgId, ct);
        await DeletePrevBotMsg(chatId, userId, ct);
        await ShowRateStep(chatId, userId, ct);
        return true;
    }

    // ═══════════════════════════════════════════════════════════════
    //  STEP: Rate
    // ═══════════════════════════════════════════════════════════════

    private async Task ShowRateStep(long chatId, long userId, CancellationToken ct)
    {
        await _stateStore.SetStateAsync(userId, "exc_rate", ct).ConfigureAwait(false);
        var currency = await _stateStore.GetFlowDataAsync(userId, "currency", ct).ConfigureAwait(false) ?? "";
        var txType = await _stateStore.GetFlowDataAsync(userId, "tx_type", ct).ConfigureAwait(false) ?? "buy";
        var flag = GetCurrencyFlag(currency);
        var currFa = GetCurrencyNameFa(currency);
        var amountStr = await _stateStore.GetFlowDataAsync(userId, "amount", ct).ConfigureAwait(false) ?? "0";
        decimal.TryParse(amountStr, out var amount);
        var (step, total) = await GetStepInfo(userId, "exc_rate", ct);

        string msg;
        if (txType == "exchange")
        {
            var destCurr = await _stateStore.GetFlowDataAsync(userId, "currency_dest", ct).ConfigureAwait(false) ?? "";
            var destFlag = GetCurrencyFlag(destCurr);
            var destFa = GetCurrencyNameFa(destCurr);

            msg = $"<b>📌 مرحله {step} از {total} — نرخ تبادل</b>\n" +
                  "━━━━━━━━━━━━━━━━━━━\n\n" +
                  $"تبادل: <b>{amount:N0}</b> {flag} {currFa} ➡️ {destFlag} {destFa}\n\n" +
                  $"به ازای هر واحد {currFa} چند واحد {destFa} می‌خواهید؟\n" +
                  "<i>نرخ تبادل بین دو ارز را وارد کنید</i>";

            var kb = new List<IReadOnlyList<string>> { new[] { BtnBack, BtnCancel } };
            await SafeSendReplyKb(chatId, msg, kb, ct);
            // Skip to custom input mode for exchange ratio
            await _stateStore.SetStateAsync(userId, "exc_rate_custom", ct).ConfigureAwait(false);
            return;
        }

        msg = $"<b>📌 مرحله {step} از {total} — نرخ پیشنهادی</b>\n" +
              "━━━━━━━━━━━━━━━━━━━\n\n" +
              $"مقدار: <b>{amount:N0}</b> {flag} {currFa}\n\n";

        decimal marketRate = 0;
        try
        {
            var cachedRate = await _exchangeRepo.GetRateAsync(currency, ct).ConfigureAwait(false);
            if (cachedRate != null && cachedRate.Rate > 0)
            {
                marketRate = cachedRate.Rate;
                var min10 = Math.Round(marketRate * 0.90m, 0);
                var max10 = Math.Round(marketRate * 1.10m, 0);
                var totalCalc = amount * marketRate;

                msg += $"💹 نرخ لحظه‌ای بازار: <b>{marketRate:N0}</b> تومان\n" +
                       $"📊 محدوده مجاز (±۱۰٪): {min10:N0} تا {max10:N0}\n" +
                       $"محاسبه: {amount:N0} × {marketRate:N0} = <b>{totalCalc:N0}</b> تومان\n\n";

                await _stateStore.SetFlowDataAsync(userId, "market_rate", marketRate.ToString("F0"), ct).ConfigureAwait(false);
            }
        }
        catch { }

        msg += "• «نرخ بازار» — استفاده از نرخ لحظه‌ای\n" +
               "• «نرخ دلخواه» — وارد کردن نرخ مورد نظر شما";

        var rateKb = new List<IReadOnlyList<string>>
        {
            new[] { BtnMarketRate, BtnCustomRate },
            new[] { BtnBack, BtnCancel },
        };
        await SafeSendReplyKb(chatId, msg, rateKb, ct);
    }

    private async Task<bool> HandleRateInput(long chatId, long userId, string text, int? userMsgId, CancellationToken ct)
    {
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
        }

        if (text == BtnCustomRate)
        {
            await CleanUserMsg(chatId, userMsgId, ct);
            await DeletePrevBotMsg(chatId, userId, ct);
            await ShowCustomRateInput(chatId, userId, ct);
            return true;
        }

        if (decimal.TryParse(text.Replace(",", "").Replace("٫", ""), out var directRate) && directRate > 0)
            return await ValidateAndSaveRate(chatId, userId, directRate, userMsgId, ct);

        await CleanUserMsg(chatId, userMsgId, ct);
        return true;
    }

    private async Task ShowCustomRateInput(long chatId, long userId, CancellationToken ct)
    {
        await _stateStore.SetStateAsync(userId, "exc_rate_custom", ct).ConfigureAwait(false);
        var currency = await _stateStore.GetFlowDataAsync(userId, "currency", ct).ConfigureAwait(false) ?? "";
        var flag = GetCurrencyFlag(currency);
        var currFa = GetCurrencyNameFa(currency);

        var msg = $"<b>📌 وارد کردن نرخ دلخواه</b>\n━━━━━━━━━━━━━━━━━━━\n\n";

        try
        {
            var cachedRate = await _exchangeRepo.GetRateAsync(currency, ct).ConfigureAwait(false);
            if (cachedRate != null && cachedRate.Rate > 0)
            {
                var min10 = Math.Round(cachedRate.Rate * 0.90m, 0);
                var max10 = Math.Round(cachedRate.Rate * 1.10m, 0);
                msg += $"💹 نرخ بازار: <b>{cachedRate.Rate:N0}</b> تومان\n" +
                       $"📊 محدوده مجاز: <b>{min10:N0}</b> تا <b>{max10:N0}</b>\n\n";
            }
        }
        catch { }

        msg += $"نرخ خود را (تومان) برای هر واحد {flag} {currFa} تایپ کنید:";

        var kb = new List<IReadOnlyList<string>> { new[] { BtnBack, BtnCancel } };
        await SafeSendReplyKb(chatId, msg, kb, ct);
    }

    private async Task<bool> HandleRateCustomInput(long chatId, long userId, string text, int? userMsgId, CancellationToken ct)
    {
        if (!decimal.TryParse(text.Replace(",", "").Replace("٫", ""), out var rate) || rate <= 0)
        { await CleanUserMsg(chatId, userMsgId, ct); return true; }

        var txType = await _stateStore.GetFlowDataAsync(userId, "tx_type", ct).ConfigureAwait(false) ?? "buy";
        if (txType == "exchange")
        {
            // For exchange, rate is a ratio — no market rate validation
            await _stateStore.SetFlowDataAsync(userId, "rate", rate.ToString("F4"), ct).ConfigureAwait(false);
            await CleanUserMsg(chatId, userMsgId, ct);
            await DeletePrevBotMsg(chatId, userId, ct);
            await ShowDescStep(chatId, userId, ct);
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
                    var errMsg = $"<b>⚠️ نرخ خارج از محدوده مجاز</b>\n━━━━━━━━━━━━━━━━━━━\n\n" +
                                 $"وارد شده: <b>{rate:N0}</b> — محدوده: <b>{min10:N0}</b> تا <b>{max10:N0}</b>\n\n" +
                                 "نرخی در محدوده مجاز وارد کنید:";
                    await _stateStore.SetStateAsync(userId, "exc_rate_custom", ct).ConfigureAwait(false);
                    await SafeSendReplyKb(chatId, errMsg, new List<IReadOnlyList<string>> { new[] { BtnBack, BtnCancel } }, ct);
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
    //  STEP: Description (optional)
    // ═══════════════════════════════════════════════════════════════

    private async Task ShowDescStep(long chatId, long userId, CancellationToken ct)
    {
        await _stateStore.SetStateAsync(userId, "exc_desc", ct).ConfigureAwait(false);
        var (step, total) = await GetStepInfo(userId, "exc_desc", ct);

        var msg = $"<b>📌 مرحله {step} از {total} — توضیحات (اختیاری)</b>\n" +
                  "━━━━━━━━━━━━━━━━━━━\n\n" +
                  "توضیحات خود را بنویسید یا رد شوید:\n" +
                  "<i>مثلاً: فوری نیاز دارم، قابل مذاکره و...</i>";

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
    //  STEP: Preview
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
        var destCurrency = await _stateStore.GetFlowDataAsync(userId, "currency_dest", ct).ConfigureAwait(false);
        var countryDest = await _stateStore.GetFlowDataAsync(userId, "country_dest", ct).ConfigureAwait(false);
        var city = await _stateStore.GetFlowDataAsync(userId, "city", ct).ConfigureAwait(false);
        var meetingPref = await _stateStore.GetFlowDataAsync(userId, "meeting_preference", ct).ConfigureAwait(false);
        var paypalEmail = await _stateStore.GetFlowDataAsync(userId, "paypal_email", ct).ConfigureAwait(false);
        var iban = await _stateStore.GetFlowDataAsync(userId, "iban", ct).ConfigureAwait(false);
        var bankName = await _stateStore.GetFlowDataAsync(userId, "bank_name", ct).ConfigureAwait(false);

        decimal.TryParse(amountStr, out var amount);
        decimal.TryParse(rateStr, out var rate);

        // Fee calculation
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
        var txFa = TxLabel(txType);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"<b>📋 پیش‌نمایش درخواست {txFa}</b>");
        sb.AppendLine("━━━━━━━━━━━━━━━━━━━\n");
        sb.AppendLine($"👤 نام: {displayName}");
        sb.AppendLine($"💱 ارز: {flag} <b>{amount:N0}</b> {currFa}");

        if (txType == "exchange" && !string.IsNullOrEmpty(destCurrency))
        {
            var destFlag = GetCurrencyFlag(destCurrency);
            var destFa = GetCurrencyNameFa(destCurrency);
            sb.AppendLine($"➡️ مقصد: {destFlag} <b>{destFa}</b>");
            sb.AppendLine($"📊 نرخ تبادل: <b>{rate:F4}</b> {destFa} به ازای هر {currFa}");
        }
        else
        {
            sb.AppendLine($"📊 نرخ: <b>{rate:N0}</b> تومان");
        }

        // Delivery info
        if (delivery == "bank")
        {
            var accFa = accountType == "company" ? "شرکتی" : "شخصی";
            sb.AppendLine($"🏦 حواله بانکی ({accFa})");
            if (!string.IsNullOrEmpty(country)) sb.AppendLine($"🌍 کشور: {country}");
            if (!string.IsNullOrEmpty(iban)) sb.AppendLine($"🔒 IBAN: <tg-spoiler>{iban}</tg-spoiler> <i>(خصوصی)</i>");
            if (!string.IsNullOrEmpty(bankName)) sb.AppendLine($"🔒 بانک: <tg-spoiler>{bankName}</tg-spoiler> <i>(خصوصی)</i>");
        }
        else if (delivery == "paypal")
        {
            sb.AppendLine("💳 پی‌پال");
            if (!string.IsNullOrEmpty(paypalEmail)) sb.AppendLine($"🔒 ایمیل: <tg-spoiler>{paypalEmail}</tg-spoiler> <i>(خصوصی)</i>");
        }
        else if (delivery == "cash")
        {
            sb.AppendLine("💵 اسکناس (حضوری)");
            if (!string.IsNullOrEmpty(country)) sb.AppendLine($"🌍 کشور: {country}");
            if (txType == "exchange" && !string.IsNullOrEmpty(countryDest)) sb.AppendLine($"🌍 مقصد: {countryDest}");
            if (!string.IsNullOrEmpty(city)) sb.AppendLine($"🏙 شهر: {city}");
            if (!string.IsNullOrEmpty(meetingPref)) sb.AppendLine($"📍 ملاقات: {meetingPref}");
        }
        // Note about private info
        if (!string.IsNullOrEmpty(iban) || !string.IsNullOrEmpty(paypalEmail) || !string.IsNullOrEmpty(bankName))
            sb.AppendLine("\n🔒 <i>اطلاعات بانکی/پی‌پال فقط برای شما و ادمین قابل مشاهده است و در آگهی عمومی نمایش داده نمی‌شود.</i>");

        if (!string.IsNullOrEmpty(description))
            sb.AppendLine($"📝 توضیحات: {description}");

        if (txType != "exchange")
        {
            sb.AppendLine("\n━━━━━━━━━━━━━━━━━━━");
            sb.AppendLine($"{amount:N0} × {rate:N0} = {subtotal:N0} تومان");
            if (feePercent > 0)
                sb.AppendLine($"کارمزد ({feePercent:F1}%): {(txType == "buy" ? "+" : "-")}{feeAmount:N0}");
            sb.AppendLine($"💰 <b>مبلغ نهایی: {totalAmount:N0} تومان</b>");
        }

        sb.AppendLine("\n━━━━━━━━━━━━━━━━━━━");
        sb.AppendLine("<i>با زدن «تایید»، درخواست شما جهت بررسی ارسال می‌شود.</i>");

        var inlineKb = new List<IReadOnlyList<InlineButton>>
        {
            new[] { new InlineButton("✅ تایید و ارسال درخواست", CbConfirm) },
            new[] { new InlineButton("❌ انصراف و بازگشت", CbCancel) },
        };

        await SafeSendInline(chatId, sb.ToString(), inlineKb, ct);
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
        var destCurrency = await _stateStore.GetFlowDataAsync(userId, "currency_dest", ct).ConfigureAwait(false);
        var city = await _stateStore.GetFlowDataAsync(userId, "city", ct).ConfigureAwait(false);
        var meetingPref = await _stateStore.GetFlowDataAsync(userId, "meeting_preference", ct).ConfigureAwait(false);
        var paypalEmail = await _stateStore.GetFlowDataAsync(userId, "paypal_email", ct).ConfigureAwait(false);
        var iban = await _stateStore.GetFlowDataAsync(userId, "iban", ct).ConfigureAwait(false);
        var bankName = await _stateStore.GetFlowDataAsync(userId, "bank_name", ct).ConfigureAwait(false);

        decimal.TryParse(amountStr, out var amount);
        decimal.TryParse(rateStr, out var rate);
        decimal.TryParse(feePercentStr, out var feePercent);
        decimal.TryParse(feeAmountStr, out var feeAmount);
        decimal.TryParse(totalAmountStr, out var totalAmount);

        // ── Payment gate: check if ad requires payment before submission ──
        if (_settingsRepo != null)
        {
            try
            {
                var pricingMode = await _settingsRepo.GetValueAsync("ad_pricing_mode", ct).ConfigureAwait(false) ?? "free";
                if (pricingMode == "paid")
                {
                    var adPriceStr = await _settingsRepo.GetValueAsync("ad_price_amount", ct).ConfigureAwait(false) ?? "0";
                    decimal.TryParse(adPriceStr, out var adPrice);
                    if (adPrice > 0)
                    {
                        var paymentMethod = await _settingsRepo.GetValueAsync("ad_payment_method", ct).ConfigureAwait(false) ?? "wallet";
                        if (paymentMethod == "wallet" && _walletRepo != null)
                        {
                            var balance = await _walletRepo.GetBalanceAsync(userId, ct).ConfigureAwait(false);
                            if (balance < adPrice)
                            {
                                await SafeDelete(chatId, triggerMsgId, ct);
                                await RemoveReplyKbSilent(chatId, ct);
                                var errMsg = $"<b>⚠️ موجودی کیف پول کافی نیست</b>\n━━━━━━━━━━━━━━━━━━━\n\n" +
                                             $"💰 هزینه ثبت آگهی: <b>{adPrice:N0}</b> تومان\n" +
                                             $"💳 موجودی فعلی: <b>{balance:N0}</b> تومان\n\n" +
                                             "لطفاً ابتدا کیف پول خود را شارژ کنید.";
                                await SafeSendInline(chatId, errMsg, new List<IReadOnlyList<InlineButton>>
                                {
                                    new[] { new InlineButton("🔙 بازگشت به پیش‌نمایش", CbCancel) },
                                }, ct);
                                return;
                            }
                            // Debit wallet
                            await _walletRepo.DebitAsync(userId, adPrice, $"هزینه ثبت آگهی تبادل ارز", null, ct).ConfigureAwait(false);
                        }
                        else if (paymentMethod == "gateway")
                        {
                            // Gateway payment — inform user and block submission until paid
                            await SafeDelete(chatId, triggerMsgId, ct);
                            await RemoveReplyKbSilent(chatId, ct);
                            var gatewayMsg = $"<b>💳 پرداخت هزینه آگهی</b>\n━━━━━━━━━━━━━━━━━━━\n\n" +
                                             $"💰 هزینه ثبت آگهی: <b>{adPrice:N0}</b> تومان\n\n" +
                                             "لطفاً از طریق کیف پول خود هزینه را پرداخت کنید.\n" +
                                             "پس از شارژ کیف پول، مجدداً اقدام به ثبت درخواست نمایید.";
                            await _sender.SendTextMessageAsync(chatId, gatewayMsg, ct).ConfigureAwait(false);
                            await SendMainMenuAsync(chatId, userId, ct);
                            return;
                        }
                    }
                }
            }
            catch { /* settings read failed — proceed without payment */ }
        }

        var requestNumber = await _exchangeRepo.GetNextRequestNumberAsync(ct).ConfigureAwait(false);

        var dto = new ExchangeRequestDto(
            Id: 0, RequestNumber: requestNumber, TelegramUserId: userId,
            Currency: currency, TransactionType: txType, DeliveryMethod: delivery,
            AccountType: accountType, Country: country, Amount: amount, ProposedRate: rate,
            Description: string.IsNullOrEmpty(description) ? null : description,
            FeePercent: feePercent, FeeAmount: feeAmount, TotalAmount: totalAmount,
            Status: "pending_approval", ChannelMessageId: null, AdminNote: null,
            UserDisplayName: displayName, CreatedAt: DateTimeOffset.UtcNow, UpdatedAt: null,
            DestinationCurrency: string.IsNullOrEmpty(destCurrency) ? null : destCurrency,
            City: string.IsNullOrEmpty(city) ? null : city,
            MeetingPreference: string.IsNullOrEmpty(meetingPref) ? null : meetingPref,
            PaypalEmail: string.IsNullOrEmpty(paypalEmail) ? null : paypalEmail,
            Iban: string.IsNullOrEmpty(iban) ? null : iban,
            BankName: string.IsNullOrEmpty(bankName) ? null : bankName);

        await _exchangeRepo.CreateRequestAsync(dto, ct).ConfigureAwait(false);

        await _stateStore.ClearStateAsync(userId, ct).ConfigureAwait(false);
        await _stateStore.ClearAllFlowDataAsync(userId, ct).ConfigureAwait(false);
        await SafeDelete(chatId, triggerMsgId, ct);

        var flag = GetCurrencyFlag(currency);
        var currFa = GetCurrencyNameFa(currency);
        var txFaDone = TxLabel(txType);

        var msg = $"<b>✅ درخواست با موفقیت ثبت شد</b>\n━━━━━━━━━━━━━━━━━━━\n\n" +
                  $"📋 شماره پیگیری: <b>#{requestNumber}</b>\n" +
                  $"نوع: {txFaDone} | ارز: {flag} <b>{amount:N0}</b> {currFa}\n" +
                  (txType != "exchange" ? $"مبلغ نهایی: <b>{totalAmount:N0}</b> تومان\n" : "") +
                  "\n🕐 وضعیت: <b>در انتظار بررسی</b>\n\n" +
                  "درخواست شما برای بررسی به تیم ارسال شد.";

        // Send plain notification without buttons
        await _sender.SendTextMessageAsync(chatId, msg, ct).ConfigureAwait(false);
        // Then immediately show main menu
        await SendMainMenuAsync(chatId, userId, ct);
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

        // Send plain notification without buttons
        await _sender.SendTextMessageAsync(chatId, "❌ <b>درخواست لغو شد</b>\n\nاطلاعات وارد شده حذف گردید.", ct).ConfigureAwait(false);
        // Then immediately show main menu
        await SendMainMenuAsync(chatId, userId, ct);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Send Main Menu helper
    // ═══════════════════════════════════════════════════════════════

    private async Task SendMainMenuAsync(long chatId, long userId, CancellationToken ct)
    {
        if (_stageRepo == null)
        {
            // Fallback: just set the reply stage and send a basic message
            await _stateStore.SetReplyStageAsync(userId, "main_menu", ct).ConfigureAwait(false);
            return;
        }

        var user = await SafeGetUser(userId, ct);
        var lang = user?.PreferredLanguage ?? "fa";
        var isFa = lang == "fa";

        var stage = await _stageRepo.GetByKeyAsync("main_menu", ct).ConfigureAwait(false);
        var text = stage != null && stage.IsEnabled
            ? (isFa ? (stage.TextFa ?? stage.TextEn ?? "منوی اصلی") : (stage.TextEn ?? stage.TextFa ?? "Main Menu"))
            : (isFa ? "منوی اصلی" : "Main Menu");

        var allButtons = await _stageRepo.GetButtonsAsync("main_menu", ct).ConfigureAwait(false);
        var permSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (_permRepo != null)
        {
            var userPerms = await _permRepo.GetUserPermissionsAsync(userId, ct).ConfigureAwait(false);
            permSet = new HashSet<string>(userPerms, StringComparer.OrdinalIgnoreCase);
        }

        var keyboard = new List<IReadOnlyList<string>>();
        foreach (var row in allButtons
            .Where(b => b.IsEnabled && (string.IsNullOrEmpty(b.RequiredPermission) || permSet.Contains(b.RequiredPermission)))
            .GroupBy(b => b.Row).OrderBy(g => g.Key))
        {
            var rowTexts = row.OrderBy(b => b.Column)
                .Select(b => isFa ? (b.TextFa ?? b.TextEn ?? "?") : (b.TextEn ?? b.TextFa ?? "?"))
                .ToList();
            if (rowTexts.Count > 0) keyboard.Add(rowTexts);
        }

        await _stateStore.SetReplyStageAsync(userId, "main_menu", ct).ConfigureAwait(false);
        await _sender.SendTextMessageWithReplyKeyboardAsync(chatId, text, keyboard, ct).ConfigureAwait(false);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Keyboard builders
    // ═══════════════════════════════════════════════════════════════

    private static List<IReadOnlyList<string>> BuildCurrencyKeyboard()
    {
        return new List<IReadOnlyList<string>>
        {
            new[] { $"{Currencies[0].flag} {Currencies[0].nameFa}", $"{Currencies[1].flag} {Currencies[1].nameFa}", $"{Currencies[2].flag} {Currencies[2].nameFa}" },
            new[] { $"{Currencies[3].flag} {Currencies[3].nameFa}", $"{Currencies[4].flag} {Currencies[4].nameFa}" },
            new[] { $"{Currencies[5].flag} {Currencies[5].nameFa}", $"{Currencies[6].flag} {Currencies[6].nameFa}", $"{Currencies[7].flag} {Currencies[7].nameFa}" },
            new[] { BtnCancel },
        };
    }

    private static List<IReadOnlyList<string>> BuildCountryKeyboard()
    {
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
        return kb;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Helpers
    // ═══════════════════════════════════════════════════════════════

    private static string TxLabel(string txType) => txType switch
    {
        "buy" => "خرید",
        "sell" => "فروش",
        "exchange" => "تبادل",
        _ => txType
    };

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
}
