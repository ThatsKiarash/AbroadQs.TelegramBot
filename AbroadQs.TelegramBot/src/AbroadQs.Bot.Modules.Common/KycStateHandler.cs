using AbroadQs.Bot.Contracts;

namespace AbroadQs.Bot.Modules.Common;

/// <summary>
/// Handles multi-step KYC (identity verification) flow:
///   kyc_step_name          → user enters first + last name
///   kyc_step_phone         → user shares phone contact
///   kyc_step_otp:XXXX      → user enters SMS OTP (XXXX = expected)
///   kyc_step_email         → user enters email address  (skippable)
///   kyc_step_email_otp:XXXX→ user enters email OTP
///   kyc_step_country       → user selects country  (skippable)
///   kyc_step_photo         → user sends selfie with paper
///
/// Every step has inline buttons for Cancel (and Skip where applicable).
/// All external I/O (SMS, Email) is wrapped in timeouts so the bot never hangs.
/// </summary>
public sealed class KycStateHandler : IUpdateHandler
{
    private readonly IResponseSender _sender;
    private readonly ITelegramUserRepository _userRepo;
    private readonly IUserConversationStateStore _stateStore;
    private readonly ISmsService? _smsService;
    private readonly IEmailService? _emailService;
    private readonly IUserMessageStateRepository? _msgStateRepo;

    private const string SamplePhotoPath = "wwwroot/kyc_sample_photo.png";

    // ── Callback data constants ──────────────────────────────────────
    private const string CbCancel      = "cancel_kyc";
    private const string CbSkipEmail   = "skip_email";
    private const string CbSkipCountry = "skip_country";
    private const string CbStartKycFix = "start_kyc_fix";

    public KycStateHandler(
        IResponseSender sender,
        ITelegramUserRepository userRepo,
        IUserConversationStateStore stateStore,
        ISmsService? smsService = null,
        IEmailService? emailService = null,
        IUserMessageStateRepository? msgStateRepo = null)
    {
        _sender = sender;
        _userRepo = userRepo;
        _stateStore = stateStore;
        _smsService = smsService;
        _emailService = emailService;
        _msgStateRepo = msgStateRepo;
    }

    public string? Command => null;

    // ═══════════════════════════════════════════════════════════════════
    //  Can Handle
    // ═══════════════════════════════════════════════════════════════════

