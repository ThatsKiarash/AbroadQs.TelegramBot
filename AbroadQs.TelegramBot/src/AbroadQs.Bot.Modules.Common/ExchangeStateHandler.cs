using AbroadQs.Bot.Contracts;

namespace AbroadQs.Bot.Modules.Common;

/// <summary>
/// Multi-step exchange request flow: name → currency → type → delivery → (account → country) → amount → rate → description → preview → confirm.
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
    private const string CbBack = "exc_back";

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
                || cb.StartsWith("excc:", StringComparison.Ordinal)   // currency
                || cb.StartsWith("exct:", StringComparison.Ordinal)   // type
                || cb.StartsWith("excd:", StringComparison.Ordinal)   // delivery
                || cb.StartsWith("exca:", StringComparison.Ordinal)   // account
                || cb.StartsWith("excr:", StringComparison.Ordinal)   // country
                || cb.StartsWith("excm:", StringComparison.Ordinal)   // amount
                || cb.StartsWith("excdesc:", StringComparison.Ordinal); // description
        }
        return !string.IsNullOrEmpty(context.MessageText);
    }

    public async Task<bool> HandleAsync(BotUpdateContext context, CancellationToken ct)
    {
        if (context.UserId == null) return false;
        var userId = context.UserId.Value;
        var chatId = context.ChatId;

        // ── Callbacks ────────────────────────────────────────────────
        if (context.IsCallbackQuery)
        {
            var cb = context.MessageText?.Trim() ?? "";
            await SafeAnswerCallback(context.CallbackQueryId, null, ct);

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

            // Name: no change
            if (cb == "exc_nochange_name")
            {
                var st = await _stateStore.GetStateAsync(userId, ct).ConfigureAwait(false);
                if (st != "exchange_step_name") return false;
                var u = await SafeGetUser(userId, ct);
                var displayName = $"{u?.FirstName} {u?.LastName}".Trim();
                await _stateStore.SetFlowDataAsync(userId, "display_name", displayName, ct).ConfigureAwait(false);
                await SafeDelete(chatId, context.CallbackMessageId, ct);
                await ShowCurrencyStep(chatId, userId, ct);
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
                {
                    await ShowAccountTypeStep(chatId, userId, ct);
                }
                else
                {
                    await ShowAmountStep(chatId, userId, ct);
                }
                return true;
            }

            // Account type (bank: personal/company)
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
                        IsFa(u) ? "لطفا نام کشور مورد نظر خود را تایپ کنید:" : "Please type your country name:",
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

        // ── Text messages ────────────────────────────────────────────
        var state = await _stateStore.GetStateAsync(userId, ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(state) || !state.StartsWith("exchange_step_")) return false;

        var user = await SafeGetUser(userId, ct);
        var isFa = IsFa(user);
        var prevBotMsgId = await GetLastBotMsgId(userId, ct);
        var text = context.MessageText?.Trim() ?? "";

        // ── Name confirmation ────────────────────────────────────────
        if (state == "exchange_step_name")
        {
            if (text == "بدون تغییر" || text.Equals("No change", StringComparison.OrdinalIgnoreCase))
            {
                // Keep existing name
                var displayName = $"{user?.FirstName} {user?.LastName}".Trim();
                await _stateStore.SetFlowDataAsync(userId, "display_name", displayName, ct).ConfigureAwait(false);
            }
            else
            {
                var parts = text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2)
                {
                    await CleanUserMsg(chatId, context.IncomingMessageId, ct);
                    var msg = isFa
                        ? "لطفا نام و نام خانوادگی خود را در یک خط وارد کنید:\nمثال: <b>علی احمدی</b>"
                        : "Please enter both first and last name:\nExample: <b>John Smith</b>";
                    await EditOrReplace(chatId, prevBotMsgId, msg, NameButtons(isFa), ct);
                    return true;
                }
                await _userRepo.UpdateProfileAsync(userId, parts[0], parts.Length > 1 ? parts[1] : null, null, ct).ConfigureAwait(false);
                await _stateStore.SetFlowDataAsync(userId, "display_name", text, ct).ConfigureAwait(false);
            }

            await CleanUserMsg(chatId, context.IncomingMessageId, ct);
            await SafeDelete(chatId, prevBotMsgId, ct);
            await ShowCurrencyStep(chatId, userId, ct);
            return true;
        }

        // ── Country text input ───────────────────────────────────────
        if (state == "exchange_step_country_text")
        {
            if (string.IsNullOrEmpty(text))
            {
                await CleanUserMsg(chatId, context.IncomingMessageId, ct);
                return true;
            }
            await _stateStore.SetFlowDataAsync(userId, "country", text, ct).ConfigureAwait(false);
            await CleanUserMsg(chatId, context.IncomingMessageId, ct);
            await SafeDelete(chatId, prevBotMsgId, ct);
            await ShowAmountStep(chatId, userId, ct);
            return true;
        }

        // ── Amount text input ────────────────────────────────────────
        if (state == "exchange_step_amount")
        {
            if (!decimal.TryParse(text.Replace(",", ""), out var amount) || amount <= 0)
            {
                await CleanUserMsg(chatId, context.IncomingMessageId, ct);
                var msg = isFa
                    ? "لطفا یک عدد معتبر وارد کنید:"
                    : "Please enter a valid number:";
                await EditOrReplace(chatId, prevBotMsgId, msg, AmountButtons(isFa), ct);
                return true;
            }
            await _stateStore.SetFlowDataAsync(userId, "amount", amount.ToString("F0"), ct).ConfigureAwait(false);
            await CleanUserMsg(chatId, context.IncomingMessageId, ct);
            await SafeDelete(chatId, prevBotMsgId, ct);
            await ShowRateStep(chatId, userId, ct);
            return true;
        }

        // ── Rate text input ──────────────────────────────────────────
        if (state == "exchange_step_rate")
        {
            if (!decimal.TryParse(text.Replace(",", ""), out var rate) || rate <= 0)
            {
                await CleanUserMsg(chatId, context.IncomingMessageId, ct);
                var msg = isFa
                    ? "لطفا نرخ پیشنهادی معتبر (به تومان) وارد کنید:"
                    : "Please enter a valid rate (in Toman):";
                await EditOrReplace(chatId, prevBotMsgId, msg, CancelRow(isFa), ct);
                return true;
            }
            await _stateStore.SetFlowDataAsync(userId, "rate", rate.ToString("F0"), ct).ConfigureAwait(false);
            await CleanUserMsg(chatId, context.IncomingMessageId, ct);
            await SafeDelete(chatId, prevBotMsgId, ct);
            await ShowDescriptionStep(chatId, userId, ct);
            return true;
        }

        // ── Description text input ───────────────────────────────────
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

    // ═══════════════════════════════════════════════════════════════════
    //  Step: Name confirmation
    // ═══════════════════════════════════════════════════════════════════

    public async Task StartExchangeFlow(long chatId, long userId, string txType, CancellationToken ct)
    {
        await _stateStore.ClearAllFlowDataAsync(userId, ct).ConfigureAwait(false);
        await _stateStore.SetFlowDataAsync(userId, "tx_type", txType, ct).ConfigureAwait(false);
        await _stateStore.SetStateAsync(userId, "exchange_step_name", ct).ConfigureAwait(false);

        var user = await SafeGetUser(userId, ct);
        var isFa = IsFa(user);
        var currentName = $"{user?.FirstName} {user?.LastName}".Trim();

        var msg = isFa
            ? $"نام شما اکنون در سیستم <b>{currentName}</b> ثبت شده است.\nلطفا در صورت نیاز به تغییر، نام صحیح خود را وارد کنید:"
            : $"Your name is currently registered as <b>{currentName}</b>.\nPlease enter your correct name if you need to change it:";

        await SafeSendInline(chatId, msg, NameButtons(isFa), ct);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Step: Currency selection
    // ═══════════════════════════════════════════════════════════════════

    private async Task ShowCurrencyStep(long chatId, long userId, CancellationToken ct)
    {
        await _stateStore.SetStateAsync(userId, "exchange_step_currency", ct).ConfigureAwait(false);
        var user = await SafeGetUser(userId, ct);
        var isFa = IsFa(user);
        var msg = isFa ? "لطفا ارز مورد نظر خود را انتخاب نمایید" : "Please select your currency";

        var currencies = new (string code, string fa, string en)[]
        {
            ("USD", "دلار آمریکا", "US Dollar"),
            ("EUR", "یورو", "Euro"),
            ("GBP", "پوند انگلیس", "British Pound"),
            ("CAD", "دلار کانادا", "Canadian Dollar"),
            ("SEK", "کرون سوئد", "Swedish Krona"),
            ("CHF", "فرانک سوییس", "Swiss Franc"),
            ("TRY", "لیر ترکیه", "Turkish Lira"),
            ("NOK", "کرون نروژ", "Norwegian Krone"),
            ("AUD", "دلار استرالیا", "Australian Dollar"),
            ("DKK", "کرون دانمارک", "Danish Krone"),
            ("AED", "درهم امارات", "UAE Dirham"),
            ("INR", "روپیه هند", "Indian Rupee"),
            ("USDT", "تتر", "Tether"),
            ("OTHER", "سایر ارزها", "Other"),
        };

        var kb = new List<IReadOnlyList<InlineButton>>();
        for (int i = 0; i < currencies.Length; i += 3)
        {
            var row = new List<InlineButton>();
            for (int j = i; j < Math.Min(i + 3, currencies.Length); j++)
                row.Add(new InlineButton(isFa ? currencies[j].fa : currencies[j].en, $"excc:{currencies[j].code}"));
            kb.Add(row);
        }
        kb.Add(new[] { new InlineButton(isFa ? "❌ انصراف" : "❌ Cancel", CbCancel) });

        await SafeSendInline(chatId, msg, kb, ct);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Step: Transaction type (buy/sell/exchange)
    // ═══════════════════════════════════════════════════════════════════

    private async Task ShowTransactionTypeStep(long chatId, long userId, CancellationToken ct)
    {
        // Check if tx_type was pre-set from the stage callback
        var existingType = await _stateStore.GetFlowDataAsync(userId, "tx_type", ct).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(existingType) && existingType != "ask")
        {
            // Skip this step since type was already determined
            await ShowDeliveryMethodStep(chatId, userId, ct);
            return;
        }

        await _stateStore.SetStateAsync(userId, "exchange_step_type", ct).ConfigureAwait(false);
        var user = await SafeGetUser(userId, ct);
        var isFa = IsFa(user);
        var currency = await _stateStore.GetFlowDataAsync(userId, "currency", ct).ConfigureAwait(false) ?? "";
        var currencyFa = GetCurrencyNameFa(currency);

        var msg = isFa
            ? $"لطفا انتخاب نمایید که شما قصد فروش یا خرید {currencyFa} دارید:"
            : $"Do you want to buy or sell {currency}?";

        var kb = new List<IReadOnlyList<InlineButton>>
        {
            new[] {
                new InlineButton(isFa ? "خرید" : "Buy", "exct:buy"),
                new InlineButton(isFa ? "فروش" : "Sell", "exct:sell"),
            },
            new[] { new InlineButton(isFa ? "تبادل" : "Exchange", "exct:exchange") },
            new[] { new InlineButton(isFa ? "❌ انصراف" : "❌ Cancel", CbCancel) },
        };

        await SafeSendInline(chatId, msg, kb, ct);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Step: Delivery method
    // ═══════════════════════════════════════════════════════════════════

    private async Task ShowDeliveryMethodStep(long chatId, long userId, CancellationToken ct)
    {
        await _stateStore.SetStateAsync(userId, "exchange_step_delivery", ct).ConfigureAwait(false);
        var user = await SafeGetUser(userId, ct);
        var isFa = IsFa(user);

        var msg = isFa
            ? "لطفا نحوه دریافت حواله خود را انتخاب نمایید:"
            : "Please select your delivery method:";

        var kb = new List<IReadOnlyList<InlineButton>>
        {
            new[] {
                new InlineButton(isFa ? "حواله بانکی" : "Bank Transfer", "excd:bank"),
                new InlineButton(isFa ? "پی‌پال" : "PayPal", "excd:paypal"),
            },
            new[] {
                new InlineButton(isFa ? "اسکناس" : "Cash", "excd:cash"),
            },
            new[] { new InlineButton(isFa ? "❌ انصراف" : "❌ Cancel", CbCancel) },
        };

        await SafeSendInline(chatId, msg, kb, ct);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Step: Account type (personal/company)
    // ═══════════════════════════════════════════════════════════════════

    private async Task ShowAccountTypeStep(long chatId, long userId, CancellationToken ct)
    {
        await _stateStore.SetStateAsync(userId, "exchange_step_account", ct).ConfigureAwait(false);
        var user = await SafeGetUser(userId, ct);
        var isFa = IsFa(user);

        var msg = isFa
            ? "در صورت تمایل به انجام حواله بانکی لطفا تعیین نمایید که حساب بانکی مربوط به شخص است یا شرکت:"
            : "Please specify if the bank account is personal or corporate:";

        var kb = new List<IReadOnlyList<InlineButton>>
        {
            new[] {
                new InlineButton(isFa ? "شخص" : "Personal", "exca:personal"),
                new InlineButton(isFa ? "شرکت" : "Company", "exca:company"),
            },
            new[] { new InlineButton(isFa ? "❌ انصراف" : "❌ Cancel", CbCancel) },
        };

        await SafeSendInline(chatId, msg, kb, ct);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Step: Country selection
    // ═══════════════════════════════════════════════════════════════════

    private async Task ShowCountryStep(long chatId, long userId, CancellationToken ct)
    {
        await _stateStore.SetStateAsync(userId, "exchange_step_country", ct).ConfigureAwait(false);
        var user = await SafeGetUser(userId, ct);
        var isFa = IsFa(user);
        var currency = await _stateStore.GetFlowDataAsync(userId, "currency", ct).ConfigureAwait(false) ?? "";
        var currencyFa = GetCurrencyNameFa(currency);

        var msg = isFa
            ? $"لطفا مشخص نمایید حساب بانکی جهت دریافت {currencyFa} در کدام کشور می‌باشد.\n\nدر صورتی‌که کشور مورد نظر شما در لیست زیر نیست می‌توانید نام آن‌را تایپ کنید."
            : $"Please specify the country for the bank account.\n\nIf your country is not listed, type it manually.";

        var countries = new (string code, string name)[]
        {
            ("nl", "هلند"), ("de", "آلمان"), ("us", "ایالات متحده آمریکا"),
            ("es", "اسپانیا"), ("it", "ایتالیا"), ("ir", "ایران"),
            ("fr", "فرانسه"), ("be", "بلژیک"), ("lt", "لیتوانی"),
            ("se", "سوئد"), ("gb", "انگلیس"), ("fi", "فنلند"),
            ("ie", "ایرلند"), ("ca", "کانادا"), ("no", "نروژ"),
            ("hu", "مجارستان"), ("ch", "سوئیس"), ("ee", "استونی"),
            ("dk", "دانمارک"), ("tr", "ترکیه"), ("other", "سایر"),
        };

        var kb = new List<IReadOnlyList<InlineButton>>();
        for (int i = 0; i < countries.Length; i += 3)
        {
            var row = new List<InlineButton>();
            for (int j = i; j < Math.Min(i + 3, countries.Length); j++)
                row.Add(new InlineButton(countries[j].name, $"excr:{countries[j].code}"));
            kb.Add(row);
        }
        kb.Add(new[] { new InlineButton(isFa ? "❌ انصراف" : "❌ Cancel", CbCancel) });

        await SafeSendInline(chatId, msg, kb, ct);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Step: Amount
    // ═══════════════════════════════════════════════════════════════════

    private async Task ShowAmountStep(long chatId, long userId, CancellationToken ct)
    {
        await _stateStore.SetStateAsync(userId, "exchange_step_amount", ct).ConfigureAwait(false);
        var user = await SafeGetUser(userId, ct);
        var isFa = IsFa(user);
        var currency = await _stateStore.GetFlowDataAsync(userId, "currency", ct).ConfigureAwait(false) ?? "";
        var currencyFa = GetCurrencyNameFa(currency);
        var txType = await _stateStore.GetFlowDataAsync(userId, "tx_type", ct).ConfigureAwait(false) ?? "buy";
        var txFa = txType == "buy" ? "خرید" : txType == "sell" ? "فروش" : "تبادل";

        var msg = isFa
            ? $"لطفا مقدار {currencyFa} جهت {txFa} را انتخاب نمایید:\n(برای سایر مبالغ تعداد {currencyFa} مورد نظر خود را تایپ کرده و ارسال کنید)"
            : $"Please select the amount of {currency} to {txType}:\n(For other amounts, type and send the number)";

        await SafeSendInline(chatId, msg, AmountButtons(isFa), ct);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Step: Rate
    // ═══════════════════════════════════════════════════════════════════

    private async Task ShowRateStep(long chatId, long userId, CancellationToken ct)
    {
        await _stateStore.SetStateAsync(userId, "exchange_step_rate", ct).ConfigureAwait(false);
        var user = await SafeGetUser(userId, ct);
        var isFa = IsFa(user);
        var currency = await _stateStore.GetFlowDataAsync(userId, "currency", ct).ConfigureAwait(false) ?? "";
        var currencyFa = GetCurrencyNameFa(currency);
        var txType = await _stateStore.GetFlowDataAsync(userId, "tx_type", ct).ConfigureAwait(false) ?? "buy";
        var txFa = txType == "buy" ? "خرید" : txType == "sell" ? "فروش" : "تبادل";

        // Try to show current rate from cache
        var rateInfo = "";
        try
        {
            var navasanCode = GetNavasanCode(currency);
            if (navasanCode != null)
            {
                var cachedRate = await _exchangeRepo.GetRateAsync(navasanCode, ct).ConfigureAwait(false);
                if (cachedRate != null && cachedRate.Rate > 0)
                {
                    rateInfo = isFa
                        ? $"\n\n💹 نرخ فعلی {currencyFa}: <b>{cachedRate.Rate:N0}</b> تومان"
                        : $"\n\nCurrent rate for {currency}: <b>{cachedRate.Rate:N0}</b> Toman";
                }
            }
        }
        catch { /* ignore */ }

        var msg = isFa
            ? $"لطفا نرخ پیشنهادی خود را (به تومان برای هر {currencyFa}) جهت {txFa} تایپ کرده و ارسال نمایید:{rateInfo}"
            : $"Please enter your proposed rate (in Toman per {currency}) for {txType}:{rateInfo}";

        await SafeSendInline(chatId, msg, CancelRow(isFa), ct);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Step: Description
    // ═══════════════════════════════════════════════════════════════════

    private async Task ShowDescriptionStep(long chatId, long userId, CancellationToken ct)
    {
        await _stateStore.SetStateAsync(userId, "exchange_step_desc", ct).ConfigureAwait(false);
        var user = await SafeGetUser(userId, ct);
        var isFa = IsFa(user);

        var msg = isFa
            ? "شما می‌توانید توضیحات مورد نظر خود را تایپ کنید:"
            : "You can add a description (optional):";

        var kb = new List<IReadOnlyList<InlineButton>>
        {
            new[] { new InlineButton(isFa ? "بدون توضیحات" : "No description", "excdesc:skip") },
            new[] { new InlineButton(isFa ? "❌ انصراف" : "❌ Cancel", CbCancel) },
        };

        await SafeSendInline(chatId, msg, kb, ct);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Step: Preview
    // ═══════════════════════════════════════════════════════════════════

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

        // Store calculated fee
        await _stateStore.SetFlowDataAsync(userId, "fee_percent", feePercent.ToString("F2"), ct).ConfigureAwait(false);
        await _stateStore.SetFlowDataAsync(userId, "fee_amount", feeAmount.ToString("F0"), ct).ConfigureAwait(false);
        await _stateStore.SetFlowDataAsync(userId, "total_amount", totalAmount.ToString("F0"), ct).ConfigureAwait(false);

        var currencyFa = GetCurrencyNameFa(currency);
        var txTypeFa = txType == "buy" ? "خرید" : txType == "sell" ? "فروش" : "تبادل";
        var txHashtag = txType == "buy" ? $"#خرید_{currencyFa.Replace(" ", "_")}" : txType == "sell" ? $"#فروش_{currencyFa.Replace(" ", "_")}" : $"#تبادل_{currencyFa.Replace(" ", "_")}";

        var deliveryFa = delivery switch
        {
            "bank" => accountType == "company"
                ? $"حواله بانکی حساب شرکتی{(country != null ? $" به {country}" : "")}"
                : $"حواله بانکی حساب شخصی{(country != null ? $" به {country}" : "")}",
            "paypal" => "پی‌پال",
            "cash" => "اسکناس",
            _ => delivery
        };

        var roleFa = txType == "buy" ? "خریدار" : txType == "sell" ? "فروشنده" : "متقاضی تبادل";

        var preview = isFa
            ? $"❗ حواله جدید بابت {txHashtag}\n\n" +
              $"💎 {roleFa}: <b>{displayName}</b>\n" +
              $"💰 مبلغ: <b>{amount:N0}</b> {currencyFa}\n" +
              $"💲 نرخ پیشنهادی: <b>{rate:N0}</b> تومان\n" +
              $"🏦 نوع حواله: {deliveryFa}\n" +
              (!string.IsNullOrEmpty(description) ? $"✍ توضیحات: {description}\n" : "") +
              $"\n❗ این درخواست هنوز تایید نشده است.\n" +
              (feePercent > 0
                  ? $"\n🏷 در صورت توافق با نرخ پیشنهادی {rate:N0} تومان،\n(با احتساب {(txType == "buy" ? "" : "تخفیف ")}کارمزد {feePercent:F1}%) شما در مقابل پرداخت <b>{totalAmount:N0}</b> تومان، مقدار <b>{amount:N0}</b> {currencyFa} دریافت خواهید کرد."
                  : $"\n🏷 مبلغ کل: <b>{totalAmount:N0}</b> تومان")
            : $"New exchange request for {txHashtag}\n\n" +
              $"User: <b>{displayName}</b>\n" +
              $"Amount: <b>{amount:N0}</b> {currency}\n" +
              $"Rate: <b>{rate:N0}</b> Toman\n" +
              $"Delivery: {delivery}\n" +
              (!string.IsNullOrEmpty(description) ? $"Note: {description}\n" : "") +
              $"\nTotal: <b>{totalAmount:N0}</b> Toman";

        var kb = new List<IReadOnlyList<InlineButton>>
        {
            new[] { new InlineButton(isFa ? "✅ تایید و ارسال" : "✅ Confirm", CbConfirm) },
            new[] { new InlineButton(isFa ? "❌ انصراف" : "❌ Cancel", CbCancel) },
        };

        await SafeSendInline(chatId, preview, kb, ct);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Confirm: Save to DB
    // ═══════════════════════════════════════════════════════════════════

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
            Id: 0,
            RequestNumber: requestNumber,
            TelegramUserId: userId,
            Currency: currency,
            TransactionType: txType,
            DeliveryMethod: delivery,
            AccountType: accountType,
            Country: country,
            Amount: amount,
            ProposedRate: rate,
            Description: string.IsNullOrEmpty(description) ? null : description,
            FeePercent: feePercent,
            FeeAmount: feeAmount,
            TotalAmount: totalAmount,
            Status: "pending_approval",
            ChannelMessageId: null,
            AdminNote: null,
            UserDisplayName: displayName,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: null);

        await _exchangeRepo.CreateRequestAsync(dto, ct).ConfigureAwait(false);

        // Clean up
        await _stateStore.ClearStateAsync(userId, ct).ConfigureAwait(false);
        await _stateStore.ClearAllFlowDataAsync(userId, ct).ConfigureAwait(false);
        await SafeDelete(chatId, triggerMsgId, ct);

        var msg = isFa
            ? $"✅ درخواست شما با شماره <b>#{requestNumber}</b> ثبت شد.\n\nدرخواست شما پس از بررسی توسط ادمین در کانال منتشر خواهد شد.\nنتیجه به شما اطلاع داده می‌شود."
            : $"Your request <b>#{requestNumber}</b> has been submitted.\n\nIt will be posted to the channel after admin approval.";

        await SafeSendInline(chatId, msg, new List<IReadOnlyList<InlineButton>>
        {
            new[] { new InlineButton(isFa ? "🔙 بازگشت به منوی اصلی" : "Back to Main Menu", "stage:main_menu") }
        }, ct);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Cancel
    // ═══════════════════════════════════════════════════════════════════

    private async Task CancelExchangeAsync(long chatId, long userId, int? triggerMsgId, CancellationToken ct)
    {
        var user = await SafeGetUser(userId, ct);
        var isFa = IsFa(user);

        await _stateStore.ClearStateAsync(userId, ct).ConfigureAwait(false);
        await _stateStore.ClearAllFlowDataAsync(userId, ct).ConfigureAwait(false);
        await SafeDelete(chatId, triggerMsgId, ct);

        await SafeSendInline(chatId,
            isFa ? "❌ درخواست لغو شد." : "❌ Request cancelled.",
            new List<IReadOnlyList<InlineButton>>
            {
                new[] { new InlineButton(isFa ? "🔙 بازگشت به منوی اصلی" : "Back to Main Menu", "stage:main_menu") }
            }, ct);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Button builders
    // ═══════════════════════════════════════════════════════════════════

    private static List<IReadOnlyList<InlineButton>> NameButtons(bool isFa) => new()
    {
        new[] { new InlineButton(isFa ? "بدون تغییر" : "No change", "exc_nochange_name") },
        new[] { new InlineButton(isFa ? "❌ انصراف" : "❌ Cancel", CbCancel) },
    };

    private static List<IReadOnlyList<InlineButton>> CancelRow(bool isFa) => new()
    {
        new[] { new InlineButton(isFa ? "❌ انصراف" : "❌ Cancel", CbCancel) }
    };

    private static List<IReadOnlyList<InlineButton>> AmountButtons(bool isFa)
    {
        var presets = new[] { 100, 200, 300, 400, 500, 600, 700, 800, 900, 1000, 2000, 3000, 4000, 5000 };
        var kb = new List<IReadOnlyList<InlineButton>>();
        for (int i = 0; i < presets.Length; i += 5)
        {
            var row = new List<InlineButton>();
            for (int j = i; j < Math.Min(i + 5, presets.Length); j++)
                row.Add(new InlineButton(presets[j].ToString("N0"), $"excm:{presets[j]}"));
            kb.Add(row);
        }
        kb.Add(new[] { new InlineButton(isFa ? "❌ انصراف" : "❌ Cancel", CbCancel) });
        return kb;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Safe wrappers & helpers
    // ═══════════════════════════════════════════════════════════════════

    private async Task SafeSendInline(long chatId, string text, List<IReadOnlyList<InlineButton>> kb, CancellationToken ct)
    {
        try { await _sender.SendTextMessageWithInlineKeyboardAsync(chatId, text, kb, ct).ConfigureAwait(false); } catch { }
    }

    private async Task SafeDelete(long chatId, int? msgId, CancellationToken ct)
    { if (msgId.HasValue) try { await _sender.DeleteMessageAsync(chatId, msgId.Value, ct).ConfigureAwait(false); } catch { } }

    private async Task SafeAnswerCallback(string? id, string? text, CancellationToken ct)
    { if (id != null) try { await _sender.AnswerCallbackQueryAsync(id, text, ct).ConfigureAwait(false); } catch { } }

    private async Task<TelegramUserDto?> SafeGetUser(long userId, CancellationToken ct)
    { try { return await _userRepo.GetByTelegramUserIdAsync(userId, ct).ConfigureAwait(false); } catch { return null; } }

    private async Task CleanUserMsg(long chatId, int? msgId, CancellationToken ct)
    { await SafeDelete(chatId, msgId, ct); }

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
        if (msgId.HasValue)
        {
            try
            {
                await _sender.EditMessageTextWithInlineKeyboardAsync(chatId, msgId.Value, text, kb, ct).ConfigureAwait(false);
                return;
            }
            catch { }
        }
        await SafeSendInline(chatId, text, kb, ct);
    }

    private static bool IsFa(TelegramUserDto? u) => (u?.PreferredLanguage ?? "fa") == "fa";

    // ═══════════════════════════════════════════════════════════════════
    //  Currency/Country name helpers
    // ═══════════════════════════════════════════════════════════════════

    public static string GetCurrencyNameFa(string code) => code.ToUpperInvariant() switch
    {
        "USD" => "دلار آمریکا",
        "EUR" => "یورو",
        "GBP" => "پوند انگلیس",
        "CAD" => "دلار کانادا",
        "SEK" => "کرون سوئد",
        "CHF" => "فرانک سوییس",
        "TRY" => "لیر ترکیه",
        "NOK" => "کرون نروژ",
        "AUD" => "دلار استرالیا",
        "DKK" => "کرون دانمارک",
        "AED" => "درهم امارات",
        "INR" => "روپیه هند",
        "USDT" => "تتر",
        _ => code
    };

    internal static string GetCurrencyNameEn(string code) => code.ToUpperInvariant() switch
    {
        "USD" => "US Dollar",
        "EUR" => "Euro",
        "GBP" => "British Pound",
        "CAD" => "Canadian Dollar",
        "SEK" => "Swedish Krona",
        "CHF" => "Swiss Franc",
        "TRY" => "Turkish Lira",
        "NOK" => "Norwegian Krone",
        "AUD" => "Australian Dollar",
        "DKK" => "Danish Krone",
        "AED" => "UAE Dirham",
        "INR" => "Indian Rupee",
        "USDT" => "Tether",
        _ => code
    };

    internal static string? GetNavasanCode(string code) => code.ToUpperInvariant() switch
    {
        "USD" => "usd_sell",
        "EUR" => "eur",
        "GBP" => "gbp_hav",
        "CAD" => "cad",
        "SEK" => "sek",
        "CHF" => "chf",
        "TRY" => "try",
        "NOK" => "nok",
        "AUD" => "aud",
        "DKK" => "dkk",
        "AED" => "aed_sell",
        "INR" => "inr",
        "USDT" => "usdt",
        _ => null
    };

    private static string GetCountryName(string code) => code switch
    {
        "nl" => "هلند", "de" => "آلمان", "us" => "ایالات متحده آمریکا",
        "es" => "اسپانیا", "it" => "ایتالیا", "ir" => "ایران",
        "fr" => "فرانسه", "be" => "بلژیک", "lt" => "لیتوانی",
        "se" => "سوئد", "gb" => "انگلیس", "fi" => "فنلند",
        "ie" => "ایرلند", "ca" => "کانادا", "no" => "نروژ",
        "hu" => "مجارستان", "ch" => "سوئیس", "ee" => "استونی",
        "dk" => "دانمارک", "tr" => "ترکیه", _ => code
    };
}
