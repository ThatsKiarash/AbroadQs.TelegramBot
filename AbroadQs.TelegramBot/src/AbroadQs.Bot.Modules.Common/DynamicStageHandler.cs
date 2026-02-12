using AbroadQs.Bot.Contracts;

namespace AbroadQs.Bot.Modules.Common;

/// <summary>
/// Handles callbacks like "stage:xxx" — loads the stage from DB, checks permissions, and displays it.
/// Also handles "lang:xx" callbacks for language selection.
/// Also handles plain text messages that match reply keyboard buttons.
/// Also handles "exc_hist:" callbacks for My Exchanges and "exc_rates:" for live rates.
///
/// Message transition rules:
///   • Same type (inline → inline)      : editMessageText in-place
///   • Same type (reply-kb → reply-kb)   : editMessageText + silent keyboard update (phantom)
///   • Type change (reply-kb → inline)   : delete reply-kb msg, send new inline msg
///   • Type change (inline → reply-kb)   : delete inline msg, send new reply-kb msg
/// </summary>
public sealed class DynamicStageHandler : IUpdateHandler
{
    private readonly IResponseSender _sender;
    private readonly IBotStageRepository _stageRepo;
    private readonly IPermissionRepository _permRepo;
    private readonly ITelegramUserRepository _userRepo;
    private readonly IUserConversationStateStore _stateStore;
    private readonly IUserMessageStateRepository? _msgStateRepo;
    private readonly ExchangeStateHandler? _exchangeHandler;
    private readonly IExchangeRepository? _exchangeRepo;

    private const int TradesPageSize = 5;

    public DynamicStageHandler(
        IResponseSender sender,
        IBotStageRepository stageRepo,
        IPermissionRepository permRepo,
        ITelegramUserRepository userRepo,
        IUserConversationStateStore stateStore,
        IUserMessageStateRepository? msgStateRepo = null,
        ExchangeStateHandler? exchangeHandler = null,
        IExchangeRepository? exchangeRepo = null)
    {
        _sender = sender;
        _stageRepo = stageRepo;
        _permRepo = permRepo;
        _userRepo = userRepo;
        _stateStore = stateStore;
        _msgStateRepo = msgStateRepo;
        _exchangeHandler = exchangeHandler;
        _exchangeRepo = exchangeRepo;
    }

    public string? Command => null;

    public bool CanHandle(BotUpdateContext context)
    {
        if (context.UserId == null) return false;
        var data = context.MessageText?.Trim();
        if (context.IsCallbackQuery && data != null)
        {
            return data.StartsWith("stage:", StringComparison.OrdinalIgnoreCase)
                || data.StartsWith("lang:", StringComparison.OrdinalIgnoreCase)
                || data.StartsWith("toggle:", StringComparison.OrdinalIgnoreCase)
                || data.StartsWith("exc_hist:", StringComparison.Ordinal)
                || data.StartsWith("exc_rates:", StringComparison.Ordinal)
                || data == "start_kyc";
        }
        var cmd = context.Command;
        if (string.Equals(cmd, "settings", StringComparison.OrdinalIgnoreCase)
            || string.Equals(cmd, "menu", StringComparison.OrdinalIgnoreCase))
            return true;

        if (!context.IsCallbackQuery && !string.IsNullOrEmpty(data) && string.IsNullOrEmpty(cmd))
            return true;

        return false;
    }