    public bool CanHandle(BotUpdateContext context)
    {
        if (context.UserId == null) return false;

        if (context.IsCallbackQuery)
        {
            var cb = context.MessageText?.Trim() ?? "";
            return cb.StartsWith("country:", StringComparison.OrdinalIgnoreCase)
                || cb == CbCancel
                || cb == CbSkipEmail
                || cb == CbSkipCountry
                || cb == CbStartKycFix;
        }

        return !string.IsNullOrEmpty(context.MessageText)
            || context.HasContact
            || context.HasPhoto;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Handle
    // ═══════════════════════════════════════════════════════════════════

    public async Task<bool> HandleAsync(BotUpdateContext context, CancellationToken cancellationToken)
    {
        if (context.UserId == null) return false;
        var userId = context.UserId.Value;
        var chatId = context.ChatId;

        // ── Callback queries ─────────────────────────────────────────
        if (context.IsCallbackQuery)
        {
            var cb = context.MessageText?.Trim() ?? "";
            await SafeAnswerCallback(context.CallbackQueryId, null, cancellationToken);

            // ── Cancel KYC ───────────────────────────────────────────
            if (cb == CbCancel)
            {
                var state = await _stateStore.GetStateAsync(userId, cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrEmpty(state) || !state.StartsWith("kyc_step_", StringComparison.OrdinalIgnoreCase))
                    return false;

                var user = await SafeGetUser(userId, cancellationToken);
                var isFa = IsFarsi(user);
                await CancelKycFlowAsync(chatId, userId, user?.CleanChatMode ?? true, isFa, cancellationToken);
                return true;
            }

            // ── Skip email ───────────────────────────────────────────
            if (cb == CbSkipEmail)
            {
                var state = await _stateStore.GetStateAsync(userId, cancellationToken).ConfigureAwait(false);
                if (state != "kyc_step_email") return false;

                var user = await SafeGetUser(userId, cancellationToken);
                var isFa = IsFarsi(user);
                await TrackIncomingAsync(userId, context.CallbackMessageId, cancellationToken);

                // Skip email → go to country
                var nextFixStep = GetNextFixStep(user?.KycRejectionData, "email");
                if (nextFixStep != null)
                {
                    await _stateStore.SetStateAsync(userId, nextFixStep, cancellationToken).ConfigureAwait(false);
                    await SendStepPromptAsync(chatId, userId, nextFixStep, isFa, cancellationToken);
                    return true;
                }
                await _stateStore.SetStateAsync(userId, "kyc_step_country", cancellationToken).ConfigureAwait(false);
                await SendCountrySelectionAsync(chatId, userId, isFa, cancellationToken);
                return true;
            }

            // ── Skip country ─────────────────────────────────────────
            if (cb == CbSkipCountry)
            {
                var state = await _stateStore.GetStateAsync(userId, cancellationToken).ConfigureAwait(false);
                if (state != "kyc_step_country") return false;

                var user = await SafeGetUser(userId, cancellationToken);
                var isFa = IsFarsi(user);
                await TrackIncomingAsync(userId, context.CallbackMessageId, cancellationToken);

                // Skip country → go to photo
                await GoToPhotoStepAsync(chatId, userId, isFa, cancellationToken);
                return true;
            }

            // ── start_kyc_fix: begin partial re-submission ───────────
            if (cb == CbStartKycFix)
            {
                var user = await SafeGetUser(userId, cancellationToken);
                var isFa = IsFarsi(user);
                var cleanMode = user?.CleanChatMode ?? true;
                if (cleanMode && context.CallbackMessageId.HasValue)
                    await SafeDelete(chatId, context.CallbackMessageId.Value, cancellationToken);

                var nextStep = GetNextRejectedStep(user?.KycRejectionData) ?? "kyc_step_name";
                await _stateStore.SetStateAsync(userId, nextStep, cancellationToken).ConfigureAwait(false);
                await SendStepPromptAsync(chatId, userId, nextStep, isFa, cancellationToken);
                return true;
            }

            // ── country:XX callback ──────────────────────────────────
            if (cb.StartsWith("country:", StringComparison.OrdinalIgnoreCase))
            {
                var state = await _stateStore.GetStateAsync(userId, cancellationToken).ConfigureAwait(false);
                if (state != "kyc_step_country") return false;

                var countryCode = cb["country:".Length..].Trim();
                var user = await SafeGetUser(userId, cancellationToken);
                var isFa = IsFarsi(user);

                if (countryCode == "other")
                {
                    await _stateStore.SetStateAsync(userId, "kyc_step_country_text", cancellationToken).ConfigureAwait(false);
                    var msg = isFa
                        ? "لطفا نام کشور محل سکونت خود را تایپ کنید:"
                        : "Please type your country of residence:";
                    await TrackIncomingAsync(userId, context.CallbackMessageId, cancellationToken);
                    await SafeSendInline(chatId, msg, CancelRow(isFa), cancellationToken);
                    await TrackLastBotMsgAsync(userId, cancellationToken);
                    return true;
                }

                var countryName = GetCountryName(countryCode, isFa);
                await _userRepo.SetCountryAsync(userId, countryName, cancellationToken).ConfigureAwait(false);
                await TrackIncomingAsync(userId, context.CallbackMessageId, cancellationToken);
                await GoToPhotoStepAsync(chatId, userId, isFa, cancellationToken);
                return true;
            }

            return false;
        }

        // ── Text / Contact / Photo ───────────────────────────────────
        var state2 = await _stateStore.GetStateAsync(userId, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrEmpty(state2) || !state2.StartsWith("kyc_step_", StringComparison.OrdinalIgnoreCase))
            return false;

        var currentUser = await SafeGetUser(userId, cancellationToken);
        var isFaLang = IsFarsi(currentUser);

        await TrackIncomingAsync(userId, context.IncomingMessageId, cancellationToken);

        // ── Step 1: Name ─────────────────────────────────────────────
        if (state2 == "kyc_step_name")
        {
            var text = context.MessageText?.Trim();
            if (string.IsNullOrEmpty(text))
            {
                await SendNamePrompt(chatId, userId, isFaLang, cancellationToken);
                return true;
            }

            var parts = text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            var firstName = parts.Length > 0 ? parts[0].Trim() : null;
            var lastName = parts.Length > 1 ? parts[1].Trim() : null;

            if (string.IsNullOrEmpty(firstName) || string.IsNullOrEmpty(lastName))
            {
                var msg = isFaLang
                    ? "لطفا نام و نام خانوادگی خود را در یک خط وارد کنید.\nمثال: <b>علی احمدی</b>"
                    : "Please enter both first and last name in one line.\nExample: <b>John Smith</b>";
                await SafeSendInline(chatId, msg, CancelRow(isFaLang), cancellationToken);
                await TrackLastBotMsgAsync(userId, cancellationToken);
                return true;
            }

            await _userRepo.UpdateProfileAsync(userId, firstName, lastName, null, cancellationToken).ConfigureAwait(false);

            // Partial fix?
            var nextFixStep = GetNextFixStep(currentUser?.KycRejectionData, "name");
            if (nextFixStep != null)
            {
                await _stateStore.SetStateAsync(userId, nextFixStep, cancellationToken).ConfigureAwait(false);
                await SendStepPromptAsync(chatId, userId, nextFixStep, isFaLang, cancellationToken);
                return true;
            }

            await _stateStore.SetStateAsync(userId, "kyc_step_phone", cancellationToken).ConfigureAwait(false);
            var phoneMsg = isFaLang
                ? $"نام شما ثبت شد: <b>{Esc(firstName)} {Esc(lastName)}</b>\n\nاکنون لطفا شماره تلفن خود را با زدن دکمه زیر به اشتراک بگذارید:"
                : $"Name saved: <b>{Esc(firstName)} {Esc(lastName)}</b>\n\nNow please share your phone number by pressing the button below:";
            var btnLabel = isFaLang ? "اشتراک‌گذاری شماره تلفن" : "Share Phone Number";
            var cancelLabel = isFaLang ? "لغو احراز هویت" : "Cancel";
            await SafeSendContactRequest(chatId, phoneMsg, btnLabel, cancelLabel, cancellationToken);
            await TrackLastBotMsgAsync(userId, cancellationToken);
            return true;
        }

        // ── Step 2: Phone ────────────────────────────────────────────
        if (state2 == "kyc_step_phone")
        {
            var txt = context.MessageText?.Trim();
            // Check cancel via reply keyboard button
            if (!string.IsNullOrEmpty(txt) && IsCancelText(txt))
            {
                await CancelKycFlowAsync(chatId, userId, currentUser?.CleanChatMode ?? true, isFaLang, cancellationToken);
                return true;
            }

            if (!context.HasContact)
            {
                var msg = isFaLang
                    ? "لطفا از دکمه زیر برای اشتراک‌گذاری شماره تلفن استفاده کنید:"
                    : "Please use the button below to share your phone number:";
                await SafeSendText(chatId, msg, cancellationToken);
                await TrackLastBotMsgAsync(userId, cancellationToken);
                return true;
            }

            var phone = context.ContactPhoneNumber!;
            await _userRepo.SetPhoneNumberAsync(userId, phone, cancellationToken).ConfigureAwait(false);

            var otp = new Random().Next(10000, 99999).ToString();
            await _stateStore.SetStateAsync(userId, $"kyc_step_otp:{otp}", cancellationToken).ConfigureAwait(false);

            // Send OTP via SMS — timeout-protected
            if (_smsService != null)
            {
                bool sent;
                try
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    cts.CancelAfter(TimeSpan.FromSeconds(15));
                    sent = await _smsService.SendVerificationCodeAsync(phone, otp, cts.Token).ConfigureAwait(false);
                }
                catch { sent = false; }

                if (!sent)
                {
                    var errMsg = isFaLang
                        ? "خطا در ارسال کد تایید پیامکی. لطفا دوباره شماره تلفن خود را به اشتراک بگذارید."
                        : "Error sending SMS code. Please share your phone number again.";
                    await _stateStore.SetStateAsync(userId, "kyc_step_phone", cancellationToken).ConfigureAwait(false);
                    await SafeSendContactRequest(chatId, errMsg,
                        isFaLang ? "اشتراک‌گذاری شماره تلفن" : "Share Phone Number",
                        isFaLang ? "لغو احراز هویت" : "Cancel", cancellationToken);
                    await TrackLastBotMsgAsync(userId, cancellationToken);
                    return true;
                }
            }

            var masked = MaskPhone(phone);
            var otpMsg = isFaLang
                ? $"شماره تلفن شما ثبت شد.\nکد تایید به شماره <b>{masked}</b> ارسال شد.\n\nلطفا کد 5 رقمی را وارد کنید:"
                : $"Phone number saved.\nCode sent to <b>{masked}</b>.\n\nPlease enter the 5-digit code:";
            await SafeRemoveKeyboard(chatId, "...", cancellationToken);
            await SafeSendInline(chatId, otpMsg, CancelRow(isFaLang), cancellationToken);
            await TrackLastBotMsgAsync(userId, cancellationToken);
            return true;
        }

        // ── Step 3: SMS OTP ──────────────────────────────────────────
        if (state2.StartsWith("kyc_step_otp:", StringComparison.OrdinalIgnoreCase))
        {
            var expectedOtp = state2["kyc_step_otp:".Length..];
            var enteredOtp = context.MessageText?.Trim();

            if (string.IsNullOrEmpty(enteredOtp))
            {
                var msg = isFaLang
                    ? "لطفا کد تایید 5 رقمی را وارد کنید:"
                    : "Please enter the 5-digit verification code:";
                await SafeSendInline(chatId, msg, CancelRow(isFaLang), cancellationToken);
                await TrackLastBotMsgAsync(userId, cancellationToken);
                return true;
            }

            if (enteredOtp != expectedOtp)
            {
                var msg = isFaLang
                    ? "کد وارد شده صحیح نیست. لطفا دوباره تلاش کنید:"
                    : "Incorrect code. Please try again:";
                await SafeSendInline(chatId, msg, CancelRow(isFaLang), cancellationToken);
                await TrackLastBotMsgAsync(userId, cancellationToken);
                return true;
            }

            // OTP correct → check partial fix
            var nextFixStep = GetNextFixStep(currentUser?.KycRejectionData, "phone");
            if (nextFixStep != null)
            {
                await _stateStore.SetStateAsync(userId, nextFixStep, cancellationToken).ConfigureAwait(false);
                await SendStepPromptAsync(chatId, userId, nextFixStep, isFaLang, cancellationToken);
                return true;
            }

            // → email step
            await _stateStore.SetStateAsync(userId, "kyc_step_email", cancellationToken).ConfigureAwait(false);
            await SendEmailPrompt(chatId, userId, isFaLang,
                isFaLang ? "شماره تلفن شما با موفقیت تایید شد.\n\n" : "Phone number verified.\n\n",
                cancellationToken);
            return true;
        }

        // ── Step 4: Email entry ──────────────────────────────────────
        if (state2 == "kyc_step_email")
        {
            var email = context.MessageText?.Trim();
            if (string.IsNullOrEmpty(email) || !email.Contains('@') || !email.Contains('.'))
            {
                var msg = isFaLang
                    ? "لطفا یک آدرس ایمیل معتبر وارد کنید:\nمثال: <b>you@example.com</b>"
                    : "Please enter a valid email address:\nExample: <b>you@example.com</b>";
                await SafeSendInline(chatId, msg, EmailStepButtons(isFaLang), cancellationToken);
                await TrackLastBotMsgAsync(userId, cancellationToken);
                return true;
            }

            await _userRepo.SetEmailAsync(userId, email, cancellationToken).ConfigureAwait(false);

            // Generate & send email OTP — timeout-protected
            var otp = new Random().Next(10000, 99999).ToString();
            await _stateStore.SetStateAsync(userId, $"kyc_step_email_otp:{otp}", cancellationToken).ConfigureAwait(false);

            if (_emailService != null)
            {
                bool sent;
                try
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    cts.CancelAfter(TimeSpan.FromSeconds(20));
                    sent = await _emailService.SendVerificationCodeAsync(email, otp, cts.Token).ConfigureAwait(false);
                }
                catch { sent = false; }

                if (!sent)
                {
                    // Don't get stuck — offer retry or skip
                    var errMsg = isFaLang
                        ? "متاسفانه ارسال کد تایید ایمیل با خطا مواجه شد.\nمی‌توانید ایمیل دیگری وارد کنید یا این مرحله را رد کنید."
                        : "Failed to send email verification code.\nYou can enter another email or skip this step.";
                    await _stateStore.SetStateAsync(userId, "kyc_step_email", cancellationToken).ConfigureAwait(false);
                    await SafeSendInline(chatId, errMsg, EmailStepButtons(isFaLang), cancellationToken);
                    await TrackLastBotMsgAsync(userId, cancellationToken);
                    return true;
                }
            }

            var maskedEmail = MaskEmail(email);
            var msg2 = isFaLang
                ? $"کد تایید به ایمیل <b>{maskedEmail}</b> ارسال شد.\n\nلطفا کد 5 رقمی را وارد کنید:"
                : $"Verification code sent to <b>{maskedEmail}</b>.\n\nPlease enter the 5-digit code:";
            await SafeSendInline(chatId, msg2, EmailOtpButtons(isFaLang), cancellationToken);
            await TrackLastBotMsgAsync(userId, cancellationToken);
            return true;
        }

