using AbroadQs.Bot.Contracts;

namespace AbroadQs.Bot.Modules.Common;

/// <summary>
/// Multi-step KYC flow. Each step cleans up its messages immediately.
/// No "..." dots are ever shown. Email OTP is sent for verification.
/// Cancel deletes ALL tracked KYC flow messages.
/// </summary>
public sealed class KycStateHandler : IUpdateHandler
{
    private readonly IResponseSender _sender;
    private readonly ITelegramUserRepository _userRepo;
    private readonly IUserConversationStateStore _stateStore;
    private readonly ISettingsRepository? _settingsRepo;
    private readonly ISmsService? _smsService;
    private readonly IEmailService? _emailService;
    private readonly IUserMessageStateRepository? _msgStateRepo;

    private const string SamplePhotoPath = "wwwroot/kyc_sample_photo.png";

    private const string CbCancel      = "cancel_kyc";
    private const string CbSkipEmail   = "skip_email";
    private const string CbSkipCountry = "skip_country";
    private const string CbStartKycFix = "start_kyc_fix";
    private const string CbPhoneManualContinue = "phone_manual_continue";

    public KycStateHandler(
        IResponseSender sender,
        ITelegramUserRepository userRepo,
        IUserConversationStateStore stateStore,
        ISettingsRepository? settingsRepo = null,
        ISmsService? smsService = null,
        IEmailService? emailService = null,
        IUserMessageStateRepository? msgStateRepo = null)
    {
        _sender = sender;
        _userRepo = userRepo;
        _stateStore = stateStore;
        _settingsRepo = settingsRepo;
        _smsService = smsService;
        _emailService = emailService;
        _msgStateRepo = msgStateRepo;
    }

    public string? Command => null;

    public bool CanHandle(BotUpdateContext context)
    {
        if (context.UserId == null) return false;
        if (context.IsCallbackQuery)
        {
            var cb = context.MessageText?.Trim() ?? "";
            return cb.StartsWith("country:", StringComparison.OrdinalIgnoreCase)
                || cb == CbCancel || cb == CbSkipEmail
                || cb == CbSkipCountry || cb == CbStartKycFix
                || cb == CbPhoneManualContinue;
        }
        return !string.IsNullOrEmpty(context.MessageText) || context.HasContact || context.HasPhoto;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Handle
    // ═══════════════════════════════════════════════════════════════════

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
                if (string.IsNullOrEmpty(st) || !st.StartsWith("kyc_step_")) return false;
                var u = await SafeGetUser(userId, ct);
                await CancelKycAsync(chatId, userId, u, context.CallbackMessageId, ct);
                return true;
            }

            if (cb == CbSkipEmail)
            {
                var st = await _stateStore.GetStateAsync(userId, ct).ConfigureAwait(false);
                if (st != "kyc_step_email") return false;
                var u = await SafeGetUser(userId, ct);
                // Delete the email prompt message
                await SafeDelete(chatId, context.CallbackMessageId, ct);
                // Skip → country
                var fix = GetNextFixStep(u?.KycRejectionData, "email");
                if (fix != null) { await GoToStep(chatId, userId, fix, u, ct); return true; }
                await GoToStep(chatId, userId, "kyc_step_country", u, ct);
                return true;
            }

            if (cb == CbSkipCountry)
            {
                var st = await _stateStore.GetStateAsync(userId, ct).ConfigureAwait(false);
                if (st != "kyc_step_country") return false;
                var u = await SafeGetUser(userId, ct);
                await SafeDelete(chatId, context.CallbackMessageId, ct);
                await GoToStep(chatId, userId, "kyc_step_photo", u, ct);
                return true;
            }

            if (cb == CbStartKycFix)
            {
                var u = await SafeGetUser(userId, ct);
                await SafeDelete(chatId, context.CallbackMessageId, ct);
                var next = GetNextRejectedStep(u?.KycRejectionData) ?? "kyc_step_name";
                await GoToStep(chatId, userId, next, u, ct);
                return true;
            }

            if (cb == CbPhoneManualContinue)
            {
                var st = await _stateStore.GetStateAsync(userId, ct).ConfigureAwait(false);
                if (st != "kyc_step_phone_manual") return false;
                var u = await SafeGetUser(userId, ct);
                await SafeDelete(chatId, context.CallbackMessageId, ct);
                var fix = GetNextFixStep(u?.KycRejectionData, "phone");
                if (fix != null) { await GoToStep(chatId, userId, fix, u, ct); return true; }
                await GoToStep(chatId, userId, "kyc_step_email", u, ct);
                return true;
            }