    public async Task<bool> HandleAsync(BotUpdateContext context, CancellationToken cancellationToken)
    {
        var userId = context.UserId!.Value;
        var chatId = context.ChatId;
        var data = context.MessageText?.Trim() ?? "";
        var editMessageId = context.IsCallbackQuery ? context.CallbackMessageId : null;

        // Load user's clean-chat preference once (used for conditional deletions)
        var currentUser = await _userRepo.GetByTelegramUserIdAsync(userId, cancellationToken).ConfigureAwait(false);
        var cleanMode = currentUser?.CleanChatMode ?? true;

        // Answer callback to remove loading spinner (skip for toggle: — answered later with toast)
        if (context.IsCallbackQuery && context.CallbackQueryId != null && !data.StartsWith("toggle:", StringComparison.OrdinalIgnoreCase))
            await _sender.AnswerCallbackQueryAsync(context.CallbackQueryId, null, cancellationToken).ConfigureAwait(false);

        // ── exc_hist: callback (My Exchanges navigation) ───────────────
        if (data.StartsWith("exc_hist:", StringComparison.Ordinal))
        {
            await HandleExcHistCallback(chatId, userId, currentUser, data, editMessageId, cancellationToken).ConfigureAwait(false);
            return true;
        }

        // ── exc_rates: callback (Exchange Rates actions) ───────────────
        if (data.StartsWith("exc_rates:", StringComparison.Ordinal))
        {
            await HandleExcRatesCallback(chatId, userId, currentUser, data, editMessageId, cancellationToken).ConfigureAwait(false);
            return true;
        }

        // ── lang:xx callback ──────────────────────────────────────────
        if (data.StartsWith("lang:", StringComparison.OrdinalIgnoreCase))
        {
            var code = data["lang:".Length..].Trim();
            if (code.Length > 0)
            {
                await _userRepo.UpdateProfileAsync(userId, null, null, code, cancellationToken).ConfigureAwait(false);
                // Type change: inline → reply-kb
                if (cleanMode && editMessageId.HasValue)
                    await _sender.DeleteMessageAsync(chatId, editMessageId.Value, cancellationToken).ConfigureAwait(false);
                await ShowReplyKeyboardStageAsync(userId, "main_menu", code, null, cancellationToken).ConfigureAwait(false);
            }
            return true;
        }

        // ── toggle:clean_chat callback ────────────────────────────────
        if (data.StartsWith("toggle:", StringComparison.OrdinalIgnoreCase))
        {
            var toggleKey = data["toggle:".Length..].Trim();
            if (string.Equals(toggleKey, "clean_chat", StringComparison.OrdinalIgnoreCase))
            {
                var newMode = !cleanMode;
                await _userRepo.SetCleanChatModeAsync(userId, newMode, cancellationToken).ConfigureAwait(false);

                var lang = currentUser?.PreferredLanguage ?? "fa";
                var isFa = lang == "fa";
                var toast = newMode
                    ? (isFa ? "حالت چت تمیز فعال شد" : "Clean chat mode enabled")
                    : (isFa ? "حالت چت تمیز غیرفعال شد" : "Clean chat mode disabled");
                if (context.CallbackQueryId != null)
                    await _sender.AnswerCallbackQueryAsync(context.CallbackQueryId, toast, cancellationToken).ConfigureAwait(false);

                // Re-render settings stage inline (edit in-place) to show updated toggle state
                await ShowStageInlineAsync(userId, "settings", editMessageId, null, cancellationToken, cleanChatOverride: newMode).ConfigureAwait(false);
            }
            return true;
        }

        // ── start_kyc callback ─────────────────────────────────────────
        if (data == "start_kyc")
        {
            if (context.CallbackQueryId != null)
                await _sender.AnswerCallbackQueryAsync(context.CallbackQueryId, null, cancellationToken).ConfigureAwait(false);

            var lang = currentUser?.PreferredLanguage ?? "fa";
            var isFa = lang == "fa";

            // Delete the inline message with the KYC prompt
            if (cleanMode && editMessageId.HasValue)
                await _sender.DeleteMessageAsync(chatId, editMessageId.Value, cancellationToken).ConfigureAwait(false);

            await _stateStore.SetStateAsync(userId, "kyc_step_name", cancellationToken).ConfigureAwait(false);
            var msg = isFa
                ? "مراحل احراز هویت:\n۱. نام و نام خانوادگی\n۲. تأیید شماره تلفن (پیامک)\n۳. تأیید ایمیل\n۴. انتخاب کشور محل سکونت\n۵. ارسال عکس تأییدیه\n۶. بررسی توسط تیم\n\nلطفاً نام و نام خانوادگی خود را در یک خط وارد کنید:\nمثال: <b>علی احمدی</b>"
                : "Verification steps:\n1. Full name\n2. Phone verification (SMS)\n3. Email verification\n4. Country of residence\n5. Selfie photo\n6. Team review\n\nPlease enter your first and last name in one line:\nExample: <b>John Smith</b>";
            await _sender.SendTextMessageAsync(chatId, msg, cancellationToken).ConfigureAwait(false);
            return true;
        }

        // ── /settings or /menu command ────────────────────────────────
        if (string.Equals(context.Command, "settings", StringComparison.OrdinalIgnoreCase)
            || string.Equals(context.Command, "menu", StringComparison.OrdinalIgnoreCase))
        {
            if (cleanMode)
                await TryDeleteAsync(chatId, context.IncomingMessageId, cancellationToken).ConfigureAwait(false);
            // Same type (reply-kb → reply-kb): edit text + update keyboard
            var oldBotMsgId = await GetOldBotMessageIdAsync(userId, cancellationToken).ConfigureAwait(false);
            await ShowReplyKeyboardStageAsync(userId, "main_menu", null, cleanMode ? oldBotMsgId : null, cancellationToken).ConfigureAwait(false);
            return true;
        }

        // ── stage:xxx callback (inline button press) ──────────────────
        if (data.StartsWith("stage:", StringComparison.OrdinalIgnoreCase))
        {
            var stageKey = data["stage:".Length..].Trim();
            if (stageKey.Length > 0)
            {
                // ── Verification gate ─────────────────────────────────
                if (RequiresVerification(stageKey) && !string.Equals(currentUser?.KycStatus, "approved", StringComparison.OrdinalIgnoreCase))
                {
                    var lang = currentUser?.PreferredLanguage ?? "fa";
                    var isFa = lang == "fa";
                    var msg = isFa
                        ? "برای استفاده از این بخش ابتدا باید احراز هویت کنید.\nلطفاً مراحل احراز هویت را تکمیل کنید تا بتوانید از خدمات تبادل ارز استفاده نمایید."
                        : "You need to verify your identity before using this section.\nPlease complete the verification process to access currency exchange services.";
                    var kycLabel = isFa ? "شروع احراز هویت" : "Start Verification";
                    var profileLabel = isFa ? "پروفایل من" : "My Profile";
                    var backLabel = isFa ? "بازگشت" : "Back";
                    var keyboard = new List<IReadOnlyList<InlineButton>>
                    {
                        new[] { new InlineButton(kycLabel, "start_kyc") },
                        new[] { new InlineButton(profileLabel, "stage:profile") },
                        new[] { new InlineButton(backLabel, "stage:submit_exchange") }
                    };
                    await SendOrEditTextAsync(chatId, msg, keyboard, editMessageId, cancellationToken).ConfigureAwait(false);
                    return true;
                }

                // ── Exchange flow: verified user clicking buy/sell/exchange ──
                if (RequiresVerification(stageKey) && _exchangeHandler != null)
                {
                    var txType = stageKey switch
                    {
                        "buy_currency" => "buy",
                        "sell_currency" => "sell",
                        "do_exchange" => "exchange",
                        _ => "ask"
                    };
                    if (cleanMode && editMessageId.HasValue)
                        await _sender.DeleteMessageAsync(chatId, editMessageId.Value, cancellationToken).ConfigureAwait(false);
                    await _exchangeHandler.StartExchangeFlow(chatId, userId, txType, cancellationToken).ConfigureAwait(false);
                    return true;
                }

                if (IsReplyKeyboardStage(stageKey))
                {
                    // Type change: inline → reply-kb
                    if (cleanMode && editMessageId.HasValue)
                        await _sender.DeleteMessageAsync(chatId, editMessageId.Value, cancellationToken).ConfigureAwait(false);
                    await ShowReplyKeyboardStageAsync(userId, stageKey, null, null, cancellationToken).ConfigureAwait(false);
                    return true;
                }

                // Profile view: show info instead of generic stage
                if (string.Equals(stageKey, "profile", StringComparison.OrdinalIgnoreCase))
                {
                    var lang = currentUser?.PreferredLanguage ?? "fa";
                    var isFa = lang == "fa";
                    var (profileText, profileKb) = ProfileStateHandler.BuildProfileView(currentUser, isFa);
                    await SendOrEditTextAsync(chatId, profileText, profileKb, editMessageId, cancellationToken).ConfigureAwait(false);
                    return true;
                }

                // Profile edit name
                if (string.Equals(stageKey, "profile_edit_name", StringComparison.OrdinalIgnoreCase))
                {
                    await _stateStore.SetStateAsync(userId, "awaiting_profile_name", cancellationToken).ConfigureAwait(false);
                    var lang = currentUser?.PreferredLanguage ?? "fa";
                    var isFa = lang == "fa";
                    var msg = isFa
                        ? "نام و نام خانوادگی خود را در یک خط بفرستید:\nمثال: <b>علی احمدی</b>"
                        : "Send your first and last name in one line:\nExample: <b>John Smith</b>";
                    await SendOrEditTextAsync(chatId, msg, Array.Empty<IReadOnlyList<InlineButton>>(), editMessageId, cancellationToken).ConfigureAwait(false);
                    return true;
                }

                // ── Exchange Rates: show live rates ──────────────────
                if (string.Equals(stageKey, "exchange_rates", StringComparison.OrdinalIgnoreCase))
                {
                    await ShowExchangeRates(chatId, currentUser, editMessageId, cancellationToken).ConfigureAwait(false);
                    return true;
                }

                // ── My Exchanges: show year selector ─────────────────
                if (string.Equals(stageKey, "my_exchanges", StringComparison.OrdinalIgnoreCase))
                {
                    await ShowMyExchangesYears(chatId, currentUser, editMessageId, cancellationToken).ConfigureAwait(false);
                    return true;
                }

                // Same type: inline → inline: edit in place
                await ShowStageInlineAsync(userId, stageKey, editMessageId, null, cancellationToken).ConfigureAwait(false);
            }
            return true;
        }

        // ── Plain text → match against reply keyboard buttons ──────────
        if (!context.IsCallbackQuery && !string.IsNullOrEmpty(data) && string.IsNullOrEmpty(context.Command))
        {
            var matched = await HandleReplyKeyboardButtonAsync(chatId, userId, data, context.IncomingMessageId, cleanMode, cancellationToken).ConfigureAwait(false);
            return matched;
        }

        return false;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Exchange Rates — live display
    // ═══════════════════════════════════════════════════════════════════

    private async Task ShowExchangeRates(long chatId, TelegramUserDto? user, int? editMessageId, CancellationToken ct)
    {
        var isFa = (user?.PreferredLanguage ?? "fa") == "fa";

        IReadOnlyList<ExchangeRateDto> rates = Array.Empty<ExchangeRateDto>();
        try
        {
            if (_exchangeRepo != null)
                rates = await _exchangeRepo.GetRatesAsync(ct).ConfigureAwait(false);
        }
        catch { }

        string text;
        var kb = new List<IReadOnlyList<InlineButton>>();

        if (rates.Count == 0)
        {
            text = isFa
                ? "<b>💹 نرخ لحظه‌ای ارزها</b>\n" +
                  "━━━━━━━━━━━━━━━━━━━\n\n" +
                  "⚠️ در حال حاضر نرخی در سیستم ثبت نشده است.\n" +
                  "لطفاً بعداً مراجعه کنید یا دکمه به‌روزرسانی را بزنید."
                : "<b>💹 Live Exchange Rates</b>\n" +
                  "━━━━━━━━━━━━━━━━━━━\n\n" +
                  "⚠️ No rates available at the moment.\n" +
                  "Please try again later or press Refresh.";
        }
        else
        {
            // Build inline buttons per currency — 2 per row
            for (int i = 0; i < rates.Count; i += 2)
            {
                var row = new List<InlineButton>();
                for (int j = i; j < Math.Min(i + 2, rates.Count); j++)
                {
                    var r = rates[j];
                    var flag = ExchangeStateHandler.GetCurrencyFlag(r.CurrencyCode);
                    var changeIcon = r.Change > 0 ? "📈" : r.Change < 0 ? "📉" : "";
                    var label = $"{flag} {r.CurrencyCode} {r.Rate:N0} T {changeIcon}";
                    row.Add(new InlineButton(label, "noop"));
                }
                kb.Add(row);
            }

            // Show last updated time in the text
            var latest = rates.OrderByDescending(r => r.LastUpdatedAt).FirstOrDefault();
            var updatedStr = "";
            if (latest != null)
            {
                var updatedLocal = latest.LastUpdatedAt.ToOffset(TimeSpan.FromHours(3.5));
                updatedStr = isFa
                    ? $"\n🕐 آخرین به‌روزرسانی: <b>{updatedLocal:HH:mm}</b> — {updatedLocal:yyyy/MM/dd}"
                    : $"\n🕐 Last updated: <b>{updatedLocal:HH:mm}</b> — {updatedLocal:yyyy/MM/dd}";
            }

            text = isFa
                ? "<b>💹 نرخ لحظه‌ای ارزها</b>\n" +
                  "━━━━━━━━━━━━━━━━━━━\n\n" +
                  "نرخ‌های زیر بر اساس آخرین داده‌های بازار آزاد هستند.\n" +
                  "<i>نرخ‌ها ممکن است با نرخ نهایی معامله متفاوت باشند.</i>" + updatedStr
                : "<b>💹 Live Exchange Rates</b>\n" +
                  "━━━━━━━━━━━━━━━━━━━\n\n" +
                  "Rates are based on the latest open market data.\n" +
                  "<i>Rates may differ from the final transaction rate.</i>" + updatedStr;
        }

        kb.Add(new[] { new InlineButton(isFa ? "🔄 به‌روزرسانی نرخ‌ها" : "🔄 Refresh Rates", "exc_rates:refresh") });
        kb.Add(new[] { new InlineButton(isFa ? "🔙 بازگشت" : "🔙 Back", "stage:student_exchange") });

        await SendOrEditTextAsync(chatId, text, kb, editMessageId, ct).ConfigureAwait(false);
    }

    private async Task HandleExcRatesCallback(long chatId, long userId, TelegramUserDto? user, string data, int? editMessageId, CancellationToken ct)
    {
        // exc_rates:refresh
        if (data == "exc_rates:refresh")
        {
            await ShowExchangeRates(chatId, user, editMessageId, ct).ConfigureAwait(false);
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  My Exchanges — year → month → paginated list
    // ═══════════════════════════════════════════════════════════════════

    private async Task ShowMyExchangesYears(long chatId, TelegramUserDto? user, int? editMessageId, CancellationToken ct)
    {
        var isFa = (user?.PreferredLanguage ?? "fa") == "fa";
        var userId = user?.TelegramUserId ?? 0;
        var firstSeen = user?.FirstSeenAt ?? DateTimeOffset.UtcNow;
        var nowIran = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(3.5));

        var startYear = firstSeen.Year;
        var endYear = nowIran.Year;

        // Get exchange counts per year
        IReadOnlyDictionary<int, int> yearCounts = new Dictionary<int, int>();
        try
        {
            if (_exchangeRepo != null)
                yearCounts = await _exchangeRepo.GetUserExchangeCountByYearAsync(userId, ct).ConfigureAwait(false);
        }
        catch { }

        var totalAll = yearCounts.Values.Sum();
        var memberSince = firstSeen.ToOffset(TimeSpan.FromHours(3.5));

        var text = isFa
            ? "<b>📋 تبادلات من</b>\n" +
              "━━━━━━━━━━━━━━━━━━━\n\n" +
              "از این بخش می‌توانید تاریخچه کامل درخواست‌ها و تبادلات خود را مشاهده و پیگیری کنید.\n" +
              "وضعیت هر درخواست به‌صورت لحظه‌ای به‌روزرسانی می‌شود.\n\n" +
              $"📅 عضویت از: <b>{memberSince:yyyy/MM/dd}</b>\n" +
              $"📊 مجموع درخواست‌ها: <b>{totalAll}</b>\n\n" +
              "سال مورد نظر را انتخاب کنید:"
            : "<b>📋 My Exchanges</b>\n" +
              "━━━━━━━━━━━━━━━━━━━\n\n" +
              "View and track the full history of your exchange requests.\n" +
              "Each request's status is updated in real-time.\n\n" +
              $"📅 Member since: <b>{memberSince:yyyy/MM/dd}</b>\n" +
              $"📊 Total requests: <b>{totalAll}</b>\n\n" +
              "Select a year:";

        var kb = new List<IReadOnlyList<InlineButton>>();
        // Show years in rows of 3
        var years = new List<int>();
        for (int y = endYear; y >= startYear; y--)
            years.Add(y);

        for (int i = 0; i < years.Count; i += 3)
        {
            var row = new List<InlineButton>();
            for (int j = i; j < Math.Min(i + 3, years.Count); j++)
            {
                var y = years[j];
                yearCounts.TryGetValue(y, out var count);
                var label = count > 0 ? $"📁 {y} ({count})" : $"{y}";
                row.Add(new InlineButton(label, $"exc_hist:y:{y}"));
            }
            kb.Add(row);
        }

        kb.Add(new[] { new InlineButton(isFa ? "🔙 بازگشت" : "🔙 Back", "stage:student_exchange") });

        await SendOrEditTextAsync(chatId, text, kb, editMessageId, ct).ConfigureAwait(false);
    }

    private async Task ShowMyExchangesMonths(long chatId, TelegramUserDto? user, int year, int? editMessageId, CancellationToken ct)
    {
        var isFa = (user?.PreferredLanguage ?? "fa") == "fa";
        var userId = user?.TelegramUserId ?? 0;

        var monthNamesFa = new[] { "", "ژانویه", "فوریه", "مارس", "آوریل", "مه", "ژوئن", "ژوئیه", "اوت", "سپتامبر", "اکتبر", "نوامبر", "دسامبر" };
        var monthNamesEn = new[] { "", "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };

        // Get exchange counts per month
        IReadOnlyDictionary<int, int> monthCounts = new Dictionary<int, int>();
        try
        {
            if (_exchangeRepo != null)
                monthCounts = await _exchangeRepo.GetUserExchangeCountByMonthAsync(userId, year, ct).ConfigureAwait(false);
        }
        catch { }

        var totalYear = monthCounts.Values.Sum();

        var text = isFa
            ? $"<b>📋 تبادلات من — {year}</b>\n" +
              "━━━━━━━━━━━━━━━━━━━\n\n" +
              $"📊 مجموع درخواست‌های سال {year}: <b>{totalYear}</b>\n\n" +
              "ماه مورد نظر را انتخاب کنید.\n" +
              "<i>ماه‌هایی که درخواست دارند با تعداد نمایش داده می‌شوند.</i>"
            : $"<b>📋 My Exchanges — {year}</b>\n" +
              "━━━━━━━━━━━━━━━━━━━\n\n" +
              $"📊 Total requests in {year}: <b>{totalYear}</b>\n\n" +
              "Select a month.\n" +
              "<i>Months with requests show their count.</i>";

        var kb = new List<IReadOnlyList<InlineButton>>();

        // Show months in rows of 3
        for (int i = 1; i <= 12; i += 3)
        {
            var row = new List<InlineButton>();
            for (int m = i; m < Math.Min(i + 3, 13); m++)
            {
                var monthName = isFa ? monthNamesFa[m] : monthNamesEn[m];
                monthCounts.TryGetValue(m, out var count);
                var label = count > 0 ? $"{monthName} ({count})" : monthName;
                row.Add(new InlineButton(label, $"exc_hist:m:{year}:{m}"));
            }
            kb.Add(row);
        }

        kb.Add(new[] { new InlineButton(isFa ? "🔙 بازگشت به سال‌ها" : "🔙 Back to years", "exc_hist:years") });

        await SendOrEditTextAsync(chatId, text, kb, editMessageId, ct).ConfigureAwait(false);
    }

    private async Task ShowMyExchangesList(long chatId, TelegramUserDto? user, int year, int month, int page, int? editMessageId, CancellationToken ct)
    {
        var userId = user?.TelegramUserId ?? 0;
        var isFa = (user?.PreferredLanguage ?? "fa") == "fa";

        var monthNamesFa = new[] { "", "ژانویه", "فوریه", "مارس", "آوریل", "مه", "ژوئن", "ژوئیه", "اوت", "سپتامبر", "اکتبر", "نوامبر", "دسامبر" };
        var monthNamesEn = new[] { "", "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };
        var monthName = isFa ? monthNamesFa[month] : monthNamesEn[month];

        (IReadOnlyList<ExchangeRequestDto> items, int totalCount) = (Array.Empty<ExchangeRequestDto>(), 0);
        try
        {
            if (_exchangeRepo != null)
                (items, totalCount) = await _exchangeRepo.ListUserRequestsPagedAsync(userId, year, month, page, TradesPageSize, ct).ConfigureAwait(false);
        }
        catch { }

        var totalPages = (int)Math.Ceiling((double)totalCount / TradesPageSize);
        if (totalPages < 1) totalPages = 1;

        string text;
        var kb = new List<IReadOnlyList<InlineButton>>();

        if (totalCount == 0)
        {
            text = isFa
                ? $"<b>📋 تبادلات من — {monthName} {year}</b>\n" +
                  "━━━━━━━━━━━━━━━━━━━\n\n" +
                  "📭 در این ماه هیچ درخواستی ثبت نشده است.\n\n" +
                  "برای ثبت درخواست جدید، از بخش «ثبت درخواست تبادل» اقدام کنید."
                : $"<b>📋 My Exchanges — {monthName} {year}</b>\n" +
                  "━━━━━━━━━━━━━━━━━━━\n\n" +
                  "📭 No requests found for this month.\n\n" +
                  "To submit a new request, go to the \"Submit Exchange\" section.";
        }
        else
        {
            text = isFa
                ? $"<b>📋 تبادلات من — {monthName} {year}</b>\n" +
                  "━━━━━━━━━━━━━━━━━━━\n\n" +
                  $"📊 مجموع: <b>{totalCount}</b> درخواست — صفحه <b>{page + 1}</b> از <b>{totalPages}</b>\n\n" +
                  "برای مشاهده جزئیات هر درخواست، روی آن کلیک کنید:"
                : $"<b>📋 My Exchanges — {monthName} {year}</b>\n" +
                  "━━━━━━━━━━━━━━━━━━━\n\n" +
                  $"📊 Total: <b>{totalCount}</b> requests — Page <b>{page + 1}</b> of <b>{totalPages}</b>\n\n" +
                  "Tap a request to see its details:";

            // Each request as an inline button
            foreach (var req in items)
            {
                var flag = ExchangeStateHandler.GetCurrencyFlag(req.Currency);
                var statusIcon = GetStatusIcon(req.Status);
                var txLabel = isFa
                    ? (req.TransactionType == "buy" ? "خرید" : req.TransactionType == "sell" ? "فروش" : "تبادل")
                    : (req.TransactionType == "buy" ? "Buy" : req.TransactionType == "sell" ? "Sell" : "Exc");
                var btnLabel = $"{statusIcon} #{req.RequestNumber} | {txLabel} {flag} {req.Amount:N0} {req.Currency}";
                kb.Add(new[] { new InlineButton(btnLabel, $"exc_hist:d:{req.Id}:{year}:{month}:{page}") });
            }
        }

        // Pagination buttons
        if (totalPages > 1)
        {
            var navRow = new List<InlineButton>();
            if (page > 0)
                navRow.Add(new InlineButton("◀️ قبلی", $"exc_hist:p:{year}:{month}:{page - 1}"));
            navRow.Add(new InlineButton($"📄 {page + 1}/{totalPages}", "noop"));
            if (page < totalPages - 1)
                navRow.Add(new InlineButton("بعدی ▶️", $"exc_hist:p:{year}:{month}:{page + 1}"));
            kb.Add(navRow);
        }

        kb.Add(new[] { new InlineButton(isFa ? "🔙 بازگشت به ماه‌ها" : "🔙 Back to months", $"exc_hist:y:{year}") });
        kb.Add(new[] { new InlineButton(isFa ? "📅 بازگشت به سال‌ها" : "📅 Back to years", "exc_hist:years") });

        await SendOrEditTextAsync(chatId, text, kb, editMessageId, ct).ConfigureAwait(false);
    }

    private async Task ShowMyExchangeDetail(long chatId, TelegramUserDto? user, int requestId, int year, int month, int page, int? editMessageId, CancellationToken ct)
    {
        var isFa = (user?.PreferredLanguage ?? "fa") == "fa";

        ExchangeRequestDto? req = null;
        try
        {
            if (_exchangeRepo != null)
                req = await _exchangeRepo.GetRequestAsync(requestId, ct).ConfigureAwait(false);
        }
        catch { }

        if (req == null)
        {
            var notFound = isFa ? "⚠️ درخواست یافت نشد." : "⚠️ Request not found.";
            var kb404 = new List<IReadOnlyList<InlineButton>>
            {
                new[] { new InlineButton(isFa ? "🔙 بازگشت به لیست" : "🔙 Back to list", $"exc_hist:p:{year}:{month}:{page}") }
            };
            await SendOrEditTextAsync(chatId, notFound, kb404, editMessageId, ct).ConfigureAwait(false);
            return;
        }

        var flag = ExchangeStateHandler.GetCurrencyFlag(req.Currency);
        var currName = isFa
            ? ExchangeStateHandler.GetCurrencyNameFa(req.Currency)
            : ExchangeStateHandler.GetCurrencyNameEn(req.Currency);
        var statusIcon = GetStatusIcon(req.Status);
        var statusLabel = isFa ? GetStatusLabelFa(req.Status) : GetStatusLabelEn(req.Status);
        var txLabel = isFa
            ? (req.TransactionType == "buy" ? "خرید" : req.TransactionType == "sell" ? "فروش" : "تبادل")
            : (req.TransactionType == "buy" ? "Buy" : req.TransactionType == "sell" ? "Sell" : "Exchange");
        var deliveryLabel = isFa
            ? (req.DeliveryMethod == "bank" ? "حواله بانکی" : req.DeliveryMethod == "paypal" ? "پی‌پال" : req.DeliveryMethod == "cash" ? "اسکناس" : req.DeliveryMethod)
            : (req.DeliveryMethod == "bank" ? "Bank Transfer" : req.DeliveryMethod == "paypal" ? "PayPal" : req.DeliveryMethod == "cash" ? "Cash" : req.DeliveryMethod);
        var date = req.CreatedAt.ToOffset(TimeSpan.FromHours(3.5));

        var text = isFa
            ? $"<b>📄 جزئیات درخواست #{req.RequestNumber}</b>\n" +
              "━━━━━━━━━━━━━━━━━━━\n\n" +
              $"{statusIcon} وضعیت: <b>{statusLabel}</b>\n\n" +
              $"💱 نوع: <b>{txLabel}</b>\n" +
              $"💵 ارز: {flag} <b>{req.Amount:N0}</b> {currName}\n" +
              $"📊 نرخ: <b>{req.ProposedRate:N0}</b> تومان\n" +
              $"🚚 تحویل: <b>{deliveryLabel}</b>" +
              (!string.IsNullOrEmpty(req.AccountType)
                  ? $" ({(req.AccountType == "company" ? "شرکتی" : "شخصی")})"
                  : "") + "\n" +
              (!string.IsNullOrEmpty(req.Country) ? $"🌍 کشور: <b>{req.Country}</b>\n" : "") +
              (!string.IsNullOrEmpty(req.Description) ? $"📝 توضیحات: {req.Description}\n" : "") +
              "\n━━━━━━━━━━━━━━━━━━━\n" +
              $"💰 مبلغ کل: <b>{req.TotalAmount:N0}</b> تومان\n" +
              (req.FeePercent > 0 ? $"📎 کارمزد: {req.FeePercent:F1}% ({req.FeeAmount:N0} T)\n" : "") +
              $"🕐 تاریخ ثبت: {date:yyyy/MM/dd HH:mm}\n" +
              (req.UpdatedAt.HasValue ? $"🔄 آخرین تغییر: {req.UpdatedAt.Value.ToOffset(TimeSpan.FromHours(3.5)):yyyy/MM/dd HH:mm}\n" : "") +
              (!string.IsNullOrEmpty(req.AdminNote) ? $"\n📋 یادداشت ادمین: <i>{req.AdminNote}</i>\n" : "")
            : $"<b>📄 Request #{req.RequestNumber} Details</b>\n" +
              "━━━━━━━━━━━━━━━━━━━\n\n" +
              $"{statusIcon} Status: <b>{statusLabel}</b>\n\n" +
              $"💱 Type: <b>{txLabel}</b>\n" +
              $"💵 Currency: {flag} <b>{req.Amount:N0}</b> {currName}\n" +
              $"📊 Rate: <b>{req.ProposedRate:N0}</b> IRR\n" +
              $"🚚 Delivery: <b>{deliveryLabel}</b>" +
              (!string.IsNullOrEmpty(req.AccountType)
                  ? $" ({(req.AccountType == "company" ? "Business" : "Personal")})"
                  : "") + "\n" +
              (!string.IsNullOrEmpty(req.Country) ? $"🌍 Country: <b>{req.Country}</b>\n" : "") +
              (!string.IsNullOrEmpty(req.Description) ? $"📝 Note: {req.Description}\n" : "") +
              "\n━━━━━━━━━━━━━━━━━━━\n" +
              $"💰 Total: <b>{req.TotalAmount:N0}</b> IRR\n" +
              (req.FeePercent > 0 ? $"📎 Fee: {req.FeePercent:F1}% ({req.FeeAmount:N0} T)\n" : "") +
              $"🕐 Created: {date:yyyy/MM/dd HH:mm}\n" +
              (req.UpdatedAt.HasValue ? $"🔄 Updated: {req.UpdatedAt.Value.ToOffset(TimeSpan.FromHours(3.5)):yyyy/MM/dd HH:mm}\n" : "") +
              (!string.IsNullOrEmpty(req.AdminNote) ? $"\n📋 Admin note: <i>{req.AdminNote}</i>\n" : "");

        var kb = new List<IReadOnlyList<InlineButton>>
        {
            new[] { new InlineButton(isFa ? "🔙 بازگشت به لیست تبادلات" : "🔙 Back to list", $"exc_hist:p:{year}:{month}:{page}") },
            new[] { new InlineButton(isFa ? "📅 بازگشت به ماه‌ها" : "📅 Back to months", $"exc_hist:y:{year}") },
        };

        await SendOrEditTextAsync(chatId, text, kb, editMessageId, ct).ConfigureAwait(false);
    }

    private async Task HandleExcHistCallback(long chatId, long userId, TelegramUserDto? user, string data, int? editMessageId, CancellationToken ct)
    {
        // exc_hist:years — back to year selector
        if (data == "exc_hist:years")
        {
            await ShowMyExchangesYears(chatId, user, editMessageId, ct).ConfigureAwait(false);
            return;
        }

        // exc_hist:d:ID:YEAR:MONTH:PAGE — show detail of a single request
        if (data.StartsWith("exc_hist:d:"))
        {
            var parts = data["exc_hist:d:".Length..].Split(':');
            if (parts.Length >= 4
                && int.TryParse(parts[0], out var reqId)
                && int.TryParse(parts[1], out var dYear)
                && int.TryParse(parts[2], out var dMonth)
                && int.TryParse(parts[3], out var dPage))
                await ShowMyExchangeDetail(chatId, user, reqId, dYear, dMonth, dPage, editMessageId, ct).ConfigureAwait(false);
            return;
        }

        // exc_hist:y:YEAR — show months for year
        if (data.StartsWith("exc_hist:y:"))
        {
            var yearStr = data["exc_hist:y:".Length..];
            if (int.TryParse(yearStr, out var year))
                await ShowMyExchangesMonths(chatId, user, year, editMessageId, ct).ConfigureAwait(false);
            return;
        }

        // exc_hist:m:YEAR:MONTH — show paginated list page 0
        if (data.StartsWith("exc_hist:m:"))
        {
            var parts = data["exc_hist:m:".Length..].Split(':');
            if (parts.Length >= 2 && int.TryParse(parts[0], out var year) && int.TryParse(parts[1], out var month))
                await ShowMyExchangesList(chatId, user, year, month, 0, editMessageId, ct).ConfigureAwait(false);
            return;
        }

        // exc_hist:p:YEAR:MONTH:PAGE — navigate to specific page
        if (data.StartsWith("exc_hist:p:"))
        {
            var parts = data["exc_hist:p:".Length..].Split(':');
            if (parts.Length >= 3 && int.TryParse(parts[0], out var year) && int.TryParse(parts[1], out var month) && int.TryParse(parts[2], out var page))
                await ShowMyExchangesList(chatId, user, year, month, page, editMessageId, ct).ConfigureAwait(false);
            return;
        }
    }

    private static string GetStatusIcon(string status) => status switch
    {
        "pending_approval" => "🟡",
        "approved" => "🟢",
        "rejected" => "🔴",
        "posted" => "🔵",
        "completed" => "✅",
        "cancelled" => "⚫",
        _ => "⚪"
    };

    private static string GetStatusLabelFa(string status) => status switch
    {
        "pending_approval" => "در انتظار بررسی",
        "approved" => "تایید شده",
        "rejected" => "رد شده",
        "posted" => "منتشر شده",
        "completed" => "تکمیل شده",
        "cancelled" => "لغو شده",
        _ => status
    };

    private static string GetStatusLabelEn(string status) => status switch
    {
        "pending_approval" => "Pending",
        "approved" => "Approved",
        "rejected" => "Rejected",
        "posted" => "Posted",
        "completed" => "Completed",
        "cancelled" => "Cancelled",
        _ => status
    };

    // ═══════════════════════════════════════════════════════════════════
    //  Stage type registry
    // ═══════════════════════════════════════════════════════════════════

    private static readonly HashSet<string> ReplyKeyboardStages = new(StringComparer.OrdinalIgnoreCase)
    {
        "main_menu", "new_request", "student_exchange", "submit_exchange"
    };

    private static bool IsReplyKeyboardStage(string stageKey) =>
        ReplyKeyboardStages.Contains(stageKey);

    /// <summary>Stages that require profile completion (IsRegistered) before entry.</summary>
    private static readonly HashSet<string> VerificationRequiredStages = new(StringComparer.OrdinalIgnoreCase)
    {
        "buy_currency", "sell_currency", "do_exchange"
    };

    private static bool RequiresVerification(string stageKey) =>
        VerificationRequiredStages.Contains(stageKey);

    // ═══════════════════════════════════════════════════════════════════
    //  Reply keyboard button handler
    // ═══════════════════════════════════════════════════════════════════

    private async Task<bool> HandleReplyKeyboardButtonAsync(long chatId, long userId, string text, int? incomingMessageId, bool cleanMode, CancellationToken cancellationToken)
    {
        // Read which reply keyboard stage the user is currently on → try that first
        var currentReplyStage = await _stateStore.GetReplyStageAsync(userId, cancellationToken).ConfigureAwait(false);
        var orderedStages = ReplyKeyboardStages.ToList();
        if (!string.IsNullOrEmpty(currentReplyStage))
        {
            orderedStages.Remove(currentReplyStage);
            orderedStages.Insert(0, currentReplyStage); // prioritize current stage
        }

        foreach (var stageKey in orderedStages)
        {
            var allButtons = await _stageRepo.GetButtonsAsync(stageKey, cancellationToken).ConfigureAwait(false);

            foreach (var btn in allButtons)
            {
                if (!btn.IsEnabled) continue;
                var matchFa = btn.TextFa?.Trim();
                var matchEn = btn.TextEn?.Trim();
                if (!string.Equals(text, matchFa, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(text, matchEn, StringComparison.OrdinalIgnoreCase))
                    continue;

                var targetStage = btn.TargetStageKey;
                if (string.IsNullOrEmpty(targetStage) && !string.IsNullOrEmpty(btn.CallbackData))
                {
                    var cb = btn.CallbackData.Trim();
                    if (cb.StartsWith("stage:", StringComparison.OrdinalIgnoreCase))
                        targetStage = cb["stage:".Length..].Trim();
                }

                if (string.IsNullOrEmpty(targetStage)) return false;

                // Delete user's incoming text message (only in clean mode)
                if (cleanMode)
                    await TryDeleteAsync(chatId, incomingMessageId, cancellationToken).ConfigureAwait(false);

                var oldBotMsgId = await GetOldBotMessageIdAsync(userId, cancellationToken).ConfigureAwait(false);

                // ── Verification gate (from reply-kb) ──────────────
                if (RequiresVerification(targetStage))
                {
                    var user = await _userRepo.GetByTelegramUserIdAsync(userId, cancellationToken).ConfigureAwait(false);
                    if (!string.Equals(user?.KycStatus, "approved", StringComparison.OrdinalIgnoreCase))
                    {
                        var lang = user?.PreferredLanguage ?? "fa";
                        var isFa = lang == "fa";
                        var msg = isFa
                            ? "برای استفاده از این بخش ابتدا باید احراز هویت کنید.\nلطفاً مراحل احراز هویت را تکمیل کنید تا بتوانید از خدمات تبادل ارز استفاده نمایید."
                            : "You need to verify your identity before using this section.\nPlease complete the verification process to access currency exchange services.";
                        var kycLabel = isFa ? "شروع احراز هویت" : "Start Verification";
                        var profileLabel = isFa ? "پروفایل من" : "My Profile";
                        var backLabel = isFa ? "بازگشت" : "Back";
                        var keyboard = new List<IReadOnlyList<InlineButton>>
                        {
                            new[] { new InlineButton(kycLabel, "start_kyc") },
                            new[] { new InlineButton(profileLabel, "stage:profile") },
                            new[] { new InlineButton(backLabel, "stage:submit_exchange") }
                        };
                        if (cleanMode)
                            await TryDeleteAsync(chatId, oldBotMsgId, cancellationToken).ConfigureAwait(false);
                        await _sender.RemoveReplyKeyboardSilentAsync(chatId, cancellationToken).ConfigureAwait(false);
                        await _sender.SendTextMessageWithInlineKeyboardAsync(chatId, msg, keyboard, cancellationToken).ConfigureAwait(false);
                        return true;
                    }

                    // User is verified → start exchange flow
                    if (_exchangeHandler != null)
                    {
                        var txType = targetStage switch
                        {
                            "buy_currency" => "buy",
                            "sell_currency" => "sell",
                            "do_exchange" => "exchange",
                            _ => "ask"
                        };
                        if (cleanMode)
                            await TryDeleteAsync(chatId, oldBotMsgId, cancellationToken).ConfigureAwait(false);
                        await _exchangeHandler.StartExchangeFlow(chatId, userId, txType, cancellationToken).ConfigureAwait(false);
                        return true;
                    }
                }

                if (IsReplyKeyboardStage(targetStage))
                {
                    // Same type (reply-kb → reply-kb): send new + delete old
                    await ShowReplyKeyboardStageAsync(userId, targetStage, null, cleanMode ? oldBotMsgId : null, cancellationToken).ConfigureAwait(false);
                    return true;
                }

                // Type change (reply-kb → inline): delete reply-kb msg, remove keyboard, send new inline
                if (string.Equals(targetStage, "profile", StringComparison.OrdinalIgnoreCase))
                {
                    var user = await _userRepo.GetByTelegramUserIdAsync(userId, cancellationToken).ConfigureAwait(false);
                    var lang = user?.PreferredLanguage ?? "fa";
                    var isFa = lang == "fa";
                    var (profileText, profileKb) = ProfileStateHandler.BuildProfileView(user, isFa);
                    if (cleanMode)
                        await TryDeleteAsync(chatId, oldBotMsgId, cancellationToken).ConfigureAwait(false);
                    await _sender.RemoveReplyKeyboardSilentAsync(chatId, cancellationToken).ConfigureAwait(false);
                    await _sender.SendTextMessageWithInlineKeyboardAsync(chatId, profileText, profileKb, cancellationToken).ConfigureAwait(false);
                    return true;
                }

                // ── Exchange Rates (from reply-kb) ─────────────────
                if (string.Equals(targetStage, "exchange_rates", StringComparison.OrdinalIgnoreCase))
                {
                    var user = await _userRepo.GetByTelegramUserIdAsync(userId, cancellationToken).ConfigureAwait(false);
                    if (cleanMode)
                        await TryDeleteAsync(chatId, oldBotMsgId, cancellationToken).ConfigureAwait(false);
                    // Remove reply keyboard silently before sending inline
                    await _sender.RemoveReplyKeyboardSilentAsync(chatId, cancellationToken).ConfigureAwait(false);
                    await ShowExchangeRates(chatId, user, null, cancellationToken).ConfigureAwait(false);
                    return true;
                }

                // ── My Exchanges (from reply-kb) ───────────────────
                if (string.Equals(targetStage, "my_exchanges", StringComparison.OrdinalIgnoreCase))
                {
                    var user = await _userRepo.GetByTelegramUserIdAsync(userId, cancellationToken).ConfigureAwait(false);
                    if (cleanMode)
                        await TryDeleteAsync(chatId, oldBotMsgId, cancellationToken).ConfigureAwait(false);
                    // Remove reply keyboard silently before sending inline
                    await _sender.RemoveReplyKeyboardSilentAsync(chatId, cancellationToken).ConfigureAwait(false);
                    await ShowMyExchangesYears(chatId, user, null, cancellationToken).ConfigureAwait(false);
                    return true;
                }

                if (cleanMode)
                    await TryDeleteAsync(chatId, oldBotMsgId, cancellationToken).ConfigureAwait(false);
                // Remove reply keyboard silently before showing inline stage
                await _sender.RemoveReplyKeyboardSilentAsync(chatId, cancellationToken).ConfigureAwait(false);
                await ShowStageInlineAsync(userId, targetStage, null, null, cancellationToken).ConfigureAwait(false);
                return true;
            }
        }
        return false;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Helpers
    // ═══════════════════════════════════════════════════════════════════

    private async Task<int?> GetOldBotMessageIdAsync(long userId, CancellationToken cancellationToken)
    {
        if (_msgStateRepo == null) return null;
        try
        {
            var msgState = await _msgStateRepo.GetUserMessageStateAsync(userId, cancellationToken).ConfigureAwait(false);
            return msgState?.LastBotTelegramMessageId is > 0 ? (int)msgState.LastBotTelegramMessageId : null;
        }
        catch { return null; }
    }

    private async Task TryDeleteAsync(long chatId, int? messageId, CancellationToken cancellationToken)
    {
        if (!messageId.HasValue) return;
        try { await _sender.DeleteMessageAsync(chatId, messageId.Value, cancellationToken).ConfigureAwait(false); }
        catch { /* swallow */ }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Stage renderers
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Show a stage with reply keyboard.
    /// If editMessageId is provided → edit text in place + silently update keyboard (phantom).
    /// If editMessageId is null → send new message with text + keyboard.
    /// </summary>
    private async Task ShowReplyKeyboardStageAsync(long userId, string stageKey, string? langOverride, int? editMessageId, CancellationToken cancellationToken)
    {
        var user = await _userRepo.GetByTelegramUserIdAsync(userId, cancellationToken).ConfigureAwait(false);
        var lang = langOverride ?? user?.PreferredLanguage ?? "fa";
        var isFa = lang == "fa";

        var stage = await _stageRepo.GetByKeyAsync(stageKey, cancellationToken).ConfigureAwait(false);
        var text = stage != null && stage.IsEnabled
            ? (isFa ? (stage.TextFa ?? stage.TextEn ?? stageKey) : (stage.TextEn ?? stage.TextFa ?? stageKey))
            : stageKey;

        var allButtons = await _stageRepo.GetButtonsAsync(stageKey, cancellationToken).ConfigureAwait(false);
        var userPerms = await _permRepo.GetUserPermissionsAsync(userId, cancellationToken).ConfigureAwait(false);
        var permSet = new HashSet<string>(userPerms, StringComparer.OrdinalIgnoreCase);

        var visibleButtons = new List<BotStageButtonDto>();
        foreach (var btn in allButtons)
        {
            if (!btn.IsEnabled) continue;
            if (!string.IsNullOrEmpty(btn.RequiredPermission) && !permSet.Contains(btn.RequiredPermission)) continue;
            visibleButtons.Add(btn);
        }

        var keyboard = new List<IReadOnlyList<string>>();
        foreach (var row in visibleButtons.GroupBy(b => b.Row).OrderBy(g => g.Key))
        {
            var rowTexts = new List<string>();
            foreach (var btn in row.OrderBy(b => b.Column))
            {
                var btnText = isFa ? (btn.TextFa ?? btn.TextEn ?? "?") : (btn.TextEn ?? btn.TextFa ?? "?");
                rowTexts.Add(btnText);
            }
            if (rowTexts.Count > 0)
                keyboard.Add(rowTexts);
        }

        // Track current reply stage for back-button routing
        await _stateStore.SetReplyStageAsync(userId, stageKey, cancellationToken).ConfigureAwait(false);

        // Send new message with reply keyboard, then delete old
        await _sender.SendTextMessageWithReplyKeyboardAsync(userId, text, keyboard, cancellationToken).ConfigureAwait(false);
        if (editMessageId.HasValue)
            await TryDeleteAsync(userId, editMessageId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Show a stage with inline keyboard. If editMessageId is provided, edits in-place.
    /// </summary>
    private async Task ShowStageInlineAsync(long userId, string stageKey, int? editMessageId, string? langOverride, CancellationToken cancellationToken, bool? cleanChatOverride = null)
    {
        var user = await _userRepo.GetByTelegramUserIdAsync(userId, cancellationToken).ConfigureAwait(false);
        var lang = langOverride ?? user?.PreferredLanguage ?? "fa";
        var isFa = lang == "fa";

        var stage = await _stageRepo.GetByKeyAsync(stageKey, cancellationToken).ConfigureAwait(false);
        if (stage == null)
        {
            var notFound = isFa ? "این بخش یافت نشد." : "Section not found.";
            await SendOrEditTextAsync(userId, notFound, Array.Empty<IReadOnlyList<InlineButton>>(), editMessageId, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!stage.IsEnabled)
        {
            var disabled = isFa ? "این بخش در حال حاضر غیرفعال است." : "This section is currently disabled.";
            await SendOrEditTextAsync(userId, disabled, Array.Empty<IReadOnlyList<InlineButton>>(), editMessageId, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!string.IsNullOrEmpty(stage.RequiredPermission))
        {
            var hasAccess = await _permRepo.UserHasPermissionAsync(userId, stage.RequiredPermission, cancellationToken).ConfigureAwait(false);
            if (!hasAccess)
            {
                var denied = isFa ? "شما دسترسی به این بخش ندارید." : "You don't have access to this section.";
                await SendOrEditTextAsync(userId, denied, Array.Empty<IReadOnlyList<InlineButton>>(), editMessageId, cancellationToken).ConfigureAwait(false);
                return;
            }
        }

        var text = isFa ? (stage.TextFa ?? stage.TextEn ?? stageKey) : (stage.TextEn ?? stage.TextFa ?? stageKey);

        var allButtons = await _stageRepo.GetButtonsAsync(stageKey, cancellationToken).ConfigureAwait(false);
        var userPerms = await _permRepo.GetUserPermissionsAsync(userId, cancellationToken).ConfigureAwait(false);
        var permSet = new HashSet<string>(userPerms, StringComparer.OrdinalIgnoreCase);

        var visibleButtons = new List<BotStageButtonDto>();
        foreach (var btn in allButtons)
        {
            if (!btn.IsEnabled) continue;
            if (!string.IsNullOrEmpty(btn.RequiredPermission) && !permSet.Contains(btn.RequiredPermission)) continue;
            visibleButtons.Add(btn);
        }

        // Resolve clean-chat mode for dynamic toggle button label
        bool? cleanChat = cleanChatOverride;
        if (cleanChat == null && visibleButtons.Any(b => b.CallbackData == "toggle:clean_chat"))
        {
            cleanChat = user?.CleanChatMode ?? true;
        }

        var keyboard = new List<IReadOnlyList<InlineButton>>();
        foreach (var row in visibleButtons.GroupBy(b => b.Row).OrderBy(g => g.Key))
        {
            var rowButtons = new List<InlineButton>();
            foreach (var btn in row.OrderBy(b => b.Column))
            {
                var btnText = isFa ? (btn.TextFa ?? btn.TextEn ?? "?") : (btn.TextEn ?? btn.TextFa ?? "?");
                var callbackData = btn.CallbackData;
                if (string.IsNullOrEmpty(callbackData) && !string.IsNullOrEmpty(btn.TargetStageKey))
                    callbackData = $"stage:{btn.TargetStageKey}";

                // Dynamic toggle label: append current state
                if (callbackData == "toggle:clean_chat" && cleanChat.HasValue)
                {
                    var stateLabel = cleanChat.Value
                        ? (isFa ? "فعال" : "ON")
                        : (isFa ? "غیرفعال" : "OFF");
                    btnText = $"{btnText}: {stateLabel}";
                }

                if (btn.ButtonType == "url" && !string.IsNullOrEmpty(btn.Url))
                    rowButtons.Add(new InlineButton(btnText, null, btn.Url));
                else
                    rowButtons.Add(new InlineButton(btnText, callbackData ?? "noop"));
            }
            if (rowButtons.Count > 0)
                keyboard.Add(rowButtons);
        }

        // Auto back-button
        if (!string.IsNullOrEmpty(stage.ParentStageKey))
        {
            var hasBack = visibleButtons.Any(b =>
                b.TargetStageKey == stage.ParentStageKey ||
                b.CallbackData == $"stage:{stage.ParentStageKey}");
            if (!hasBack)
            {
                var backLabel = isFa ? "بازگشت" : "Back";
                keyboard.Add(new[] { new InlineButton(backLabel, $"stage:{stage.ParentStageKey}") });
            }
        }

        await SendOrEditTextAsync(userId, text, keyboard, editMessageId, cancellationToken).ConfigureAwait(false);
    }

    private async Task SendOrEditTextAsync(long chatId, string text, IReadOnlyList<IReadOnlyList<InlineButton>> keyboard, int? editMessageId, CancellationToken cancellationToken)
    {
        if (editMessageId.HasValue)
        {
            try
            {
                await _sender.EditMessageTextWithInlineKeyboardAsync(chatId, editMessageId.Value, text, keyboard, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch
            {
                // Edit failed — fall back to sending new message
            }
        }
        await _sender.SendTextMessageWithInlineKeyboardAsync(chatId, text, keyboard, cancellationToken).ConfigureAwait(false);
    }
}