        // ── Step 5: Email OTP ────────────────────────────────────────
        if (state2.StartsWith("kyc_step_email_otp:", StringComparison.OrdinalIgnoreCase))
        {
            var expectedOtp = state2["kyc_step_email_otp:".Length..];
            var enteredOtp = context.MessageText?.Trim();

            if (string.IsNullOrEmpty(enteredOtp))
            {
                var msg = isFaLang
                    ? "لطفا کد تایید ایمیل 5 رقمی را وارد کنید:"
                    : "Please enter the 5-digit email verification code:";
                await SafeSendInline(chatId, msg, EmailOtpButtons(isFaLang), cancellationToken);
                await TrackLastBotMsgAsync(userId, cancellationToken);
                return true;
            }

            if (enteredOtp != expectedOtp)
            {
                var msg = isFaLang
                    ? "کد وارد شده صحیح نیست. لطفا دوباره تلاش کنید:"
                    : "Incorrect code. Please try again:";
                await SafeSendInline(chatId, msg, EmailOtpButtons(isFaLang), cancellationToken);
                await TrackLastBotMsgAsync(userId, cancellationToken);
                return true;
            }

            await _userRepo.SetEmailVerifiedAsync(userId, cancellationToken).ConfigureAwait(false);

            var nextFixStep = GetNextFixStep(currentUser?.KycRejectionData, "email");
            if (nextFixStep != null)
            {
                await _stateStore.SetStateAsync(userId, nextFixStep, cancellationToken).ConfigureAwait(false);
                await SendStepPromptAsync(chatId, userId, nextFixStep, isFaLang, cancellationToken);
                return true;
            }

            await _stateStore.SetStateAsync(userId, "kyc_step_country", cancellationToken).ConfigureAwait(false);
            await SendCountrySelectionAsync(chatId, userId, isFaLang, cancellationToken);
            return true;
        }