            if (cb.StartsWith("country:"))
            {
                var st = await _stateStore.GetStateAsync(userId, ct).ConfigureAwait(false);
                if (st != "kyc_step_country") return false;
                var code = cb["country:".Length..].Trim();
                var u = await SafeGetUser(userId, ct);
                var isFa = IsFa(u);

                if (code == "other")
                {
                    await _stateStore.SetStateAsync(userId, "kyc_step_country_text", ct).ConfigureAwait(false);
                    await SafeDelete(chatId, context.CallbackMessageId, ct);
                    await SafeSendInline(chatId,
                        isFa ? "لطفا نام کشور محل سکونت خود را تایپ کنید:" : "Please type your country of residence:",
                        CancelRow(isFa), ct);
                    return true;
                }

                await _userRepo.SetCountryAsync(userId, GetCountryName(code, isFa), ct).ConfigureAwait(false);
                await SafeDelete(chatId, context.CallbackMessageId, ct);
                await GoToStep(chatId, userId, "kyc_step_photo", u, ct);
                return true;
            }

            return false;
        }

        // ── Text / Contact / Photo ───────────────────────────────────
        var state = await _stateStore.GetStateAsync(userId, ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(state) || !state.StartsWith("kyc_step_")) return false;

        var currentUser = await SafeGetUser(userId, ct);
        var isFaLang = IsFa(currentUser);
        var prevBotMsgId = await GetLastBotMsgId(userId, ct);

        // ── Guard: prevent re-submission while pending review ────────
        if (currentUser?.KycStatus?.Equals("pending_review", StringComparison.OrdinalIgnoreCase) == true)
        {
            await SafeSendInline(chatId,
                isFaLang
                    ? "درخواست احراز هویت شما قبلا ثبت شده و در حال بررسی است. لطفا منتظر بمانید."
                    : "Your verification request is already under review. Please wait.",
                new List<IReadOnlyList<InlineButton>>
                {
                    new[] { new InlineButton(isFaLang ? "بازگشت به منوی اصلی" : "Back to Main Menu", "stage:main_menu") }
                }, ct);
            await _stateStore.ClearStateAsync(userId, ct).ConfigureAwait(false);
            return true;
        }

        // ── Step: Name ───────────────────────────────────────────────
        if (state == "kyc_step_name")
        {
            var text = context.MessageText?.Trim();
            if (string.IsNullOrEmpty(text))
            {
                await CleanUserMsg(chatId, context.IncomingMessageId, ct);
                return true;
            }

            var parts = text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            var first = parts.Length > 0 ? parts[0].Trim() : null;
            var last = parts.Length > 1 ? parts[1].Trim() : null;

            if (string.IsNullOrEmpty(first) || string.IsNullOrEmpty(last))
            {
                await CleanUserMsg(chatId, context.IncomingMessageId, ct);
                var msg = isFaLang
                    ? "لطفا نام و نام خانوادگی خود را در یک خط وارد کنید.\nمثال: <b>علی احمدی</b>"
                    : "Please enter both first and last name.\nExample: <b>John Smith</b>";
                await EditOrReplace(chatId, prevBotMsgId, msg, CancelRow(isFaLang), ct);
                return true;
            }

            await _userRepo.UpdateProfileAsync(userId, first, last, null, ct).ConfigureAwait(false);
            // Clean current step
            await CleanUserMsg(chatId, context.IncomingMessageId, ct);
            await SafeDelete(chatId, prevBotMsgId, ct);

            var fix = GetNextFixStep(currentUser?.KycRejectionData, "name");
            if (fix != null) { await GoToStep(chatId, userId, fix, currentUser, ct); return true; }
            await GoToStep(chatId, userId, "kyc_step_phone", currentUser, ct);
            return true;
        }

        // ── Step: Phone ──────────────────────────────────────────────
        if (state == "kyc_step_phone")
        {
            var txt = context.MessageText?.Trim();
            if (!string.IsNullOrEmpty(txt) && IsCancelText(txt))
            {
                await CancelKycAsync(chatId, userId, currentUser, null, ct);
                return true;
            }

            if (!context.HasContact)
            {
                await CleanUserMsg(chatId, context.IncomingMessageId, ct);
                return true;
            }

            var phone = context.ContactPhoneNumber!;
            await _userRepo.SetPhoneNumberAsync(userId, phone, ct).ConfigureAwait(false);

            // Check if Iranian phone number
            bool isIranianPhone = phone.StartsWith("+98") || phone.StartsWith("98") || phone.StartsWith("09");

            if (!isIranianPhone)
            {
                // Non-Iranian: manual verification via support
                var verifyCode = new Random().Next(10000, 99999).ToString();
                var supportUsername = _settingsRepo != null
                    ? (await _settingsRepo.GetValueAsync("SupportTelegramUsername", ct).ConfigureAwait(false) ?? "support")
                    : "support";

                await CleanUserMsg(chatId, context.IncomingMessageId, ct);
                await SafeDelete(chatId, prevBotMsgId, ct);
                try { await _sender.RemoveReplyKeyboardSilentAsync(chatId, ct).ConfigureAwait(false); } catch { }

                var infoBlock = $"\u06a9\u062f \u062a\u0627\u06cc\u06cc\u062f: {verifyCode}\n\u0622\u06cc\u062f\u06cc \u062a\u0644\u06af\u0631\u0627\u0645: {userId}\n\u0634\u0645\u0627\u0631\u0647 \u062a\u0645\u0627\u0633: {phone}";
                var msg = isFaLang
                    ? $"\u0628\u0627 \u062a\u0648\u062c\u0647 \u0628\u0647 \u0627\u06cc\u0646\u06a9\u0647 \u0634\u0645\u0627\u0631\u0647 \u0634\u0645\u0627 \u0627\u06cc\u0631\u0627\u0646\u06cc \u0646\u06cc\u0633\u062a\u060c \u0627\u0645\u06a9\u0627\u0646 \u0627\u0631\u0633\u0627\u0644 \u06a9\u062f \u062a\u0627\u06cc\u06cc\u062f \u067e\u06cc\u0627\u0645\u06a9\u06cc \u0648\u062c\u0648\u062f \u0646\u062f\u0627\u0631\u062f.\n\n\u0628\u0631\u0627\u06cc \u062a\u0627\u06cc\u06cc\u062f \u0634\u0645\u0627\u0631\u0647 \u062a\u0645\u0627\u0633\u060c \u0644\u0637\u0641\u0627 \u0627\u0637\u0644\u0627\u0639\u0627\u062a \u0632\u06cc\u0631 \u0631\u0627 \u0628\u0647 \u067e\u0634\u062a\u06cc\u0628\u0627\u0646\u06cc \u0627\u0631\u0633\u0627\u0644 \u06a9\u0646\u06cc\u062f:\n\n<code>{infoBlock}</code>\n\n\u0645\u06cc\u200c\u062a\u0648\u0627\u0646\u06cc\u062f \u0628\u0627 \u0632\u062f\u0646 \u062f\u06a9\u0645\u0647 \u0632\u06cc\u0631 \u0627\u0637\u0644\u0627\u0639\u0627\u062a \u0631\u0627 \u0645\u0633\u062a\u0642\u06cc\u0645\u0627 \u0627\u0631\u0633\u0627\u0644 \u06a9\u0646\u06cc\u062f:"
                    : $"SMS verification is not available for non-Iranian numbers.\n\nPlease send the following info to support:\n\n<code>{infoBlock}</code>\n\nYou can send directly using the button below:";

                var prefilledText = Uri.EscapeDataString(infoBlock);
                var deepLink = $"https://t.me/{supportUsername}?text={prefilledText}";

                await _stateStore.SetStateAsync(userId, "kyc_step_phone_manual", ct).ConfigureAwait(false);

                var kb = new List<IReadOnlyList<InlineButton>>
                {
                    new[] { new InlineButton(isFaLang ? "\u0627\u0631\u0633\u0627\u0644 \u0628\u0647 \u067e\u0634\u062a\u06cc\u0628\u0627\u0646\u06cc" : "Send to Support", null, deepLink) },
                    new[] { new InlineButton(isFaLang ? "\u0627\u062f\u0627\u0645\u0647 \u0645\u0631\u0627\u062d\u0644" : "Continue", CbPhoneManualContinue) },
                    new[] { new InlineButton(isFaLang ? "\u0644\u063a\u0648 \u0627\u062d\u0631\u0627\u0632 \u0647\u0648\u06cc\u062a" : "Cancel", CbCancel) },
                };

                await SafeSendInline(chatId, msg, kb, ct);
                return true;
            }

            // Iranian phone: SMS OTP flow
            var otp = new Random().Next(10000, 99999).ToString();
            await _stateStore.SetStateAsync(userId, $"kyc_step_otp:{otp}", ct).ConfigureAwait(false);

            if (_smsService != null)
            {
                bool sent;
                try
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    cts.CancelAfter(TimeSpan.FromSeconds(15));
                    sent = await _smsService.SendVerificationCodeAsync(phone, otp, cts.Token).ConfigureAwait(false);
                }
                catch { sent = false; }

                if (!sent)
                {
                    await _stateStore.SetStateAsync(userId, "kyc_step_phone", ct).ConfigureAwait(false);
                    await CleanUserMsg(chatId, context.IncomingMessageId, ct);
                    var errMsg = isFaLang
                        ? "\u062e\u0637\u0627 \u062f\u0631 \u0627\u0631\u0633\u0627\u0644 \u06a9\u062f \u062a\u0627\u06cc\u06cc\u062f. \u0644\u0637\u0641\u0627 \u062f\u0648\u0628\u0627\u0631\u0647 \u0634\u0645\u0627\u0631\u0647 \u062a\u0644\u0641\u0646 \u062e\u0648\u062f \u0631\u0627 \u0628\u0647 \u0627\u0634\u062a\u0631\u0627\u06a9 \u0628\u06af\u0630\u0627\u0631\u06cc\u062f."
                        : "Error sending code. Please share your phone number again.";
                    await SafeSendContactRequest(chatId, errMsg,
                        isFaLang ? "\u0627\u0634\u062a\u0631\u0627\u06a9\u200c\u06af\u0630\u0627\u0631\u06cc \u0634\u0645\u0627\u0631\u0647 \u062a\u0644\u0641\u0646" : "Share Phone Number",
                        isFaLang ? "\u0644\u063a\u0648 \u0627\u062d\u0631\u0627\u0632 \u0647\u0648\u06cc\u062a" : "Cancel", ct);
                    return true;
                }
            }

            // Clean previous step
            await CleanUserMsg(chatId, context.IncomingMessageId, ct);
            await SafeDelete(chatId, prevBotMsgId, ct);
            try { await _sender.RemoveReplyKeyboardSilentAsync(chatId, ct).ConfigureAwait(false); } catch { }

            var masked = MaskPhone(phone);
            await SafeSendInline(chatId,
                isFaLang
                    ? $"\u06a9\u062f \u062a\u0627\u06cc\u06cc\u062f \u0628\u0647 \u0634\u0645\u0627\u0631\u0647 <b>{masked}</b> \u0627\u0631\u0633\u0627\u0644 \u0634\u062f.\n\u0644\u0637\u0641\u0627 \u06a9\u062f 5 \u0631\u0642\u0645\u06cc \u0631\u0627 \u0648\u0627\u0631\u062f \u06a9\u0646\u06cc\u062f:"
                    : $"Code sent to <b>{masked}</b>.\nPlease enter the 5-digit code:",
                CancelRow(isFaLang), ct);
            return true;
        }

        // ── Step: Phone Manual (waiting for support verification) ────
        if (state == "kyc_step_phone_manual")
        {
            await CleanUserMsg(chatId, context.IncomingMessageId, ct);
            return true;
        }

        // ── Step: SMS OTP ────────────────────────────────────────────
        if (state.StartsWith("kyc_step_otp:"))
        {
            var expected = state["kyc_step_otp:".Length..];
            var entered = context.MessageText?.Trim();

            await CleanUserMsg(chatId, context.IncomingMessageId, ct);

            if (string.IsNullOrEmpty(entered) || entered != expected)
            {
                var msg = (entered == null || entered.Length == 0)
                    ? (isFaLang ? "لطفا کد تایید 5 رقمی را وارد کنید:" : "Please enter the 5-digit code:")
                    : (isFaLang ? "کد وارد شده صحیح نیست. لطفا دوباره تلاش کنید:" : "Incorrect code. Please try again:");
                await EditOrReplace(chatId, prevBotMsgId, msg, CancelRow(isFaLang), ct);
                return true;
            }

            // OTP correct
            await SafeDelete(chatId, prevBotMsgId, ct);
            var fix = GetNextFixStep(currentUser?.KycRejectionData, "phone");
            if (fix != null) { await GoToStep(chatId, userId, fix, currentUser, ct); return true; }
            await GoToStep(chatId, userId, "kyc_step_email", currentUser, ct);
            return true;
        }

        // ── Step: Email (collect + send OTP) ─────────────────────────
        if (state == "kyc_step_email")
        {
            var email = context.MessageText?.Trim();

            if (string.IsNullOrEmpty(email) || !email.Contains('@') || !email.Contains('.'))
            {
                await CleanUserMsg(chatId, context.IncomingMessageId, ct);
                var msg = isFaLang
                    ? "لطفا یک آدرس ایمیل معتبر وارد کنید:\nمثال: <b>you@example.com</b>"
                    : "Please enter a valid email address:\nExample: <b>you@example.com</b>";
                await EditOrReplace(chatId, prevBotMsgId, msg, EmailButtons(isFaLang), ct);
                return true;
            }

            // Show "sending..." feedback BEFORE attempting to send
            await CleanUserMsg(chatId, context.IncomingMessageId, ct);
            var sendingMsg = isFaLang
                ? $"در حال ارسال کد تایید به <b>{email}</b> ..."
                : $"Sending verification code to <b>{email}</b> ...";
            await EditOrReplace(chatId, prevBotMsgId, sendingMsg, new List<IReadOnlyList<InlineButton>>(), ct);

            await _userRepo.SetEmailAsync(userId, email, ct).ConfigureAwait(false);

            // Generate and send email OTP
            var otp = new Random().Next(10000, 99999).ToString();
            await _stateStore.SetStateAsync(userId, $"kyc_step_email_otp:{otp}", ct).ConfigureAwait(false);

            if (_emailService != null)
            {
                bool sent;
                try
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    cts.CancelAfter(TimeSpan.FromSeconds(20));
                    sent = await _emailService.SendVerificationCodeAsync(email, otp, cts.Token).ConfigureAwait(false);
                }
                catch { sent = false; }

                if (!sent)
                {
                    await _stateStore.SetStateAsync(userId, "kyc_step_email", ct).ConfigureAwait(false);
                    var errMsg = isFaLang
                        ? "خطا در ارسال کد تایید ایمیل. لطفا دوباره ایمیل خود را وارد کنید."
                        : "Error sending email verification code. Please enter your email again.";
                    await EditOrReplace(chatId, prevBotMsgId, errMsg, EmailButtons(isFaLang), ct);
                    return true;
                }
            }

            await SafeDelete(chatId, prevBotMsgId, ct);
            var maskedEmail = MaskEmail(email);
            await SafeSendInline(chatId,
                isFaLang
                    ? $"کد تایید به ایمیل <b>{maskedEmail}</b> ارسال شد.\nلطفا کد ۵ رقمی را وارد کنید:"
                    : $"Verification code sent to <b>{maskedEmail}</b>.\nPlease enter the 5-digit code:",
                CancelRow(isFaLang), ct);
            return true;
        }

        // ── Step: Email OTP verification ─────────────────────────────
        if (state.StartsWith("kyc_step_email_otp:"))
        {
            var expected = state["kyc_step_email_otp:".Length..];
            var entered = context.MessageText?.Trim();

            await CleanUserMsg(chatId, context.IncomingMessageId, ct);

            if (string.IsNullOrEmpty(entered) || entered != expected)
            {
                var msg2 = (entered == null || entered.Length == 0)
                    ? (isFaLang ? "لطفا کد تایید ایمیل ۵ رقمی را وارد کنید:" : "Please enter the 5-digit email code:")
                    : (isFaLang ? "کد وارد شده صحیح نیست. لطفا دوباره تلاش کنید:" : "Incorrect code. Please try again:");
                await EditOrReplace(chatId, prevBotMsgId, msg2, CancelRow(isFaLang), ct);
                return true;
            }

            // Email verified
            await _userRepo.SetEmailVerifiedAsync(userId, ct).ConfigureAwait(false);
            await SafeDelete(chatId, prevBotMsgId, ct);

            var fix = GetNextFixStep(currentUser?.KycRejectionData, "email");
            if (fix != null) { await GoToStep(chatId, userId, fix, currentUser, ct); return true; }
            await GoToStep(chatId, userId, "kyc_step_country", currentUser, ct);
            return true;
        }

        // ── Step: Country text ("Other") ─────────────────────────────
        if (state == "kyc_step_country_text")
        {
            var country = context.MessageText?.Trim();
            await CleanUserMsg(chatId, context.IncomingMessageId, ct);

            if (string.IsNullOrEmpty(country))
            {
                return true;
            }

            await _userRepo.SetCountryAsync(userId, country, ct).ConfigureAwait(false);
            await SafeDelete(chatId, prevBotMsgId, ct);
            await GoToStep(chatId, userId, "kyc_step_photo", currentUser, ct);
            return true;
        }

        // ── Step: Photo ──────────────────────────────────────────────
        if (state == "kyc_step_photo")
        {
            if (!context.HasPhoto)
            {
                await CleanUserMsg(chatId, context.IncomingMessageId, ct);
                return true;
            }

            var photoFileId = context.PhotoFileId!;
            await _userRepo.SetVerifiedAsync(userId, photoFileId, ct).ConfigureAwait(false);
            await _userRepo.SetKycStatusAsync(userId, "pending_review", null, ct).ConfigureAwait(false);
            await _stateStore.ClearStateAsync(userId, ct).ConfigureAwait(false);

            // Clean this step's messages
            await CleanUserMsg(chatId, context.IncomingMessageId, ct);
            await SafeDelete(chatId, prevBotMsgId, ct);
            // Also try to delete the sample photo (it was the message before the prompt)
            if (prevBotMsgId.HasValue)
                await SafeDelete(chatId, prevBotMsgId.Value - 1, ct); // best effort: sample photo is msg before prompt

            var msg = isFaLang
                ? "مدارک شما ارسال شد و در انتظار بررسی است.\nپس از بررسی، نتیجه به شما اطلاع داده خواهد شد."
                : "Your documents have been submitted for review.\nYou will be notified once the review is complete.";
            await SafeSendInline(chatId, msg, new List<IReadOnlyList<InlineButton>>
            {
                new[] { new InlineButton(isFaLang ? "بازگشت به منوی اصلی" : "Back to Main Menu", "stage:main_menu") }
            }, ct);
            return true;
        }

        return false;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  GoToStep — transition to any KYC step and show its prompt
    // ═══════════════════════════════════════════════════════════════════

    private async Task GoToStep(long chatId, long userId, string step, TelegramUserDto? user, CancellationToken ct)
    {
        var isFa = IsFa(user);
        await _stateStore.SetStateAsync(userId, step, ct).ConfigureAwait(false);

        switch (step)
        {
            case "kyc_step_name":
                await SafeSendInline(chatId,
                    isFa ? "لطفا نام و نام خانوادگی خود را در یک خط وارد کنید:\nمثال: <b>علی احمدی</b>"
                         : "Please enter your first and last name:\nExample: <b>John Smith</b>",
                    CancelRow(isFa), ct);
                break;

            case "kyc_step_phone":
                await SafeSendContactRequest(chatId,
                    isFa ? "لطفا شماره تلفن خود را با زدن دکمه زیر به اشتراک بگذارید:"
                         : "Please share your phone number:",
                    isFa ? "اشتراک‌گذاری شماره تلفن" : "Share Phone Number",
                    isFa ? "لغو احراز هویت" : "Cancel", ct);
                break;

            case "kyc_step_email":
                await SafeSendInline(chatId,
                    isFa ? "لطفا آدرس ایمیل خود را وارد کنید:\n(می‌توانید این مرحله را رد کنید)"
                         : "Please enter your email address:\n(You can skip this step)",
                    EmailButtons(isFa), ct);
                break;

            case "kyc_step_country":
                await SendCountrySelection(chatId, isFa, ct);
                break;

            case "kyc_step_photo":
                await SendPhotoStep(chatId, userId, isFa, ct);
                break;
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Country selection
    // ═══════════════════════════════════════════════════════════════════

    private async Task SendCountrySelection(long chatId, bool isFa, CancellationToken ct)
    {
        var msg = isFa
            ? "لطفا کشور محل سکونت خود را انتخاب کنید:\n(می‌توانید این مرحله را رد کنید)"
            : "Select your country of residence:\n(You can skip this step)";

        var countries = new (string code, string fa, string en)[]
        {
            ("ir","ایران","Iran"), ("tr","ترکیه","Turkey"), ("de","آلمان","Germany"),
            ("ca","کانادا","Canada"), ("au","استرالیا","Australia"), ("gb","انگلستان","UK"),
            ("fr","فرانسه","France"), ("nl","هلند","Netherlands"), ("at","اتریش","Austria"),
            ("it","ایتالیا","Italy"), ("se","سوئد","Sweden"), ("other","سایر","Other"),
        };

        var kb = new List<IReadOnlyList<InlineButton>>();
        for (int i = 0; i < countries.Length; i += 3)
        {
            var row = new List<InlineButton>();
            for (int j = i; j < Math.Min(i + 3, countries.Length); j++)
                row.Add(new InlineButton(isFa ? countries[j].fa : countries[j].en, $"country:{countries[j].code}"));
            kb.Add(row);
        }
        kb.Add(new[] { new InlineButton(isFa ? "رد کردن" : "Skip", CbSkipCountry) });
        kb.Add(new[] { new InlineButton(isFa ? "لغو" : "Cancel", CbCancel) });

        await SafeSendInline(chatId, msg, kb, ct);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Photo step
    // ═══════════════════════════════════════════════════════════════════

    private async Task SendPhotoStep(long chatId, long userId, bool isFa, CancellationToken ct)
    {
        // Check partial fix
        var u = await SafeGetUser(userId, ct);
        var fix = GetNextFixStep(u?.KycRejectionData, "country");
        if (fix != null) { await GoToStep(chatId, userId, fix, u, ct); return; }

        await _stateStore.SetStateAsync(userId, "kyc_step_photo", ct).ConfigureAwait(false);

        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, SamplePhotoPath);
            if (File.Exists(path))
                await _sender.SendPhotoAsync(chatId, path, isFa ? "نمونه عکس تایید هویت" : "Sample verification photo", ct).ConfigureAwait(false);
        }
        catch { }

        await SafeSendInline(chatId,
            isFa ? "لطفا یک عکس سلفی از خود ارسال کنید که در آن:\n- یک تکه کاغذ در کنار صورت شما باشد\n- روی کاغذ عبارت <b>AbroadQs</b> نوشته شده باشد"
                 : "Please send a selfie with a paper next to your face\nthat has <b>AbroadQs</b> written on it.",
            CancelRow(isFa), ct);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Cancel
    // ═══════════════════════════════════════════════════════════════════

    private async Task CancelKycAsync(long chatId, long userId, TelegramUserDto? user, int? triggerMsgId, CancellationToken ct)
    {
        var isFa = IsFa(user);
        await _stateStore.ClearStateAsync(userId, ct).ConfigureAwait(false);

        // Delete the message that triggered cancel (inline button message)
        await SafeDelete(chatId, triggerMsgId, ct);

        // Delete ALL tracked flow messages (bot + user messages from all KYC steps)
        try
        {
            var flowIds = await _stateStore.GetAndClearFlowMessageIdsAsync(userId, ct).ConfigureAwait(false);
            foreach (var id in flowIds)
                await SafeDelete(chatId, id, ct);
        }
        catch { /* best effort */ }

        // Also delete the last bot message if not already covered
        var lastBotMsg = await GetLastBotMsgId(userId, ct);
        if (lastBotMsg.HasValue && lastBotMsg != triggerMsgId)
            await SafeDelete(chatId, lastBotMsg, ct);

        // Remove reply keyboard silently
        try { await _sender.RemoveReplyKeyboardSilentAsync(chatId, ct).ConfigureAwait(false); } catch { }

        await SafeSendInline(chatId,
            isFa ? "فرایند احراز هویت لغو شد.\nهر زمان که آماده بودید، از بخش پروفایل اقدام کنید."
                 : "Verification cancelled.\nYou can restart from Profile anytime.",
            new List<IReadOnlyList<InlineButton>>
            {
                new[] { new InlineButton(isFa ? "بازگشت به منوی اصلی" : "Back to Main Menu", "stage:main_menu") }
            }, ct);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Button builders
    // ═══════════════════════════════════════════════════════════════════

    private static List<IReadOnlyList<InlineButton>> CancelRow(bool isFa) => new()
    { new[] { new InlineButton(isFa ? "لغو احراز هویت" : "Cancel", CbCancel) } };

    private static List<IReadOnlyList<InlineButton>> EmailButtons(bool isFa) => new()
    {
        new[] { new InlineButton(isFa ? "رد کردن ایمیل" : "Skip Email", CbSkipEmail) },
        new[] { new InlineButton(isFa ? "لغو" : "Cancel", CbCancel) },
    };

    // ═══════════════════════════════════════════════════════════════════
    //  Cleanup helpers — delete previous messages immediately
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>Delete the user's incoming message and track it for cleanup.</summary>
    private async Task CleanUserMsg(long chatId, int? msgId, CancellationToken ct)
    {
        if (msgId.HasValue)
            await TrackFlowMsg(chatId, msgId.Value, ct);
        await SafeDelete(chatId, msgId, ct);
    }

    /// <summary>Track a message ID in the flow for bulk cleanup on cancel.</summary>
    private async Task TrackFlowMsg(long userId, int msgId, CancellationToken ct)
    { try { await _stateStore.AddFlowMessageIdAsync(userId, msgId, ct).ConfigureAwait(false); } catch { } }

    /// <summary>Track the last bot message ID in the flow.</summary>
    private async Task TrackLastBotMsg(long userId, CancellationToken ct)
    {
        var id = await GetLastBotMsgId(userId, ct);
        if (id.HasValue) await TrackFlowMsg(userId, id.Value, ct);
    }

    /// <summary>Get the last bot message ID from the state repo.</summary>
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

    /// <summary>Try to edit the previous bot message in-place, or send a new one if edit fails.</summary>
    private async Task EditOrReplace(long chatId, int? msgId, string text, List<IReadOnlyList<InlineButton>> kb, CancellationToken ct)
    {
        if (msgId.HasValue)
        {
            try
            {
                await _sender.EditMessageTextWithInlineKeyboardAsync(chatId, msgId.Value, text, kb, ct).ConfigureAwait(false);
                return;
            }
            catch { /* edit failed, send new */ }
        }
        await SafeSendInline(chatId, text, kb, ct);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Safe wrappers
    // ═══════════════════════════════════════════════════════════════════

    private async Task SafeSendInline(long chatId, string text, List<IReadOnlyList<InlineButton>> kb, CancellationToken ct)
    {
        try { await _sender.SendTextMessageWithInlineKeyboardAsync(chatId, text, kb, ct).ConfigureAwait(false); } catch { }
        await TrackLastBotMsg(chatId, ct);
    }

    private async Task SafeSendContactRequest(long chatId, string text, string btn, string cancel, CancellationToken ct)
    {
        try { await _sender.SendContactRequestAsync(chatId, text, btn, cancel, ct).ConfigureAwait(false); } catch { }
        await TrackLastBotMsg(chatId, ct);
    }

    private async Task SafeDelete(long chatId, int? msgId, CancellationToken ct)
    { if (msgId.HasValue) try { await _sender.DeleteMessageAsync(chatId, msgId.Value, ct).ConfigureAwait(false); } catch { } }

    private async Task SafeAnswerCallback(string? id, string? text, CancellationToken ct)
    { if (id != null) try { await _sender.AnswerCallbackQueryAsync(id, text, ct).ConfigureAwait(false); } catch { } }

    private async Task<TelegramUserDto?> SafeGetUser(long userId, CancellationToken ct)
    { try { return await _userRepo.GetByTelegramUserIdAsync(userId, ct).ConfigureAwait(false); } catch { return null; } }

    // ═══════════════════════════════════════════════════════════════════
    //  Re-submission helpers
    // ═══════════════════════════════════════════════════════════════════

    private static readonly (string step, string field)[] KycSteps =
    { ("kyc_step_name","name"),("kyc_step_phone","phone"),("kyc_step_email","email"),("kyc_step_country","country"),("kyc_step_photo","photo") };

    private static string? GetNextRejectedStep(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try { var d = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string,string>>(json);
              if (d == null) return null;
              foreach (var (s,f) in KycSteps) if (d.ContainsKey(f)) return s; } catch { }
        return null;
    }

    private static string? GetNextFixStep(string? json, string done)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try { var d = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string,string>>(json);
              if (d == null) return null; bool found = false;
              foreach (var (s,f) in KycSteps) { if (f==done){found=true;continue;} if (found && d.ContainsKey(f)) return s; } } catch { }
        return null;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Utilities
    // ═══════════════════════════════════════════════════════════════════

    private static bool IsFa(TelegramUserDto? u) => (u?.PreferredLanguage ?? "fa") == "fa";
    private static bool IsCancelText(string t) => t == "لغو احراز هویت" || t == "لغو" || t.Equals("Cancel", StringComparison.OrdinalIgnoreCase);

    private static string MaskPhone(string p)
    { if (p.Length <= 4) return "****"; return p[..3] + new string('*', p.Length - 5) + p[^2..]; }

    private static string MaskEmail(string email)
    {
        var at = email.IndexOf('@');
        if (at <= 1) return "***" + email[at..];
        return email[..2] + new string('*', at - 2) + email[at..];
    }

    private static string GetCountryName(string code, bool isFa) => code switch
    {
        "ir"=>isFa?"ایران":"Iran", "tr"=>isFa?"ترکیه":"Turkey", "de"=>isFa?"آلمان":"Germany",
        "ca"=>isFa?"کانادا":"Canada", "au"=>isFa?"استرالیا":"Australia", "gb"=>isFa?"انگلستان":"UK",
        "fr"=>isFa?"فرانسه":"France", "nl"=>isFa?"هلند":"Netherlands", "at"=>isFa?"اتریش":"Austria",
        "it"=>isFa?"ایتالیا":"Italy", "se"=>isFa?"سوئد":"Sweden", _=>code
    };
}
