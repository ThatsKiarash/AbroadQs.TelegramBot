using AbroadQs.Bot.Contracts;

namespace AbroadQs.Bot.Modules.Common;

/// <summary>
/// Redesigned multi-step exchange request flow with creative UX:
/// currency (with live rates) → type → delivery → (bank: account+country) → amount (with calc) → rate (with ±10% range) → description → preview → confirm.
/// Uses a mix of inline keyboard (glass buttons) and reply keyboard for input steps.
/// </summary>
public sealed class ExchangeStateHandler : IUpdateHandler
{
    private readonly IResponseSender _sender;
    private readonly ITelegramUserRepository _userRepo;
    private readonly IUserConversationStateStore _stateStore;
    private readonly IExchangeRepository _exchangeRepo;
    private readonly ISettingsRepository? _settingsRepo;
    private readonly IUserMessageStateRepository? _msgStateRepo;

    private const string CbCancel = "exc_cancel";
    private const string CbConfirm = "exc_confirm";

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
            return cb.StartsWith("exc_", StringComparison.Ordinal)
                || cb.StartsWith("excc:", StringComparison.Ordinal)
                || cb.StartsWith("exct:", StringComparison.Ordinal)
                || cb.StartsWith("excd:", StringComparison.Ordinal)
                || cb.StartsWith("exca:", StringComparison.Ordinal)
                || cb.StartsWith("excr:", StringComparison.Ordinal)
                || cb.StartsWith("excm:", StringComparison.Ordinal)
                || cb.StartsWith("excdesc:", StringComparison.Ordinal)
                || cb.StartsWith("exc_del_msg:", StringComparison.Ordinal);
        }
        return !string.IsNullOrEmpty(context.MessageText);
    }

    public async Task<bool> HandleAsync(BotUpdateContext context, CancellationToken ct)
    {
        if (context.UserId == null) return false;
        var userId = context.UserId.Value;
        var chatId = context.ChatId;

        // ── Callbacks ─────────────────────────────────────────────
        if (context.IsCallbackQuery)
        {
            var cb = context.MessageText?.Trim() ?? "";
            await SafeAnswerCallback(context.CallbackQueryId, null, ct);

            // Delete notification message (clean chat)
            if (cb.StartsWith("exc_del_msg:"))
            {
                var msgIdStr = cb["exc_del_msg:".Length..];
                if (int.TryParse(msgIdStr, out var delMsgId))
                    await SafeDelete(chatId, delMsgId, ct);
                await SafeDelete(chatId, context.CallbackMessageId, ct);
                return true;
            }

            // Continue to rate step after amount calculation display
            if (cb == "exc_go_rate")
            {
                await SafeDelete(chatId, context.CallbackMessageId, ct);
                await ShowRateStep(chatId, userId, ct);
                return true;
            }

            if (cb == CbCancel)
            {
                var st = await _stateStore.GetStateAsync(userId, ct).ConfigureAwait(false);
                if (st == null || !st.StartsWith("exchange_step_")) return false;
                await CancelExchangeAsync(chatId, userId, context.CallbackMessageId, ct);
                return true;
            }

            if (cb == CbConfirm)
            {
                var st = await _stateStore.GetStateAsync(userId, ct).ConfigureAwait(false);
                if (st != "exchange_step_preview") return false;
                await ConfirmExchangeAsync(chatId, userId, context.CallbackMessageId, ct);
                return true;
            }

            // Currency selection
            if (cb.StartsWith("excc:"))
            {
                var st = await _stateStore.GetStateAsync(userId, ct).ConfigureAwait(false);
                if (st != "exchange_step_currency") return false;
                var code = cb["excc:".Length..];
                await _stateStore.SetFlowDataAsync(userId, "currency", code, ct).ConfigureAwait(false);
                await SafeDelete(chatId, context.CallbackMessageId, ct);
                await ShowTransactionTypeStep(chatId, userId, ct);
                return true;
            }

            // Transaction type
            if (cb.StartsWith("exct:"))
            {
                var st = await _stateStore.GetStateAsync(userId, ct).ConfigureAwait(false);
                if (st != "exchange_step_type") return false;
                var type = cb["exct:".Length..];
                await _stateStore.SetFlowDataAsync(userId, "tx_type", type, ct).ConfigureAwait(false);
                await SafeDelete(chatId, context.CallbackMessageId, ct);
                await ShowDeliveryMethodStep(chatId, userId, ct);
                return true;
            }

            // Delivery method
            if (cb.StartsWith("excd:"))
            {
                var st = await _stateStore.GetStateAsync(userId, ct).ConfigureAwait(false);
                if (st != "exchange_step_delivery") return false;
                var method = cb["excd:".Length..];
                await _stateStore.SetFlowDataAsync(userId, "delivery", method, ct).ConfigureAwait(false);
                await SafeDelete(chatId, context.CallbackMessageId, ct);
                if (method == "bank")
                    await ShowAccountTypeStep(chatId, userId, ct);
                else
                    await ShowAmountStep(chatId, userId, ct);
                return true;
            }

            // Account type
            if (cb.StartsWith("exca:"))
            {
                var st = await _stateStore.GetStateAsync(userId, ct).ConfigureAwait(false);
                if (st != "exchange_step_account") return false;
                var accType = cb["exca:".Length..];
                await _stateStore.SetFlowDataAsync(userId, "account_type", accType, ct).ConfigureAwait(false);
                await SafeDelete(chatId, context.CallbackMessageId, ct);
                await ShowCountryStep(chatId, userId, ct);
                return true;
            }

            // Country selection
            if (cb.StartsWith("excr:"))
            {
                var st = await _stateStore.GetStateAsync(userId, ct).ConfigureAwait(false);
                if (st != "exchange_step_country") return false;
                var countryCode = cb["excr:".Length..];
                if (countryCode == "other")
                {
                    await _stateStore.SetStateAsync(userId, "exchange_step_country_text", ct).ConfigureAwait(false);
                    await SafeDelete(chatId, context.CallbackMessageId, ct);
                    var u = await SafeGetUser(userId, ct);
                    await SafeSendInline(chatId,
                        IsFa(u) ? "🌍 لطفا نام کشور مورد نظر خود را تایپ کنید:" : "🌍 Please type your country name:",
                        CancelRow(IsFa(u)), ct);
                    return true;
                }
                var countryName = GetCountryName(countryCode);
                await _stateStore.SetFlowDataAsync(userId, "country", countryName, ct).ConfigureAwait(false);
                await SafeDelete(chatId, context.CallbackMessageId, ct);
                await ShowAmountStep(chatId, userId, ct);
                return true;
            }

            // Amount preset
            if (cb.StartsWith("excm:"))
            {
                var st = await _stateStore.GetStateAsync(userId, ct).ConfigureAwait(false);
                if (st != "exchange_step_amount") return false;
                var amountStr = cb["excm:".Length..];
                await _stateStore.SetFlowDataAsync(userId, "amount", amountStr, ct).ConfigureAwait(false);
                await SafeDelete(chatId, context.CallbackMessageId, ct);
                // Remove reply keyboard
                await RemoveReplyKbSilent(chatId, ct);
                await ShowRateStep(chatId, userId, ct);
                return true;
            }

            // Description skip
            if (cb == "excdesc:skip")
            {
                var st = await _stateStore.GetStateAsync(userId, ct).ConfigureAwait(false);
                if (st != "exchange_step_desc") return false;
                await _stateStore.SetFlowDataAsync(userId, "description", "", ct).ConfigureAwait(false);
                await SafeDelete(chatId, context.CallbackMessageId, ct);
                await ShowPreviewStep(chatId, userId, ct);
                return true;
            }

            return false;
        }

        // ── Text messages ─────────────────────────────────────────
        var state = await _stateStore.GetStateAsync(userId, ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(state) || !state.StartsWith("exchange_step_")) return false;

        var user = await SafeGetUser(userId, ct);
        var isFa = IsFa(user);
        var prevBotMsgId = await GetLastBotMsgId(userId, ct);
        var text = context.MessageText?.Trim() ?? "";

        // ── Country text input ────────────────────────────────────
        if (state == "exchange_step_country_text")
        {
            if (string.IsNullOrEmpty(text)) { await CleanUserMsg(chatId, context.IncomingMessageId, ct); return true; }
            await _stateStore.SetFlowDataAsync(userId, "country", text, ct).ConfigureAwait(false);
            await CleanUserMsg(chatId, context.IncomingMessageId, ct);
            await SafeDelete(chatId, prevBotMsgId, ct);
            await ShowAmountStep(chatId, userId, ct);
            return true;
        }

        // ── Amount text input ─────────────────────────────────────
        if (state == "exchange_step_amount")
        {
            if (!decimal.TryParse(text.Replace(",", "").Replace("٫", ""), out var amount) || amount <= 0)
            {
                await CleanUserMsg(chatId, context.IncomingMessageId, ct);
                var errMsg = isFa
                    ? "⚠️ لطفا یک عدد معتبر وارد کنید.\nمثال: <b>1000</b>"
                    : "⚠️ Please enter a valid number.\nExample: <b>1000</b>";
                await EditOrReplace(chatId, prevBotMsgId, errMsg, CancelRow(isFa), ct);
                return true;
            }

            // Show rich live calculation before proceeding
            var amtCurrency = await _stateStore.GetFlowDataAsync(userId, "currency", ct).ConfigureAwait(false) ?? "";
            var amtFlag = GetCurrencyFlag(amtCurrency);
            var amtCurrFa = GetCurrencyNameFa(amtCurrency);
            try
            {
                var cachedRate = await _exchangeRepo.GetRateAsync(amtCurrency, ct).ConfigureAwait(false);
                if (cachedRate != null && cachedRate.Rate > 0)
                {
                    var est = amount * cachedRate.Rate;
                    var min10 = cachedRate.Rate * 0.9m;
                    var max10 = cachedRate.Rate * 1.1m;
                    var minTotal = amount * min10;
                    var maxTotal = amount * max10;

                    // Show a brief calculation summary as an inline message before moving to rate step
                    await CleanUserMsg(chatId, context.IncomingMessageId, ct);
                    await SafeDelete(chatId, prevBotMsgId, ct);
                    await RemoveReplyKbSilent(chatId, ct);

                    var calcMsg = isFa
                        ? $"🧮 <b>محاسبه سریع</b>\n\n" +
                          $"{amtFlag} {amount:N0} {amtCurrFa}\n" +
                          $"💹 نرخ بازار: <b>{cachedRate.Rate:N0}</b> تومان\n\n" +
                          $"💰 ارزش تقریبی: <b>{est:N0}</b> تومان\n" +
                          $"📊 بازه ±۱۰٪: {minTotal:N0} تا {maxTotal:N0} تومان\n\n" +
                          "<i>در مرحله بعد نرخ پیشنهادی خود را وارد کنید...</i>"
                        : $"🧮 <b>Quick Calculation</b>\n\n" +
                          $"{amtFlag} {amount:N0} {amtCurrency}\n" +
                          $"💹 Market rate: <b>{cachedRate.Rate:N0}</b> Toman\n\n" +
                          $"💰 Est. value: <b>{est:N0}</b> Toman\n" +
                          $"📊 ±10% range: {minTotal:N0} to {maxTotal:N0} Toman\n\n" +
                          "<i>Next: enter your proposed rate...</i>";

                    await _stateStore.SetFlowDataAsync(userId, "amount", amount.ToString("F0"), ct).ConfigureAwait(false);
                    // Show calc with continue button, then wait for user to proceed
                    var calcKb = new List<IReadOnlyList<InlineButton>>
                    {
                        new[] { new InlineButton(isFa ? "👉 ادامه — وارد کردن نرخ" : "👉 Continue — Enter Rate", "exc_go_rate") },
                        new[] { new InlineButton(isFa ? "❌ انصراف" : "❌ Cancel", CbCancel) },
                    };
                    await SafeSendInline(chatId, calcMsg, calcKb, ct);
                    await _stateStore.SetReplyStageAsync(userId, "exchange_step_rate_wait", ct).ConfigureAwait(false);
                    return true;
                }
            }
            catch { }

            // No cached rate — proceed directly
            await _stateStore.SetFlowDataAsync(userId, "amount", amount.ToString("F0"), ct).ConfigureAwait(false);
            await CleanUserMsg(chatId, context.IncomingMessageId, ct);
            await SafeDelete(chatId, prevBotMsgId, ct);
            await RemoveReplyKbSilent(chatId, ct);
            await ShowRateStep(chatId, userId, ct);
            return true;
        }

        // ── Rate wait (user typed instead of clicking continue) ──
        if (state == "exchange_step_rate_wait")
        {
            // Treat any text input as wanting to proceed to rate step
            await CleanUserMsg(chatId, context.IncomingMessageId, ct);
            await RemoveReplyKbSilent(chatId, ct);
            // Try to parse as rate directly
            if (decimal.TryParse(text.Replace(",", "").Replace("٫", ""), out var directRate) && directRate > 0)
            {
                await _stateStore.SetReplyStageAsync(userId, "exchange_step_rate", ct).ConfigureAwait(false);
                await _stateStore.SetFlowDataAsync(userId, "rate", directRate.ToString("F0"), ct).ConfigureAwait(false);
                await _stateStore.SetFlowDataAsync(userId, "pending_rate", "", ct).ConfigureAwait(false);
                await ShowDescriptionStep(chatId, userId, ct);
                return true;
            }
            await ShowRateStep(chatId, userId, ct);
            return true;
        }

        // ── Rate text input ───────────────────────────────────────
        if (state == "exchange_step_rate")
        {
            if (!decimal.TryParse(text.Replace(",", "").Replace("٫", ""), out var rate) || rate <= 0)
            {
                await CleanUserMsg(chatId, context.IncomingMessageId, ct);
                var errMsg = isFa
                    ? "⚠️ لطفا نرخ معتبر (به تومان) وارد کنید.\nمثال: <b>16000</b>"
                    : "⚠️ Please enter a valid rate in Toman.\nExample: <b>16000</b>";
                await EditOrReplace(chatId, prevBotMsgId, errMsg, CancelRow(isFa), ct);
                return true;
            }

            // Validate against market rate
            var rateCurrency = await _stateStore.GetFlowDataAsync(userId, "currency", ct).ConfigureAwait(false) ?? "";
            var rateAmountStr = await _stateStore.GetFlowDataAsync(userId, "amount", ct).ConfigureAwait(false) ?? "0";
            decimal.TryParse(rateAmountStr, out var rateAmount);
            try
            {
                var cachedRate = await _exchangeRepo.GetRateAsync(rateCurrency, ct).ConfigureAwait(false);
                if (cachedRate != null && cachedRate.Rate > 0)
                {
                    var deviation = Math.Abs(rate - cachedRate.Rate) / cachedRate.Rate * 100;
                    if (deviation > 15)
                    {
                        // Check if user is confirming a previously warned rate
                        var pendingRate = await _stateStore.GetFlowDataAsync(userId, "pending_rate", ct).ConfigureAwait(false);
                        if (pendingRate == rate.ToString("F0"))
                        {
                            // Confirmed — proceed
                            await _stateStore.SetFlowDataAsync(userId, "pending_rate", "", ct).ConfigureAwait(false);
                        }
                        else
                        {
                            await _stateStore.SetFlowDataAsync(userId, "pending_rate", rate.ToString("F0"), ct).ConfigureAwait(false);
                            await CleanUserMsg(chatId, context.IncomingMessageId, ct);

                            var diff = rate - cachedRate.Rate;
                            var diffDir = diff > 0 ? "بالاتر" : "پایین‌تر";
                            var total = rateAmount * rate;
                            var totalMarket = rateAmount * cachedRate.Rate;

                            var warnMsg = isFa
                                ? $"⚠️ <b>هشدار — نرخ غیرمعمول</b>\n\n" +
                                  $"نرخ شما: <b>{rate:N0}</b> تومان\n" +
                                  $"نرخ بازار: <b>{cachedRate.Rate:N0}</b> تومان\n" +
                                  $"تفاوت: <b>{deviation:F1}%</b> {diffDir} از بازار\n" +
                                  (rateAmount > 0 ? $"\n💵 با نرخ شما: {rateAmount:N0} × {rate:N0} = <b>{total:N0}</b> تومان" +
                                  $"\n💵 با نرخ بازار: {rateAmount:N0} × {cachedRate.Rate:N0} = <b>{totalMarket:N0}</b> تومان\n" : "\n") +
                                  "\n🔄 اگر مطمئن هستید، <b>همین عدد را دوباره ارسال کنید</b> تا تأیید شود."
                                : $"⚠️ <b>Warning — Unusual rate</b>\n\n" +
                                  $"Your rate: <b>{rate:N0}</b> Toman ({deviation:F1}% from market)\n" +
                                  $"Market rate: <b>{cachedRate.Rate:N0}</b> Toman\n" +
                                  "\n🔄 Resend the same rate to confirm.";
                            await EditOrReplace(chatId, prevBotMsgId, warnMsg, CancelRow(isFa), ct);
                            return true;
                        }
                    }
                }
            }
            catch { }

            await _stateStore.SetFlowDataAsync(userId, "rate", rate.ToString("F0"), ct).ConfigureAwait(false);
            await _stateStore.SetFlowDataAsync(userId, "pending_rate", "", ct).ConfigureAwait(false);
            await CleanUserMsg(chatId, context.IncomingMessageId, ct);
            await SafeDelete(chatId, prevBotMsgId, ct);
            await RemoveReplyKbSilent(chatId, ct);
            await ShowDescriptionStep(chatId, userId, ct);
            return true;
        }

        // ── Description text input ────────────────────────────────
        if (state == "exchange_step_desc")
        {
            await _stateStore.SetFlowDataAsync(userId, "description", text, ct).ConfigureAwait(false);
            await CleanUserMsg(chatId, context.IncomingMessageId, ct);
            await SafeDelete(chatId, prevBotMsgId, ct);
            await ShowPreviewStep(chatId, userId, ct);
            return true;
        }

        return false;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Start flow — called from DynamicStageHandler
    // ═══════════════════════════════════════════════════════════════

    public async Task StartExchangeFlow(long chatId, long userId, string txType, CancellationToken ct)
    {
        await _stateStore.ClearAllFlowDataAsync(userId, ct).ConfigureAwait(false);
        await _stateStore.SetFlowDataAsync(userId, "tx_type", txType, ct).ConfigureAwait(false);

        // Use existing profile name directly — no name confirmation step
        var user = await SafeGetUser(userId, ct);
        var displayName = $"{user?.FirstName} {user?.LastName}".Trim();
        if (string.IsNullOrEmpty(displayName)) displayName = user?.Username ?? $"User_{userId}";
        await _stateStore.SetFlowDataAsync(userId, "display_name", displayName, ct).ConfigureAwait(false);

        await ShowCurrencyStep(chatId, userId, ct);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Step 1: Currency selection — with LIVE RATES
    // ═══════════════════════════════════════════════════════════════

    private async Task ShowCurrencyStep(long chatId, long userId, CancellationToken ct)
    {
        await _stateStore.SetStateAsync(userId, "exchange_step_currency", ct).ConfigureAwait(false);
        var user = await SafeGetUser(userId, ct);
        var isFa = IsFa(user);
        var txType = await _stateStore.GetFlowDataAsync(userId, "tx_type", ct).ConfigureAwait(false) ?? "buy";
        var txFa = txType == "buy" ? "خرید" : txType == "sell" ? "فروش" : "تبادل";

        // Load all cached rates for display
        var rates = new Dictionary<string, decimal>();
        try
        {
            var allRates = await _exchangeRepo.GetRatesAsync(ct).ConfigureAwait(false);
            foreach (var r in allRates)
                rates[r.CurrencyCode.ToUpperInvariant()] = r.Rate;
        }
        catch { }

        var currencies = new (string code, string flag, string fa, string en)[]
        {
            ("USD", "🇺🇸", "دلار", "USD"),
            ("EUR", "🇪🇺", "یورو", "EUR"),
            ("GBP", "🇬🇧", "پوند", "GBP"),
            ("CAD", "🇨🇦", "دلار کانادا", "CAD"),
            ("SEK", "🇸🇪", "کرون سوئد", "SEK"),
            ("CHF", "🇨🇭", "فرانک سوییس", "CHF"),
            ("TRY", "🇹🇷", "لیر ترکیه", "TRY"),
            ("NOK", "🇳🇴", "کرون نروژ", "NOK"),
            ("AUD", "🇦🇺", "دلار استرالیا", "AUD"),
            ("DKK", "🇩🇰", "کرون دانمارک", "DKK"),
            ("AED", "🇦🇪", "درهم", "AED"),
            ("INR", "🇮🇳", "روپیه", "INR"),
            ("USDT", "💎", "تتر", "USDT"),
        };

        var ratesList = "";
        foreach (var c in currencies)
        {
            if (rates.TryGetValue(c.code, out var price) && price > 0)
                ratesList += isFa
                    ? $"\n   {c.flag} {c.fa}: <b>{price:N0}</b> ت"
                    : $"\n   {c.flag} {c.en}: <b>{price:N0}</b> T";
        }

        var msg = isFa
            ? $"💱 <b>ثبت درخواست {txFa} ارز</b>\n\n" +
              Progress(1, 7) +
              "🪙 ارز مورد نظر خود را از لیست زیر انتخاب کنید.\n" +
              "<i>💡 نرخ لحظه‌ای کنار هر ارز نمایش داده شده است.</i>" +
              (ratesList != "" ? $"\n\n📊 <b>تابلوی نرخ لحظه‌ای:</b>{ratesList}" : "") +
              "\n\n<i>📌 نرخ نهایی و کارمزد در مراحل بعدی محاسبه و نمایش داده می‌شود.</i>"
            : $"💱 <b>New {txFa} Exchange Request</b>\n\n" +
              Progress(1, 7) +
              "🪙 Select your currency from the list below.\n" +
              "<i>💡 Live rates are shown next to each currency.</i>" +
              (ratesList != "" ? $"\n\n📊 <b>Live Rate Board:</b>{ratesList}" : "") +
              "\n\n<i>📌 Final rate and fees will be calculated in the next steps.</i>";

        var kb = new List<IReadOnlyList<InlineButton>>();
        for (int i = 0; i < currencies.Length; i += 2)
        {
            var row = new List<InlineButton>();
            for (int j = i; j < Math.Min(i + 2, currencies.Length); j++)
            {
                var priceTag = rates.TryGetValue(currencies[j].code, out var p) && p > 0
                    ? $" [{p:N0}]" : "";
                var label = isFa
                    ? $"{currencies[j].flag} {currencies[j].fa}{priceTag}"
                    : $"{currencies[j].flag} {currencies[j].en}{priceTag}";
                row.Add(new InlineButton(label, $"excc:{currencies[j].code}"));
            }
            kb.Add(row);
        }
        kb.Add(new[] { new InlineButton(isFa ? "❌ انصراف" : "❌ Cancel", CbCancel) });

        await SafeSendInline(chatId, msg, kb, ct);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Step 2: Transaction type (buy/sell/exchange)
    // ═══════════════════════════════════════════════════════════════

    private async Task ShowTransactionTypeStep(long chatId, long userId, CancellationToken ct)
    {
        var existingType = await _stateStore.GetFlowDataAsync(userId, "tx_type", ct).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(existingType) && existingType != "ask")
        {
            await ShowDeliveryMethodStep(chatId, userId, ct);
            return;
        }

        await _stateStore.SetStateAsync(userId, "exchange_step_type", ct).ConfigureAwait(false);
        var user = await SafeGetUser(userId, ct);
        var isFa = IsFa(user);
        var currency = await _stateStore.GetFlowDataAsync(userId, "currency", ct).ConfigureAwait(false) ?? "";
        var currFa = GetCurrencyNameFa(currency);
        var flag = GetCurrencyFlag(currency);

        var msg = isFa
            ? Progress(2, 7) + $"🔄 <b>نوع معامله</b>\n\nقصد خرید یا فروش {flag} {currFa} را دارید؟\n\n" +
              "<i>💡 خرید: شما ارز دریافت می‌کنید\n💡 فروش: شما ارز پرداخت می‌کنید\n💡 تبادل: ارز با ارز</i>"
            : Progress(2, 7) + $"🔄 <b>Transaction Type</b>\n\nDo you want to buy or sell {flag} {currency}?";

        var kb = new List<IReadOnlyList<InlineButton>>
        {
            new[] { new InlineButton(isFa ? "📥 خرید ارز" : "📥 Buy", "exct:buy"), new InlineButton(isFa ? "📤 فروش ارز" : "📤 Sell", "exct:sell") },
            new[] { new InlineButton(isFa ? "🔁 تبادل ارز با ارز" : "🔁 Exchange", "exct:exchange") },
            new[] { new InlineButton(isFa ? "❌ انصراف" : "❌ Cancel", CbCancel) },
        };

        await SafeSendInline(chatId, msg, kb, ct);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Step 3: Delivery method
    // ═══════════════════════════════════════════════════════════════

    private async Task ShowDeliveryMethodStep(long chatId, long userId, CancellationToken ct)
    {
        await _stateStore.SetStateAsync(userId, "exchange_step_delivery", ct).ConfigureAwait(false);
        var user = await SafeGetUser(userId, ct);
        var isFa = IsFa(user);

        var msg = isFa
            ? Progress(3, 7) + "📦 <b>روش تحویل</b>\n\nنحوه دریافت یا ارسال ارز خود را انتخاب کنید:\n\n" +
              "🏦 <b>حواله بانکی:</b> انتقال به حساب بانکی\n" +
              "💳 <b>پی‌پال:</b> انتقال از طریق PayPal\n" +
              "💵 <b>اسکناس:</b> تحویل حضوری"
            : Progress(3, 7) + "📦 <b>Delivery Method</b>\n\nChoose how you want to receive/send the currency.";

        var kb = new List<IReadOnlyList<InlineButton>>
        {
            new[] { new InlineButton(isFa ? "🏦 حواله بانکی" : "🏦 Bank Transfer", "excd:bank") },
            new[] { new InlineButton(isFa ? "💳 پی‌پال" : "💳 PayPal", "excd:paypal"), new InlineButton(isFa ? "💵 اسکناس" : "💵 Cash", "excd:cash") },
            new[] { new InlineButton(isFa ? "❌ انصراف" : "❌ Cancel", CbCancel) },
        };

        await SafeSendInline(chatId, msg, kb, ct);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Step 4a: Account type (bank only)
    // ═══════════════════════════════════════════════════════════════

    private async Task ShowAccountTypeStep(long chatId, long userId, CancellationToken ct)
    {
        await _stateStore.SetStateAsync(userId, "exchange_step_account", ct).ConfigureAwait(false);
        var user = await SafeGetUser(userId, ct);
        var isFa = IsFa(user);

        var msg = isFa
            ? Progress(4, 9) + "🏛 <b>نوع حساب بانکی</b>\n\nحساب مقصد شخصی است یا شرکتی؟\n\n" +
              "<i>💡 نوع حساب روی نحوه انتقال و کارمزد تاثیر دارد.</i>"
            : Progress(4, 9) + "🏛 <b>Account Type</b>\n\nIs the destination account personal or corporate?";

        var kb = new List<IReadOnlyList<InlineButton>>
        {
            new[] { new InlineButton(isFa ? "👤 حساب شخصی" : "👤 Personal", "exca:personal"), new InlineButton(isFa ? "🏢 حساب شرکتی" : "🏢 Corporate", "exca:company") },
            new[] { new InlineButton(isFa ? "❌ انصراف" : "❌ Cancel", CbCancel) },
        };

        await SafeSendInline(chatId, msg, kb, ct);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Step 4b: Country selection — with FLAGS
    // ═══════════════════════════════════════════════════════════════

    private async Task ShowCountryStep(long chatId, long userId, CancellationToken ct)
    {
        await _stateStore.SetStateAsync(userId, "exchange_step_country", ct).ConfigureAwait(false);
        var user = await SafeGetUser(userId, ct);
        var isFa = IsFa(user);

        var msg = isFa
            ? Progress(5, 9) + "🌍 <b>کشور مقصد</b>\n\nحساب بانکی در کدام کشور است؟\n\n" +
              "<i>💡 اگر کشور شما در لیست نیست، «سایر» را بزنید.</i>"
            : Progress(5, 9) + "🌍 <b>Destination Country</b>\n\nWhere is the bank account located?";

        var countries = new (string code, string flag, string name)[]
        {
            ("nl", "🇳🇱", "هلند"), ("de", "🇩🇪", "آلمان"), ("us", "🇺🇸", "آمریکا"),
            ("es", "🇪🇸", "اسپانیا"), ("it", "🇮🇹", "ایتالیا"), ("ir", "🇮🇷", "ایران"),
            ("fr", "🇫🇷", "فرانسه"), ("be", "🇧🇪", "بلژیک"), ("lt", "🇱🇹", "لیتوانی"),
            ("se", "🇸🇪", "سوئد"), ("gb", "🇬🇧", "انگلیس"), ("fi", "🇫🇮", "فنلاند"),
            ("ie", "🇮🇪", "ایرلند"), ("ca", "🇨🇦", "کانادا"), ("no", "🇳🇴", "نروژ"),
            ("hu", "🇭🇺", "مجارستان"), ("ch", "🇨🇭", "سوئیس"), ("ee", "🇪🇪", "استونی"),
            ("dk", "🇩🇰", "دانمارک"), ("tr", "🇹🇷", "ترکیه"), ("other", "🌐", "سایر"),
        };

        var kb = new List<IReadOnlyList<InlineButton>>();
        for (int i = 0; i < countries.Length; i += 3)
        {
            var row = new List<InlineButton>();
            for (int j = i; j < Math.Min(i + 3, countries.Length); j++)
                row.Add(new InlineButton($"{countries[j].flag} {countries[j].name}", $"excr:{countries[j].code}"));
            kb.Add(row);
        }
        kb.Add(new[] { new InlineButton(isFa ? "❌ انصراف" : "❌ Cancel", CbCancel) });

        await SafeSendInline(chatId, msg, kb, ct);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Step 5: Amount — with LIVE CALCULATION + REPLY KEYBOARD
    // ═══════════════════════════════════════════════════════════════

    private async Task ShowAmountStep(long chatId, long userId, CancellationToken ct)
    {
        await _stateStore.SetStateAsync(userId, "exchange_step_amount", ct).ConfigureAwait(false);
        var user = await SafeGetUser(userId, ct);
        var isFa = IsFa(user);
        var currency = await _stateStore.GetFlowDataAsync(userId, "currency", ct).ConfigureAwait(false) ?? "";
        var currFa = GetCurrencyNameFa(currency);
        var flag = GetCurrencyFlag(currency);
        var txType = await _stateStore.GetFlowDataAsync(userId, "tx_type", ct).ConfigureAwait(false) ?? "buy";
        var txFa = txType == "buy" ? "خرید" : txType == "sell" ? "فروش" : "تبادل";
        var delivery = await _stateStore.GetFlowDataAsync(userId, "delivery", ct).ConfigureAwait(false) ?? "";
        var stepN = delivery == "bank" ? 6 : 4;
        var totalN = delivery == "bank" ? 9 : 7;

        // Show current rate info
        var rateInfo = "";
        decimal marketRate = 0;
        try
        {
            var cachedRate = await _exchangeRepo.GetRateAsync(currency, ct).ConfigureAwait(false);
            if (cachedRate != null && cachedRate.Rate > 0)
            {
                marketRate = cachedRate.Rate;
                rateInfo = isFa
                    ? $"\n\n💹 <b>نرخ لحظه‌ای {flag} {currFa}:</b> {marketRate:N0} تومان"
                    : $"\n\n💹 <b>Live rate for {flag} {currency}:</b> {marketRate:N0} Toman";
            }
        }
        catch { }

        // Sample calculations
        var calcExamples = "";
        if (marketRate > 0)
        {
            var examples = new[] { 500m, 1000m, 2000m, 5000m };
            calcExamples = isFa ? "\n\n📐 <b>محاسبه سریع:</b>" : "\n\n📐 <b>Quick calc:</b>";
            foreach (var ex in examples)
            {
                var total = ex * marketRate;
                calcExamples += $"\n   {flag} {ex:N0} ≈ {total:N0} ت";
            }
        }

        var msg = isFa
            ? Progress(stepN, totalN) + $"💰 <b>مقدار {txFa}</b>\n\nچقدر {flag} {currFa} مد نظرتان است؟{rateInfo}{calcExamples}\n\n" +
              "⌨️ <i>یکی از مقادیر پیشنهادی را بزنید یا عدد دلخواه خود را تایپ کنید.</i>\n" +
              "<i>💡 بعد از وارد کردن مقدار، مبلغ تقریبی تومانی به شما نشان داده می‌شود.</i>"
            : Progress(stepN, totalN) + $"💰 <b>{txType} Amount</b>\n\nHow much {flag} {currency}?{rateInfo}{calcExamples}\n\n" +
              "⌨️ <i>Pick a preset amount or type your own number.</i>\n" +
              "<i>💡 After entering, the estimated Toman value will be shown.</i>";

        // Send inline keyboard (with cancel and presets)
        var inlineKb = new List<IReadOnlyList<InlineButton>>();
        var presets = new[] { 100, 200, 500, 1000, 2000, 5000 };
        var row1 = new List<InlineButton>();
        var row2 = new List<InlineButton>();
        for (int i = 0; i < presets.Length; i++)
        {
            var btn = new InlineButton(presets[i].ToString("N0"), $"excm:{presets[i]}");
            if (i < 3) row1.Add(btn); else row2.Add(btn);
        }
        inlineKb.Add(row1);
        inlineKb.Add(row2);
        inlineKb.Add(new[] { new InlineButton(isFa ? "❌ انصراف" : "❌ Cancel", CbCancel) });

        await SafeSendInline(chatId, msg, inlineKb, ct);

        // Also show reply keyboard for quick amount input
        var replyKb = new List<IReadOnlyList<string>>
        {
            new[] { "100", "200", "300", "500" },
            new[] { "1,000", "2,000", "3,000", "5,000" },
            new[] { "10,000", "50,000" },
        };
        try { await _sender.UpdateReplyKeyboardSilentAsync(chatId, replyKb, ct).ConfigureAwait(false); } catch { }
    }

    // ═══════════════════════════════════════════════════════════════
    //  Step 6: Rate — with ±10% RANGE and LIVE CALCULATION
    // ═══════════════════════════════════════════════════════════════

    private async Task ShowRateStep(long chatId, long userId, CancellationToken ct)
    {
        await _stateStore.SetStateAsync(userId, "exchange_step_rate", ct).ConfigureAwait(false);
        var user = await SafeGetUser(userId, ct);
        var isFa = IsFa(user);
        var currency = await _stateStore.GetFlowDataAsync(userId, "currency", ct).ConfigureAwait(false) ?? "";
        var currFa = GetCurrencyNameFa(currency);
        var flag = GetCurrencyFlag(currency);
        var amountStr = await _stateStore.GetFlowDataAsync(userId, "amount", ct).ConfigureAwait(false) ?? "0";
        decimal.TryParse(amountStr, out var amount);
        var delivery = await _stateStore.GetFlowDataAsync(userId, "delivery", ct).ConfigureAwait(false) ?? "";
        var stepN = delivery == "bank" ? 7 : 5;
        var totalN = delivery == "bank" ? 9 : 7;

        // Market rate info
        var rateInfo = "";
        var rangeInfo = "";
        decimal marketRate = 0;
        try
        {
            var cachedRate = await _exchangeRepo.GetRateAsync(currency, ct).ConfigureAwait(false);
            if (cachedRate != null && cachedRate.Rate > 0)
            {
                marketRate = cachedRate.Rate;
                var minR = Math.Round(marketRate * 0.90m, 0);
                var maxR = Math.Round(marketRate * 1.10m, 0);
                var totalAtMarket = amount * marketRate;

                rateInfo = isFa
                    ? $"\n\n💹 <b>نرخ لحظه‌ای بازار:</b> هر واحد {flag} {currFa} = <b>{marketRate:N0}</b> تومان"
                    : $"\n\n💹 <b>Live market rate:</b> 1 {flag} {currency} = <b>{marketRate:N0}</b> Toman";

                rangeInfo = isFa
                    ? $"\n\n📊 <b>محاسبه با نرخ بازار:</b>\n" +
                      $"   {amount:N0} {flag} × {marketRate:N0} = <b>{totalAtMarket:N0}</b> تومان\n" +
                      $"\n🎯 <b>محدوده پیشنهادی:</b>\n" +
                      $"   📉 حداقل: <b>{minR:N0}</b> تومان (۱۰٪ کمتر از بازار)\n" +
                      $"   📊 نرخ بازار: <b>{marketRate:N0}</b> تومان\n" +
                      $"   📈 حداکثر: <b>{maxR:N0}</b> تومان (۱۰٪ بیشتر از بازار)\n" +
                      "\n<i>💡 نرخ پیشنهادی خود را وارد کنید. هرچه به نرخ بازار نزدیک‌تر باشد شانس تأیید بالاتر است.</i>"
                    : $"\n\n📊 <b>Calculation at market rate:</b>\n" +
                      $"   {amount:N0} {flag} × {marketRate:N0} = <b>{totalAtMarket:N0}</b> Toman\n" +
                      $"\n🎯 <b>Suggested range:</b>\n" +
                      $"   📉 Min: <b>{minR:N0}</b> Toman (-10%)\n" +
                      $"   📊 Market: <b>{marketRate:N0}</b> Toman\n" +
                      $"   📈 Max: <b>{maxR:N0}</b> Toman (+10%)\n" +
                      "\n<i>💡 Enter your proposed rate. Closer to market rate = higher approval chance.</i>";
            }
        }
        catch { }

        var msg = isFa
            ? Progress(stepN, totalN) + $"💲 <b>نرخ پیشنهادی</b>\n\nنرخ پیشنهادی خود را (تومان برای هر واحد {flag} {currFa}) وارد کنید:{rateInfo}{rangeInfo}"
            : Progress(stepN, totalN) + $"💲 <b>Proposed Rate</b>\n\nEnter your rate per {flag} {currency} in Toman:{rateInfo}{rangeInfo}";

        var inlineKb = CancelRow(isFa);

        await SafeSendInline(chatId, msg, inlineKb, ct);

        // Show reply keyboard with suggested rate values for quick input
        if (marketRate > 0)
        {
            var r95 = Math.Round(marketRate * 0.95m, 0);
            var r100 = Math.Round(marketRate, 0);
            var r105 = Math.Round(marketRate * 1.05m, 0);
            var r90 = Math.Round(marketRate * 0.90m, 0);
            var r110 = Math.Round(marketRate * 1.10m, 0);
            var replyKb = new List<IReadOnlyList<string>>
            {
                new[] { $"{r90:N0}", $"{r95:N0}", $"{r100:N0}" },
                new[] { $"{r105:N0}", $"{r110:N0}" },
            };
            try { await _sender.UpdateReplyKeyboardSilentAsync(chatId, replyKb, ct).ConfigureAwait(false); } catch { }
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  Step 7: Description (optional)
    // ═══════════════════════════════════════════════════════════════

    private async Task ShowDescriptionStep(long chatId, long userId, CancellationToken ct)
    {
        await _stateStore.SetStateAsync(userId, "exchange_step_desc", ct).ConfigureAwait(false);
        var user = await SafeGetUser(userId, ct);
        var isFa = IsFa(user);
        var delivery = await _stateStore.GetFlowDataAsync(userId, "delivery", ct).ConfigureAwait(false) ?? "";
        var stepN = delivery == "bank" ? 8 : 6;
        var totalN = delivery == "bank" ? 9 : 7;

        var msg = isFa
            ? Progress(stepN, totalN) + "✍️ <b>توضیحات (اختیاری)</b>\n\n" +
              "هر توضیح یا شرطی که دارید تایپ کنید.\n\n" +
              "💡 <b>نمونه‌ها:</b>\n" +
              "• <i>فوری نیاز دارم — همین امروز</i>\n" +
              "• <i>نرخ قابل مذاکره است</i>\n" +
              "• <i>فقط انتقال بانکی — شبا ملت</i>\n" +
              "• <i>ارسال اسکناس فقط در تهران</i>\n\n" +
              "یا دکمه «رد کردن» را بزنید 👇"
            : Progress(stepN, totalN) + "✍️ <b>Description (optional)</b>\n\n" +
              "Type any notes or conditions for your ad.\n\n" +
              "💡 <b>Examples:</b>\n" +
              "• <i>Urgent — needed today</i>\n" +
              "• <i>Rate is negotiable</i>\n" +
              "• <i>Bank transfer only</i>\n\n" +
              "Or press Skip to continue 👇";

        var kb = new List<IReadOnlyList<InlineButton>>
        {
            new[] { new InlineButton(isFa ? "⏭ بدون توضیحات — ادامه" : "⏭ Skip", "excdesc:skip") },
            new[] { new InlineButton(isFa ? "❌ انصراف" : "❌ Cancel", CbCancel) },
        };

        await SafeSendInline(chatId, msg, kb, ct);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Step 8: Preview — detailed summary with fee
    // ═══════════════════════════════════════════════════════════════

    private async Task ShowPreviewStep(long chatId, long userId, CancellationToken ct)
    {
        await _stateStore.SetStateAsync(userId, "exchange_step_preview", ct).ConfigureAwait(false);
        var user = await SafeGetUser(userId, ct);
        var isFa = IsFa(user);

        var currency = await _stateStore.GetFlowDataAsync(userId, "currency", ct).ConfigureAwait(false) ?? "";
        var txType = await _stateStore.GetFlowDataAsync(userId, "tx_type", ct).ConfigureAwait(false) ?? "buy";
        var delivery = await _stateStore.GetFlowDataAsync(userId, "delivery", ct).ConfigureAwait(false) ?? "";
        var accountType = await _stateStore.GetFlowDataAsync(userId, "account_type", ct).ConfigureAwait(false);
        var country = await _stateStore.GetFlowDataAsync(userId, "country", ct).ConfigureAwait(false);
        var amountStr = await _stateStore.GetFlowDataAsync(userId, "amount", ct).ConfigureAwait(false) ?? "0";
        var rateStr = await _stateStore.GetFlowDataAsync(userId, "rate", ct).ConfigureAwait(false) ?? "0";
        var description = await _stateStore.GetFlowDataAsync(userId, "description", ct).ConfigureAwait(false);
        var displayName = await _stateStore.GetFlowDataAsync(userId, "display_name", ct).ConfigureAwait(false)
            ?? $"{user?.FirstName} {user?.LastName}".Trim();

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
        var txFa = txType == "buy" ? "خرید" : txType == "sell" ? "فروش" : "تبادل";
        var roleFa = txType == "buy" ? "خریدار" : txType == "sell" ? "فروشنده" : "متقاضی تبادل";

        var deliveryFa = delivery switch
        {
            "bank" => accountType == "company"
                ? $"🏢 حواله بانکی شرکتی{(country != null ? $" — {country}" : "")}"
                : $"👤 حواله بانکی شخصی{(country != null ? $" — {country}" : "")}",
            "paypal" => "💳 پی‌پال",
            "cash" => "💵 اسکناس",
            _ => delivery
        };

        // Market comparison
        var marketComp = "";
        try
        {
            var cachedRate = await _exchangeRepo.GetRateAsync(currency, ct).ConfigureAwait(false);
            if (cachedRate != null && cachedRate.Rate > 0)
            {
                var diff = rate - cachedRate.Rate;
                var pct = diff / cachedRate.Rate * 100;
                var sign = diff >= 0 ? "+" : "";
                marketComp = isFa
                    ? $" ({sign}{pct:F1}% نسبت به بازار)"
                    : $" ({sign}{pct:F1}% vs market)";
            }
        }
        catch { }

        var delivery_ = delivery == "bank" ? 9 : 7;
        var preview = isFa
            ? Progress(delivery_, delivery_) +
              $"📋 <b>پیش‌نمایش درخواست {txFa}</b>\n" +
              "━━━━━━━━━━━━━━━━━━━\n\n" +
              $"👤 {roleFa}: <b>{displayName}</b>\n" +
              $"🪙 ارز: {flag} <b>{amount:N0}</b> {currFa}\n" +
              $"💲 نرخ پیشنهادی: <b>{rate:N0}</b> تومان{marketComp}\n" +
              $"📦 روش تحویل: {deliveryFa}\n" +
              (!string.IsNullOrEmpty(description) ? $"✍ توضیحات: <i>{description}</i>\n" : "") +
              "\n━━━━━━━━━━━━━━━━━━━\n" +
              $"🧮 <b>محاسبه مالی:</b>\n" +
              $"   💰 {amount:N0} × {rate:N0} = {subtotal:N0} تومان\n" +
              (feePercent > 0
                  ? $"   🏷 کارمزد ({feePercent:F1}%): {(txType == "buy" ? "+" : "-")}{feeAmount:N0} تومان\n"
                  : "") +
              $"   💵 <b>مبلغ نهایی: {totalAmount:N0} تومان</b>\n" +
              "\n━━━━━━━━━━━━━━━━━━━\n" +
              "⚠️ <i>با تأیید، درخواست شما جهت بررسی ادمین ارسال می‌شود.\n" +
              "نتیجه از طریق همین ربات اطلاع‌رسانی خواهد شد.</i>"
            : Progress(delivery_, delivery_) +
              $"📋 <b>{txFa} Request Preview</b>\n" +
              "━━━━━━━━━━━━━━━━━━━\n\n" +
              $"👤 User: <b>{displayName}</b>\n" +
              $"🪙 Currency: {flag} <b>{amount:N0}</b> {currency}\n" +
              $"💲 Rate: <b>{rate:N0}</b> Toman{marketComp}\n" +
              $"📦 Delivery: {deliveryFa}\n" +
              (!string.IsNullOrEmpty(description) ? $"✍ Note: <i>{description}</i>\n" : "") +
              $"\n━━━━━━━━━━━━━━━━━━━\n" +
              $"🧮 <b>Breakdown:</b>\n" +
              $"   💰 {amount:N0} × {rate:N0} = {subtotal:N0} Toman\n" +
              (feePercent > 0
                  ? $"   🏷 Fee ({feePercent:F1}%): {(txType == "buy" ? "+" : "-")}{feeAmount:N0} Toman\n"
                  : "") +
              $"   💵 <b>Total: {totalAmount:N0} Toman</b>\n" +
              "\n━━━━━━━━━━━━━━━━━━━\n" +
              "⚠️ <i>After confirmation, the request will be sent for admin review.\n" +
              "You'll be notified of the result via this bot.</i>";

        var kb = new List<IReadOnlyList<InlineButton>>
        {
            new[] { new InlineButton(isFa ? "✅ تأیید و ارسال" : "✅ Confirm & Submit", CbConfirm) },
            new[] { new InlineButton(isFa ? "❌ انصراف" : "❌ Cancel", CbCancel) },
        };

        await SafeSendInline(chatId, preview, kb, ct);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Confirm: Save to DB + notify
    // ═══════════════════════════════════════════════════════════════

    private async Task ConfirmExchangeAsync(long chatId, long userId, int? triggerMsgId, CancellationToken ct)
    {
        var user = await SafeGetUser(userId, ct);
        var isFa = IsFa(user);

        var currency = await _stateStore.GetFlowDataAsync(userId, "currency", ct).ConfigureAwait(false) ?? "";
        var txType = await _stateStore.GetFlowDataAsync(userId, "tx_type", ct).ConfigureAwait(false) ?? "buy";
        var delivery = await _stateStore.GetFlowDataAsync(userId, "delivery", ct).ConfigureAwait(false) ?? "";
        var accountType = await _stateStore.GetFlowDataAsync(userId, "account_type", ct).ConfigureAwait(false);
        var country = await _stateStore.GetFlowDataAsync(userId, "country", ct).ConfigureAwait(false);
        var amountStr = await _stateStore.GetFlowDataAsync(userId, "amount", ct).ConfigureAwait(false) ?? "0";
        var rateStr = await _stateStore.GetFlowDataAsync(userId, "rate", ct).ConfigureAwait(false) ?? "0";
        var description = await _stateStore.GetFlowDataAsync(userId, "description", ct).ConfigureAwait(false);
        var displayName = await _stateStore.GetFlowDataAsync(userId, "display_name", ct).ConfigureAwait(false)
            ?? $"{user?.FirstName} {user?.LastName}".Trim();
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

        // Clean up
        await _stateStore.ClearStateAsync(userId, ct).ConfigureAwait(false);
        await _stateStore.ClearAllFlowDataAsync(userId, ct).ConfigureAwait(false);
        await SafeDelete(chatId, triggerMsgId, ct);

        var txFa = txType == "buy" ? "خرید" : txType == "sell" ? "فروش" : "تبادل";
        var flag = GetCurrencyFlag(currency);
        var currFa = GetCurrencyNameFa(currency);

        var msg = isFa
            ? $"✅ <b>درخواست {txFa} ثبت شد!</b>\n\n" +
              $"📌 شماره درخواست: <b>#{requestNumber}</b>\n" +
              $"💰 {flag} {amount:N0} {currFa} — {rate:N0} تومان\n" +
              $"💵 مبلغ نهایی: <b>{totalAmount:N0}</b> تومان\n\n" +
              "⏳ درخواست شما در انتظار بررسی ادمین است.\nنتیجه به شما اطلاع داده خواهد شد."
            : $"✅ <b>Request submitted!</b>\n\nRequest <b>#{requestNumber}</b> is pending admin review.";

        // Send with delete button for clean chat
        var kb = new List<IReadOnlyList<InlineButton>>
        {
            new[] { new InlineButton(isFa ? "🗑 پاک کردن پیام" : "🗑 Delete message", $"exc_del_msg:0") },
            new[] { new InlineButton(isFa ? "🔙 منوی اصلی" : "🔙 Main Menu", "stage:main_menu") },
        };

        await SafeSendInline(chatId, msg, kb, ct);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Cancel
    // ═══════════════════════════════════════════════════════════════

    private async Task CancelExchangeAsync(long chatId, long userId, int? triggerMsgId, CancellationToken ct)
    {
        var user = await SafeGetUser(userId, ct);
        var isFa = IsFa(user);

        await _stateStore.ClearStateAsync(userId, ct).ConfigureAwait(false);
        await _stateStore.ClearAllFlowDataAsync(userId, ct).ConfigureAwait(false);
        await SafeDelete(chatId, triggerMsgId, ct);
        await RemoveReplyKbSilent(chatId, ct);

        await SafeSendInline(chatId,
            isFa ? "❌ درخواست لغو شد." : "❌ Request cancelled.",
            new List<IReadOnlyList<InlineButton>>
            {
                new[] { new InlineButton(isFa ? "🔙 منوی اصلی" : "🔙 Main Menu", "stage:main_menu") }
            }, ct);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Notification sender — called from Program.cs approve/reject
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Builds an inline keyboard with delete button for notification messages (approve/reject).
    /// The caller (Program.cs) can use this to build KB for the notification.
    /// </summary>
    public static List<IReadOnlyList<InlineButton>> NotificationButtons(bool isFa, int? channelMsgId = null) => new()
    {
        channelMsgId.HasValue
            ? new[] { new InlineButton(isFa ? "🗑 پاک کردن" : "🗑 Delete", $"exc_del_msg:0") }
            : new[] { new InlineButton(isFa ? "🗑 پاک کردن پیام" : "🗑 Delete", $"exc_del_msg:0") },
    };

    // ═══════════════════════════════════════════════════════════════
    //  Helpers
    // ═══════════════════════════════════════════════════════════════

    private static List<IReadOnlyList<InlineButton>> CancelRow(bool isFa) => new()
    {
        new[] { new InlineButton(isFa ? "❌ انصراف" : "❌ Cancel", CbCancel) }
    };

    private static string Progress(int step, int total)
    {
        var bar = "";
        for (int i = 1; i <= total; i++)
        {
            if (i < step) bar += "✅";
            else if (i == step) bar += "📍";
            else bar += "⬜";
        }
        return $"〔 {bar} 〕 مرحله {step} از {total}\n\n";
    }

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

    private async Task<int?> GetLastBotMsgId(long userId, CancellationToken ct)
    {
        if (_msgStateRepo == null) return null;
        try
        {
            var s = await _msgStateRepo.GetUserMessageStateAsync(userId, ct).ConfigureAwait(false);
            return s?.LastBotTelegramMessageId is > 0 ? (int)s.LastBotTelegramMessageId : null;
        }
        catch { return null; }
    }

    private async Task EditOrReplace(long chatId, int? msgId, string text, List<IReadOnlyList<InlineButton>> kb, CancellationToken ct)
    {
        if (msgId.HasValue) try { await _sender.EditMessageTextWithInlineKeyboardAsync(chatId, msgId.Value, text, kb, ct).ConfigureAwait(false); return; } catch { }
        await SafeSendInline(chatId, text, kb, ct);
    }

    private static bool IsFa(TelegramUserDto? u) => (u?.PreferredLanguage ?? "fa") == "fa";

    // ═══════════════════════════════════════════════════════════════
    //  Currency/Country helpers
    // ═══════════════════════════════════════════════════════════════

    public static string GetCurrencyFlag(string code) => code.ToUpperInvariant() switch
    {
        "USD" => "🇺🇸", "EUR" => "🇪🇺", "GBP" => "🇬🇧", "CAD" => "🇨🇦",
        "SEK" => "🇸🇪", "CHF" => "🇨🇭", "TRY" => "🇹🇷", "NOK" => "🇳🇴",
        "AUD" => "🇦🇺", "DKK" => "🇩🇰", "AED" => "🇦🇪", "INR" => "🇮🇳",
        "USDT" => "💎", _ => "💱"
    };

    public static string GetCurrencyNameFa(string code) => code.ToUpperInvariant() switch
    {
        "USD" => "دلار آمریکا", "EUR" => "یورو", "GBP" => "پوند انگلیس",
        "CAD" => "دلار کانادا", "SEK" => "کرون سوئد", "CHF" => "فرانک سوییس",
        "TRY" => "لیر ترکیه", "NOK" => "کرون نروژ", "AUD" => "دلار استرالیا",
        "DKK" => "کرون دانمارک", "AED" => "درهم امارات", "INR" => "روپیه هند",
        "USDT" => "تتر", _ => code
    };

    internal static string GetCurrencyNameEn(string code) => code.ToUpperInvariant() switch
    {
        "USD" => "US Dollar", "EUR" => "Euro", "GBP" => "British Pound",
        "CAD" => "Canadian Dollar", "SEK" => "Swedish Krona", "CHF" => "Swiss Franc",
        "TRY" => "Turkish Lira", "NOK" => "Norwegian Krone", "AUD" => "Australian Dollar",
        "DKK" => "Danish Krone", "AED" => "UAE Dirham", "INR" => "Indian Rupee",
        "USDT" => "Tether", _ => code
    };

    private static string GetCountryName(string code) => code switch
    {
        "nl" => "هلند", "de" => "آلمان", "us" => "ایالات متحده آمریکا",
        "es" => "اسپانیا", "it" => "ایتالیا", "ir" => "ایران",
        "fr" => "فرانسه", "be" => "بلژیک", "lt" => "لیتوانی",
        "se" => "سوئد", "gb" => "انگلیس", "fi" => "فنلاند",
        "ie" => "ایرلند", "ca" => "کانادا", "no" => "نروژ",
        "hu" => "مجارستان", "ch" => "سوئیس", "ee" => "استونی",
        "dk" => "دانمارک", "tr" => "ترکیه", _ => code
    };
}