        // ── Step 6a: Country text (for "Other") ─────────────────────
        if (state2 == "kyc_step_country_text")
        {
            var country = context.MessageText?.Trim();
            if (string.IsNullOrEmpty(country))
            {
                var msg = isFaLang ? "لطفا نام کشور را وارد کنید:" : "Please enter the country name:";
                await SafeSendInline(chatId, msg, CancelRow(isFaLang), cancellationToken);
                await TrackLastBotMsgAsync(userId, cancellationToken);
                return true;
            }

            await _userRepo.SetCountryAsync(userId, country, cancellationToken).ConfigureAwait(false);
            await GoToPhotoStepAsync(chatId, userId, isFaLang, cancellationToken);
            return true;
        }

        // ── Step 7: Photo upload ─────────────────────────────────────
        if (state2 == "kyc_step_photo")
        {
            if (!context.HasPhoto)
            {
                var msg = isFaLang
                    ? "لطفا یک عکس سلفی ارسال کنید (نه فایل).\nعکس باید شامل صورت شما و کاغذی با نوشته <b>AbroadQs</b> باشد."
                    : "Please send a selfie photo (not a file).\nThe photo must show your face and a paper with <b>AbroadQs</b> written on it.";
                await SafeSendInline(chatId, msg, CancelRow(isFaLang), cancellationToken);
                await TrackLastBotMsgAsync(userId, cancellationToken);
                return true;
            }

            var photoFileId = context.PhotoFileId!;
            await _userRepo.SetVerifiedAsync(userId, photoFileId, cancellationToken).ConfigureAwait(false);
            await _userRepo.SetKycStatusAsync(userId, "pending_review", null, cancellationToken).ConfigureAwait(false);
            await _stateStore.ClearStateAsync(userId, cancellationToken).ConfigureAwait(false);

            await CleanupFlowMessagesAsync(chatId, userId, currentUser?.CleanChatMode ?? true, cancellationToken);

            var successMsg = isFaLang
                ? "مدارک شما با موفقیت ارسال شد و در انتظار بررسی توسط تیم ماست.\nپس از بررسی، نتیجه به شما اطلاع داده خواهد شد."
                : "Your documents have been submitted for review.\nYou will be notified once the review is complete.";
            var kb = new List<IReadOnlyList<InlineButton>>
            {
                new[] { new InlineButton(isFaLang ? "بازگشت به منوی اصلی" : "Back to Main Menu", "stage:main_menu") }
            };
            await SafeSendInline(chatId, successMsg, kb, cancellationToken);
            return true;
        }

        return false;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Inline button builders
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>Single cancel row for steps that are NOT skippable.</summary>
    private static List<IReadOnlyList<InlineButton>> CancelRow(bool isFa) => new()
    {
        new[] { new InlineButton(isFa ? "لغو احراز هویت" : "Cancel Verification", CbCancel) }
    };

    /// <summary>Email step: skip + cancel.</summary>
    private static List<IReadOnlyList<InlineButton>> EmailStepButtons(bool isFa) => new()
    {
        new[]
        {
            new InlineButton(isFa ? "رد کردن ایمیل" : "Skip Email", CbSkipEmail),
            new InlineButton(isFa ? "لغو" : "Cancel", CbCancel),
        }
    };

    /// <summary>Email OTP step: skip (don't verify) + cancel.</summary>
    private static List<IReadOnlyList<InlineButton>> EmailOtpButtons(bool isFa) => new()
    {
        new[]
        {
            new InlineButton(isFa ? "رد کردن ایمیل" : "Skip Email", CbSkipEmail),
            new InlineButton(isFa ? "لغو" : "Cancel", CbCancel),
        }
    };

    // ═══════════════════════════════════════════════════════════════════
    //  Cancel flow
    // ═══════════════════════════════════════════════════════════════════

    private async Task CancelKycFlowAsync(long chatId, long userId, bool cleanMode, bool isFa, CancellationToken ct)
    {
        await _stateStore.ClearStateAsync(userId, ct).ConfigureAwait(false);
        await CleanupFlowMessagesAsync(chatId, userId, cleanMode, ct);

        try { await _sender.RemoveReplyKeyboardAsync(chatId, "...", ct).ConfigureAwait(false); } catch { }

        var cancelMsg = isFa
            ? "فرایند احراز هویت لغو شد.\nهر زمان که آماده بودید، می‌توانید دوباره از بخش پروفایل اقدام کنید."
            : "Verification process cancelled.\nYou can restart anytime from the Profile section.";
        var kb = new List<IReadOnlyList<InlineButton>>
        {
            new[] { new InlineButton(isFa ? "بازگشت به منوی اصلی" : "Back to Main Menu", "stage:main_menu") }
        };
        await SafeSendInline(chatId, cancelMsg, kb, ct);
    }

    private static bool IsCancelText(string txt) =>
        txt == "لغو احراز هویت" || txt == "لغو" || string.Equals(txt, "Cancel", StringComparison.OrdinalIgnoreCase);

    // ═══════════════════════════════════════════════════════════════════
    //  Prompt helpers
    // ═══════════════════════════════════════════════════════════════════

    private async Task SendNamePrompt(long chatId, long userId, bool isFa, CancellationToken ct)
    {
        var msg = isFa
            ? "لطفا نام و نام خانوادگی خود را در یک خط وارد کنید:\nمثال: <b>علی احمدی</b>"
            : "Please enter your first and last name in one line:\nExample: <b>John Smith</b>";
        await SafeSendInline(chatId, msg, CancelRow(isFa), ct);
        await TrackLastBotMsgAsync(userId, ct);
    }

    private async Task SendEmailPrompt(long chatId, long userId, bool isFa, string prefix, CancellationToken ct)
    {
        var msg = prefix + (isFa
            ? "لطفا آدرس ایمیل خود را وارد کنید:\n(می‌توانید این مرحله را رد کنید)"
            : "Please enter your email address:\n(You can skip this step)");
        await SafeSendInline(chatId, msg, EmailStepButtons(isFa), ct);
        await TrackLastBotMsgAsync(userId, ct);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Country selection
    // ═══════════════════════════════════════════════════════════════════

    private async Task SendCountrySelectionAsync(long chatId, long userId, bool isFa, CancellationToken ct)
    {
        var msg = isFa
            ? "اکنون لطفا کشور محل سکونت خود را انتخاب کنید:\n(می‌توانید این مرحله را رد کنید)"
            : "Now please select your country of residence:\n(You can skip this step)";

        var countries = new (string code, string fa, string en)[]
        {
            ("ir", "ایران", "Iran"),
            ("tr", "ترکیه", "Turkey"),
            ("de", "آلمان", "Germany"),
            ("ca", "کانادا", "Canada"),
            ("au", "استرالیا", "Australia"),
            ("gb", "انگلستان", "UK"),
            ("fr", "فرانسه", "France"),
            ("nl", "هلند", "Netherlands"),
            ("at", "اتریش", "Austria"),
            ("it", "ایتالیا", "Italy"),
            ("se", "سوئد", "Sweden"),
            ("other", "سایر", "Other"),
        };

        var keyboard = new List<IReadOnlyList<InlineButton>>();
        for (int i = 0; i < countries.Length; i += 3)
        {
            var row = new List<InlineButton>();
            for (int j = i; j < Math.Min(i + 3, countries.Length); j++)
            {
                var label = isFa ? countries[j].fa : countries[j].en;
                row.Add(new InlineButton(label, $"country:{countries[j].code}"));
            }
            keyboard.Add(row);
        }

        // Skip + Cancel row
        keyboard.Add(new[]
        {
            new InlineButton(isFa ? "رد کردن" : "Skip", CbSkipCountry),
            new InlineButton(isFa ? "لغو" : "Cancel", CbCancel),
        });

        await SafeSendInline(chatId, msg, keyboard, ct);
        await TrackLastBotMsgAsync(userId, ct);
    }

    private static string GetCountryName(string code, bool isFa) => code switch
    {
        "ir" => isFa ? "ایران" : "Iran",
        "tr" => isFa ? "ترکیه" : "Turkey",
        "de" => isFa ? "آلمان" : "Germany",
        "ca" => isFa ? "کانادا" : "Canada",
        "au" => isFa ? "استرالیا" : "Australia",
        "gb" => isFa ? "انگلستان" : "UK",
        "fr" => isFa ? "فرانسه" : "France",
        "nl" => isFa ? "هلند" : "Netherlands",
        "at" => isFa ? "اتریش" : "Austria",
        "it" => isFa ? "ایتالیا" : "Italy",
        "se" => isFa ? "سوئد" : "Sweden",
        _ => code
    };

    // ═══════════════════════════════════════════════════════════════════
    //  Photo step
    // ═══════════════════════════════════════════════════════════════════

    private async Task GoToPhotoStepAsync(long chatId, long userId, bool isFa, CancellationToken ct)
    {
        var user = await SafeGetUser(userId, ct);
        var nextFixStep = GetNextFixStep(user?.KycRejectionData, "country");
        if (nextFixStep != null)
        {
            await _stateStore.SetStateAsync(userId, nextFixStep, ct).ConfigureAwait(false);
            await SendStepPromptAsync(chatId, userId, nextFixStep, isFa, ct);
            return;
        }

        await _stateStore.SetStateAsync(userId, "kyc_step_photo", ct).ConfigureAwait(false);

        // Send sample photo
        try
        {
            var photoPath = Path.Combine(AppContext.BaseDirectory, SamplePhotoPath);
            if (File.Exists(photoPath))
            {
                await _sender.SendPhotoAsync(chatId, photoPath,
                    isFa ? "نمونه عکس تایید هویت" : "Sample verification photo", ct).ConfigureAwait(false);
                await TrackLastBotMsgAsync(userId, ct);
            }
        }
        catch { /* continue without sample */ }

        var photoMsg = isFa
            ? "اکنون لطفا یک عکس سلفی از خود ارسال کنید که در آن:\n" +
              "- یک تکه کاغذ در کنار صورت شما باشد\n" +
              "- روی کاغذ عبارت <b>AbroadQs</b> نوشته شده باشد\n\n" +
              "مطابق تصویر نمونه بالا عکس بگیرید."
            : "Now please send a selfie photo where:\n" +
              "- You hold a piece of paper next to your face\n" +
              "- The paper has <b>AbroadQs</b> written on it\n\n" +
              "Take the photo similar to the sample above.";
        await SafeSendInline(chatId, photoMsg, CancelRow(isFa), ct);
        await TrackLastBotMsgAsync(userId, ct);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Flow message tracking + cleanup
    // ═══════════════════════════════════════════════════════════════════

    private async Task TrackIncomingAsync(long userId, int? messageId, CancellationToken ct)
    {
        if (messageId.HasValue)
            try { await _stateStore.AddFlowMessageIdAsync(userId, messageId.Value, ct).ConfigureAwait(false); } catch { }
    }

    private async Task TrackLastBotMsgAsync(long userId, CancellationToken ct)
    {
        if (_msgStateRepo == null) return;
        try
        {
            var s = await _msgStateRepo.GetUserMessageStateAsync(userId, ct).ConfigureAwait(false);
            if (s?.LastBotTelegramMessageId is > 0)
                await _stateStore.AddFlowMessageIdAsync(userId, (int)s.LastBotTelegramMessageId, ct).ConfigureAwait(false);
        }
        catch { }
    }

    private async Task CleanupFlowMessagesAsync(long chatId, long userId, bool cleanMode, CancellationToken ct)
    {
        if (!cleanMode) return;
        try
        {
            var ids = await _stateStore.GetAndClearFlowMessageIdsAsync(userId, ct).ConfigureAwait(false);
            foreach (var id in ids)
                try { await _sender.DeleteMessageAsync(chatId, id, ct).ConfigureAwait(false); } catch { }
        }
        catch { }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Safe wrappers — the bot must NEVER hang
    // ═══════════════════════════════════════════════════════════════════

    private async Task SafeSendText(long chatId, string text, CancellationToken ct)
    { try { await _sender.SendTextMessageAsync(chatId, text, ct).ConfigureAwait(false); } catch { } }

    private async Task SafeSendInline(long chatId, string text, List<IReadOnlyList<InlineButton>> kb, CancellationToken ct)
    { try { await _sender.SendTextMessageWithInlineKeyboardAsync(chatId, text, kb, ct).ConfigureAwait(false); } catch { } }

    private async Task SafeSendContactRequest(long chatId, string text, string btn, string cancel, CancellationToken ct)
    { try { await _sender.SendContactRequestAsync(chatId, text, btn, cancel, ct).ConfigureAwait(false); } catch { } }

    private async Task SafeRemoveKeyboard(long chatId, string text, CancellationToken ct)
    { try { await _sender.RemoveReplyKeyboardAsync(chatId, text, ct).ConfigureAwait(false); } catch { } }

    private async Task SafeDelete(long chatId, int msgId, CancellationToken ct)
    { try { await _sender.DeleteMessageAsync(chatId, msgId, ct).ConfigureAwait(false); } catch { } }

    private async Task SafeAnswerCallback(string? callbackId, string? text, CancellationToken ct)
    { if (callbackId != null) try { await _sender.AnswerCallbackQueryAsync(callbackId, text, ct).ConfigureAwait(false); } catch { } }

    private async Task<TelegramUserDto?> SafeGetUser(long userId, CancellationToken ct)
    { try { return await _userRepo.GetByTelegramUserIdAsync(userId, ct).ConfigureAwait(false); } catch { return null; } }

    // ═══════════════════════════════════════════════════════════════════
    //  Re-submission helpers
    // ═══════════════════════════════════════════════════════════════════

    private static readonly (string step, string field)[] KycSteps = new[]
    {
        ("kyc_step_name", "name"),
        ("kyc_step_phone", "phone"),
        ("kyc_step_email", "email"),
        ("kyc_step_country", "country"),
        ("kyc_step_photo", "photo"),
    };

    private static string? GetNextRejectedStep(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            var dict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (dict == null || dict.Count == 0) return null;
            foreach (var (step, field) in KycSteps)
                if (dict.ContainsKey(field)) return step;
        }
        catch { }
        return null;
    }

    private static string? GetNextFixStep(string? json, string completedField)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            var dict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (dict == null || dict.Count == 0) return null;
            bool found = false;
            foreach (var (step, field) in KycSteps)
            {
                if (field == completedField) { found = true; continue; }
                if (found && dict.ContainsKey(field)) return step;
            }
        }
        catch { }
        return null;
    }

    private async Task SendStepPromptAsync(long chatId, long userId, string step, bool isFa, CancellationToken ct)
    {
        switch (step)
        {
            case "kyc_step_name":
                await SendNamePrompt(chatId, userId, isFa, ct);
                return;
            case "kyc_step_phone":
                var phoneMsg = isFa
                    ? "لطفا شماره تلفن خود را با زدن دکمه زیر به اشتراک بگذارید:"
                    : "Please share your phone number by pressing the button below:";
                await SafeSendContactRequest(chatId, phoneMsg,
                    isFa ? "اشتراک‌گذاری شماره تلفن" : "Share Phone Number",
                    isFa ? "لغو احراز هویت" : "Cancel", ct);
                break;
            case "kyc_step_email":
                await SendEmailPrompt(chatId, userId, isFa, "", ct);
                return;
            case "kyc_step_country":
                await SendCountrySelectionAsync(chatId, userId, isFa, ct);
                return;
            case "kyc_step_photo":
                await GoToPhotoStepAsync(chatId, userId, isFa, ct);
                return;
        }
        await TrackLastBotMsgAsync(userId, ct);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Utilities
    // ═══════════════════════════════════════════════════════════════════

    private static bool IsFarsi(TelegramUserDto? u) => (u?.PreferredLanguage ?? "fa") == "fa";

    private static string Esc(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    private static string MaskPhone(string phone)
    {
        if (phone.Length <= 4) return "****";
        return phone[..3] + new string('*', phone.Length - 5) + phone[^2..];
    }

    private static string MaskEmail(string email)
    {
        var at = email.IndexOf('@');
        if (at <= 1) return "***" + email[at..];
        return email[..2] + new string('*', at - 2) + email[at..];
    }
}
