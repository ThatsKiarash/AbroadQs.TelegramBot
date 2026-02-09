using AbroadQs.Bot.Contracts;

namespace AbroadQs.Bot.Modules.Common;

/// <summary>
/// Handles multi-step KYC (identity verification) flow:
///   kyc_step_name          → user enters first + last name
///   kyc_step_phone         → user shares phone contact
///   kyc_step_otp:XXXX      → user enters SMS OTP (XXXX = expected)
///   kyc_step_email         → user enters email address
///   kyc_step_email_otp:XXXX→ user enters email OTP
///   kyc_step_country       → user selects country from inline buttons
///   kyc_step_photo         → user sends selfie with paper
///
/// Also supports partial re-submission when admin rejects fields (start_kyc_fix).
/// Tracks message IDs during the flow for bulk cleanup at the end.
/// </summary>
public sealed class KycStateHandler : IUpdateHandler
{
    private readonly IResponseSender _sender;
    private readonly ITelegramUserRepository _userRepo;
    private readonly IUserConversationStateStore _stateStore;
    private readonly ISmsService? _smsService;
    private readonly IEmailService? _emailService;
    private readonly IUserMessageStateRepository? _msgStateRepo;

    // Path to the sample KYC photo (inside wwwroot)
    private const string SamplePhotoPath = "wwwroot/kyc_sample_photo.png";

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

    public bool CanHandle(BotUpdateContext context)
    {
        if (context.UserId == null) return false;
        // Handle callback for country selection during KYC
        if (context.IsCallbackQuery)
        {
            var cb = context.MessageText?.Trim() ?? "";
            return cb.StartsWith("country:", StringComparison.OrdinalIgnoreCase)
                || cb == "start_kyc_fix";
        }
        // Accept text, contact, or photo messages (state check in HandleAsync)
        return !string.IsNullOrEmpty(context.MessageText)
            || context.HasContact
            || context.HasPhoto;
    }

    public async Task<bool> HandleAsync(BotUpdateContext context, CancellationToken cancellationToken)
    {
        if (context.UserId == null) return false;
        var userId = context.UserId.Value;
        var chatId = context.ChatId;

        // ── Callback: country selection ─────────────────────────────────
        if (context.IsCallbackQuery)
        {
            var cb = context.MessageText?.Trim() ?? "";

            // start_kyc_fix: begin partial re-submission for rejected fields
            if (cb == "start_kyc_fix")
            {
                if (context.CallbackQueryId != null)
                    await _sender.AnswerCallbackQueryAsync(context.CallbackQueryId, null, cancellationToken).ConfigureAwait(false);

                var user = await _userRepo.GetByTelegramUserIdAsync(userId, cancellationToken).ConfigureAwait(false);
                var isFa = (user?.PreferredLanguage ?? "fa") == "fa";
                var cleanMode = user?.CleanChatMode ?? true;
                var editMsgId = context.CallbackMessageId;
                if (cleanMode && editMsgId.HasValue)
                    await _sender.DeleteMessageAsync(chatId, editMsgId.Value, cancellationToken).ConfigureAwait(false);

                // Determine which fields are rejected
                var rejectionData = user?.KycRejectionData;
                var nextStep = GetNextRejectedStep(rejectionData);
                if (nextStep == null) nextStep = "kyc_step_name"; // fallback: redo all

                await _stateStore.SetStateAsync(userId, nextStep, cancellationToken).ConfigureAwait(false);
                await SendStepPromptAsync(chatId, userId, nextStep, isFa, cancellationToken).ConfigureAwait(false);
                return true;
            }

            // country:XX callback
            if (cb.StartsWith("country:", StringComparison.OrdinalIgnoreCase))
            {
                if (context.CallbackQueryId != null)
                    await _sender.AnswerCallbackQueryAsync(context.CallbackQueryId, null, cancellationToken).ConfigureAwait(false);

                var state = await _stateStore.GetStateAsync(userId, cancellationToken).ConfigureAwait(false);
                if (state != "kyc_step_country") return false;

                var countryCode = cb["country:".Length..].Trim();
                var user = await _userRepo.GetByTelegramUserIdAsync(userId, cancellationToken).ConfigureAwait(false);
                var isFa = (user?.PreferredLanguage ?? "fa") == "fa";

                if (countryCode == "other")
                {
                    await _stateStore.SetStateAsync(userId, "kyc_step_country_text", cancellationToken).ConfigureAwait(false);
                    var msg = isFa
                        ? "لطفاً نام کشور محل سکونت خود را تایپ کنید:"
                        : "Please type your country of residence:";
                    // Track outgoing
                    await TrackIncomingAsync(userId, context.CallbackMessageId, cancellationToken).ConfigureAwait(false);
                    await _sender.SendTextMessageAsync(chatId, msg, cancellationToken).ConfigureAwait(false);
                    await TrackLastBotMsgAsync(userId, cancellationToken).ConfigureAwait(false);
                    return true;
                }

                var countryName = GetCountryName(countryCode, isFa);
                await _userRepo.SetCountryAsync(userId, countryName, cancellationToken).ConfigureAwait(false);

                // Track outgoing
                await TrackIncomingAsync(userId, context.CallbackMessageId, cancellationToken).ConfigureAwait(false);

                // Move to photo step
                await GoToPhotoStepAsync(chatId, userId, isFa, cancellationToken).ConfigureAwait(false);
                return true;
            }

            return false;
        }

        // ── Text / Contact / Photo messages ─────────────────────────────
        var state2 = await _stateStore.GetStateAsync(userId, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrEmpty(state2) || !state2.StartsWith("kyc_step_", StringComparison.OrdinalIgnoreCase))
            return false;

        var currentUser = await _userRepo.GetByTelegramUserIdAsync(userId, cancellationToken).ConfigureAwait(false);
        var isFaLang = (currentUser?.PreferredLanguage ?? "fa") == "fa";

        // Track user's incoming message
        await TrackIncomingAsync(userId, context.IncomingMessageId, cancellationToken).ConfigureAwait(false);

        // ── Step 1: Name entry ────────────────────────────────────────
        if (state2 == "kyc_step_name")
        {
            var text = context.MessageText?.Trim();
            if (string.IsNullOrEmpty(text))
            {
                var msg = isFaLang ? "لطفاً نام و نام خانوادگی خود را وارد کنید:" : "Please enter your first and last name:";
                await _sender.SendTextMessageAsync(chatId, msg, cancellationToken).ConfigureAwait(false);
                await TrackLastBotMsgAsync(userId, cancellationToken).ConfigureAwait(false);
                return true;
            }

            var parts = text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            var firstName = parts.Length > 0 ? parts[0].Trim() : null;
            var lastName = parts.Length > 1 ? parts[1].Trim() : null;

            if (string.IsNullOrEmpty(firstName) || string.IsNullOrEmpty(lastName))
            {
                var msg = isFaLang
                    ? "لطفاً نام و نام خانوادگی خود را در یک خط وارد کنید.\nمثال: <b>علی احمدی</b>"
                    : "Please enter both first and last name in one line.\nExample: <b>John Smith</b>";
                await _sender.SendTextMessageAsync(chatId, msg, cancellationToken).ConfigureAwait(false);
                await TrackLastBotMsgAsync(userId, cancellationToken).ConfigureAwait(false);
                return true;
            }

            await _userRepo.UpdateProfileAsync(userId, firstName, lastName, null, cancellationToken).ConfigureAwait(false);

            // Check if this is a partial fix (skip to next rejected step)
            var nextFixStep = GetNextFixStep(currentUser?.KycRejectionData, "name");
            if (nextFixStep != null)
            {
                await _stateStore.SetStateAsync(userId, nextFixStep, cancellationToken).ConfigureAwait(false);
                await SendStepPromptAsync(chatId, userId, nextFixStep, isFaLang, cancellationToken).ConfigureAwait(false);
                return true;
            }

            await _stateStore.SetStateAsync(userId, "kyc_step_phone", cancellationToken).ConfigureAwait(false);

            var phoneMsg = isFaLang
                ? $"نام شما ثبت شد: <b>{Esc(firstName)} {Esc(lastName)}</b>\n\nاکنون لطفاً شماره تلفن خود را با زدن دکمه زیر به اشتراک بگذارید:"
                : $"Name saved: <b>{Esc(firstName)} {Esc(lastName)}</b>\n\nNow please share your phone number by pressing the button below:";
            var btnLabel = isFaLang ? "اشتراک‌گذاری شماره تلفن" : "Share Phone Number";
            var cancelLabel = isFaLang ? "لغو" : "Cancel";
            await _sender.SendContactRequestAsync(chatId, phoneMsg, btnLabel, cancelLabel, cancellationToken).ConfigureAwait(false);
            await TrackLastBotMsgAsync(userId, cancellationToken).ConfigureAwait(false);
            return true;
        }

        // ── Step 2: Phone sharing ─────────────────────────────────────
        if (state2 == "kyc_step_phone")
        {
            // Check for cancel
            var txt = context.MessageText?.Trim();
            if (!string.IsNullOrEmpty(txt) && (txt == "لغو" || string.Equals(txt, "Cancel", StringComparison.OrdinalIgnoreCase)))
            {
                await _stateStore.ClearStateAsync(userId, cancellationToken).ConfigureAwait(false);
                await CleanupFlowMessagesAsync(chatId, userId, currentUser?.CleanChatMode ?? true, cancellationToken).ConfigureAwait(false);
                var cancelMsg = isFaLang ? "فرایند احراز هویت لغو شد." : "Verification process cancelled.";
                await _sender.RemoveReplyKeyboardAsync(chatId, cancelMsg, cancellationToken).ConfigureAwait(false);
                var backKeyboard = new List<IReadOnlyList<InlineButton>>
                {
                    new[] { new InlineButton(isFaLang ? "بازگشت" : "Back", "stage:main_menu") }
                };
                await _sender.SendTextMessageWithInlineKeyboardAsync(chatId, cancelMsg, backKeyboard, cancellationToken).ConfigureAwait(false);
                return true;
            }

            if (!context.HasContact)
            {
                var msg = isFaLang ? "لطفاً از دکمه زیر برای اشتراک‌گذاری شماره تلفن استفاده کنید:" : "Please use the button below to share your phone number:";
                await _sender.SendTextMessageAsync(chatId, msg, cancellationToken).ConfigureAwait(false);
                await TrackLastBotMsgAsync(userId, cancellationToken).ConfigureAwait(false);
                return true;
            }

            var phone = context.ContactPhoneNumber!;
            await _userRepo.SetPhoneNumberAsync(userId, phone, cancellationToken).ConfigureAwait(false);

            // Generate OTP
            var otp = new Random().Next(10000, 99999).ToString();
            await _stateStore.SetStateAsync(userId, $"kyc_step_otp:{otp}", cancellationToken).ConfigureAwait(false);

            // Send OTP via SMS
            if (_smsService != null)
            {
                var sent = await _smsService.SendVerificationCodeAsync(phone, otp, cancellationToken).ConfigureAwait(false);
                if (!sent)
                {
                    var errMsg = isFaLang ? "خطا در ارسال کد تأیید. لطفاً دوباره تلاش کنید." : "Error sending verification code. Please try again.";
                    await _stateStore.SetStateAsync(userId, "kyc_step_phone", cancellationToken).ConfigureAwait(false);
                    await _sender.SendTextMessageAsync(chatId, errMsg, cancellationToken).ConfigureAwait(false);
                    await TrackLastBotMsgAsync(userId, cancellationToken).ConfigureAwait(false);
                    return true;
                }
            }

            var masked = MaskPhone(phone);
            var otpMsg = isFaLang
                ? $"شماره تلفن شما ثبت شد.\nکد تأیید به شماره <b>{masked}</b> ارسال شد.\n\nلطفاً کد ۵ رقمی را وارد کنید:"
                : $"Phone number saved.\nVerification code sent to <b>{masked}</b>.\n\nPlease enter the 5-digit code:";
            await _sender.RemoveReplyKeyboardAsync(chatId, otpMsg, cancellationToken).ConfigureAwait(false);
            await TrackLastBotMsgAsync(userId, cancellationToken).ConfigureAwait(false);
            return true;
        }

        // ── Step 3: SMS OTP verification ──────────────────────────────
        if (state2.StartsWith("kyc_step_otp:", StringComparison.OrdinalIgnoreCase))
        {
            var expectedOtp = state2["kyc_step_otp:".Length..];
            var enteredOtp = context.MessageText?.Trim();

            if (string.IsNullOrEmpty(enteredOtp))
            {
                var msg = isFaLang ? "لطفاً کد تأیید ۵ رقمی را وارد کنید:" : "Please enter the 5-digit verification code:";
                await _sender.SendTextMessageAsync(chatId, msg, cancellationToken).ConfigureAwait(false);
                await TrackLastBotMsgAsync(userId, cancellationToken).ConfigureAwait(false);
                return true;
            }

            if (enteredOtp != expectedOtp)
            {
                var msg = isFaLang ? "کد وارد شده صحیح نیست. لطفاً دوباره تلاش کنید:" : "Incorrect code. Please try again:";
                await _sender.SendTextMessageAsync(chatId, msg, cancellationToken).ConfigureAwait(false);
                await TrackLastBotMsgAsync(userId, cancellationToken).ConfigureAwait(false);
                return true;
            }

            // Phone OTP correct → check for partial fix
            var nextFixStep = GetNextFixStep(currentUser?.KycRejectionData, "phone");
            if (nextFixStep != null)
            {
                await _stateStore.SetStateAsync(userId, nextFixStep, cancellationToken).ConfigureAwait(false);
                await SendStepPromptAsync(chatId, userId, nextFixStep, isFaLang, cancellationToken).ConfigureAwait(false);
                return true;
            }

            // Go to email step
            await _stateStore.SetStateAsync(userId, "kyc_step_email", cancellationToken).ConfigureAwait(false);
            var emailMsg = isFaLang
                ? "شماره تلفن شما با موفقیت تأیید شد.\n\nاکنون لطفاً آدرس ایمیل خود را وارد کنید:"
                : "Phone number verified successfully.\n\nNow please enter your email address:";
            await _sender.SendTextMessageAsync(chatId, emailMsg, cancellationToken).ConfigureAwait(false);
            await TrackLastBotMsgAsync(userId, cancellationToken).ConfigureAwait(false);
            return true;
        }

        // ── Step 4: Email entry ───────────────────────────────────────
        if (state2 == "kyc_step_email")
        {
            var email = context.MessageText?.Trim();
            if (string.IsNullOrEmpty(email) || !email.Contains('@') || !email.Contains('.'))
            {
                var msg = isFaLang
                    ? "لطفاً یک آدرس ایمیل معتبر وارد کنید:\nمثال: <b>you@example.com</b>"
                    : "Please enter a valid email address:\nExample: <b>you@example.com</b>";
                await _sender.SendTextMessageAsync(chatId, msg, cancellationToken).ConfigureAwait(false);
                await TrackLastBotMsgAsync(userId, cancellationToken).ConfigureAwait(false);
                return true;
            }

            await _userRepo.SetEmailAsync(userId, email, cancellationToken).ConfigureAwait(false);

            // Generate email OTP
            var otp = new Random().Next(10000, 99999).ToString();
            await _stateStore.SetStateAsync(userId, $"kyc_step_email_otp:{otp}", cancellationToken).ConfigureAwait(false);

            // Send email OTP
            if (_emailService != null)
            {
                var sent = await _emailService.SendVerificationCodeAsync(email, otp, cancellationToken).ConfigureAwait(false);
                if (!sent)
                {
                    var errMsg = isFaLang
                        ? "خطا در ارسال کد تأیید ایمیل. لطفاً دوباره تلاش کنید."
                        : "Error sending email verification code. Please try again.";
                    await _stateStore.SetStateAsync(userId, "kyc_step_email", cancellationToken).ConfigureAwait(false);
                    await _sender.SendTextMessageAsync(chatId, errMsg, cancellationToken).ConfigureAwait(false);
                    await TrackLastBotMsgAsync(userId, cancellationToken).ConfigureAwait(false);
                    return true;
                }
            }

            var maskedEmail = MaskEmail(email);
            var msg2 = isFaLang
                ? $"کد تأیید به ایمیل <b>{maskedEmail}</b> ارسال شد.\n\nلطفاً کد ۵ رقمی را وارد کنید:"
                : $"Verification code sent to <b>{maskedEmail}</b>.\n\nPlease enter the 5-digit code:";
            await _sender.SendTextMessageAsync(chatId, msg2, cancellationToken).ConfigureAwait(false);
            await TrackLastBotMsgAsync(userId, cancellationToken).ConfigureAwait(false);
            return true;
        }

        // ── Step 5: Email OTP verification ────────────────────────────
        if (state2.StartsWith("kyc_step_email_otp:", StringComparison.OrdinalIgnoreCase))
        {
            var expectedOtp = state2["kyc_step_email_otp:".Length..];
            var enteredOtp = context.MessageText?.Trim();

            if (string.IsNullOrEmpty(enteredOtp))
            {
                var msg = isFaLang ? "لطفاً کد تأیید ایمیل ۵ رقمی را وارد کنید:" : "Please enter the 5-digit email verification code:";
                await _sender.SendTextMessageAsync(chatId, msg, cancellationToken).ConfigureAwait(false);
                await TrackLastBotMsgAsync(userId, cancellationToken).ConfigureAwait(false);
                return true;
            }

            if (enteredOtp != expectedOtp)
            {
                var msg = isFaLang ? "کد وارد شده صحیح نیست. لطفاً دوباره تلاش کنید:" : "Incorrect code. Please try again:";
                await _sender.SendTextMessageAsync(chatId, msg, cancellationToken).ConfigureAwait(false);
                await TrackLastBotMsgAsync(userId, cancellationToken).ConfigureAwait(false);
                return true;
            }

            // Email verified
            await _userRepo.SetEmailVerifiedAsync(userId, cancellationToken).ConfigureAwait(false);

            // Check for partial fix
            var nextFixStep = GetNextFixStep(currentUser?.KycRejectionData, "email");
            if (nextFixStep != null)
            {
                await _stateStore.SetStateAsync(userId, nextFixStep, cancellationToken).ConfigureAwait(false);
                await SendStepPromptAsync(chatId, userId, nextFixStep, isFaLang, cancellationToken).ConfigureAwait(false);
                return true;
            }

            // Go to country step
            await _stateStore.SetStateAsync(userId, "kyc_step_country", cancellationToken).ConfigureAwait(false);
            await SendCountrySelectionAsync(chatId, isFaLang, cancellationToken).ConfigureAwait(false);
            return true;
        }

        // ── Step 6a: Country selection (text for "Other") ─────────────
        if (state2 == "kyc_step_country_text")
        {
            var country = context.MessageText?.Trim();
            if (string.IsNullOrEmpty(country))
            {
                var msg = isFaLang ? "لطفاً نام کشور را وارد کنید:" : "Please enter the country name:";
                await _sender.SendTextMessageAsync(chatId, msg, cancellationToken).ConfigureAwait(false);
                await TrackLastBotMsgAsync(userId, cancellationToken).ConfigureAwait(false);
                return true;
            }

            await _userRepo.SetCountryAsync(userId, country, cancellationToken).ConfigureAwait(false);
            await GoToPhotoStepAsync(chatId, userId, isFaLang, cancellationToken).ConfigureAwait(false);
            return true;
        }

        // ── Step 6b: Country selection (inline button handled above as callback) ──

        // ── Step 7: Photo upload ──────────────────────────────────────
        if (state2 == "kyc_step_photo")
        {
            if (!context.HasPhoto)
            {
                var msg = isFaLang
                    ? "لطفاً یک عکس سلفی ارسال کنید (نه فایل).\nعکس باید شامل صورت شما و کاغذی با نوشته <b>AbroadQs</b> باشد."
                    : "Please send a selfie photo (not a file).\nThe photo must show your face and a paper with <b>AbroadQs</b> written on it.";
                await _sender.SendTextMessageAsync(chatId, msg, cancellationToken).ConfigureAwait(false);
                await TrackLastBotMsgAsync(userId, cancellationToken).ConfigureAwait(false);
                return true;
            }

            var photoFileId = context.PhotoFileId!;
            // Save photo but do NOT auto-verify — set pending_review
            await _userRepo.SetVerifiedAsync(userId, photoFileId, cancellationToken).ConfigureAwait(false);
            // Override: set to pending_review (not approved)
            await _userRepo.SetKycStatusAsync(userId, "pending_review", null, cancellationToken).ConfigureAwait(false);
            await _stateStore.ClearStateAsync(userId, cancellationToken).ConfigureAwait(false);

            // Cleanup flow messages
            await CleanupFlowMessagesAsync(chatId, userId, currentUser?.CleanChatMode ?? true, cancellationToken).ConfigureAwait(false);

            var successMsg = isFaLang
                ? "مدارک شما با موفقیت ارسال شد و در انتظار بررسی توسط تیم ماست.\nپس از بررسی، نتیجه به شما اطلاع داده خواهد شد."
                : "Your documents have been submitted for review.\nYou will be notified once the review is complete.";
            var keyboard = new List<IReadOnlyList<InlineButton>>
            {
                new[] { new InlineButton(isFaLang ? "بازگشت به منوی اصلی" : "Back to Main Menu", "stage:main_menu") }
            };
            await _sender.SendTextMessageWithInlineKeyboardAsync(chatId, successMsg, keyboard, cancellationToken).ConfigureAwait(false);
            return true;
        }

        return false;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Country selection
    // ═══════════════════════════════════════════════════════════════════

    private async Task SendCountrySelectionAsync(long chatId, bool isFa, CancellationToken ct)
    {
        var msg = isFa
            ? "ایمیل شما با موفقیت تأیید شد.\n\nاکنون لطفاً کشور محل سکونت خود را انتخاب کنید:"
            : "Email verified successfully.\n\nNow please select your country of residence:";

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
        // 3 per row
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

        await _sender.SendTextMessageWithInlineKeyboardAsync(chatId, msg, keyboard, ct).ConfigureAwait(false);
        await TrackLastBotMsgAsync(chatId, ct).ConfigureAwait(false);
    }

    private static string GetCountryName(string code, bool isFa)
    {
        return code switch
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
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Photo step (with sample image)
    // ═══════════════════════════════════════════════════════════════════

    private async Task GoToPhotoStepAsync(long chatId, long userId, bool isFa, CancellationToken ct)
    {
        // Check for partial fix
        var user = await _userRepo.GetByTelegramUserIdAsync(userId, ct).ConfigureAwait(false);
        var nextFixStep = GetNextFixStep(user?.KycRejectionData, "country");
        if (nextFixStep != null)
        {
            await _stateStore.SetStateAsync(userId, nextFixStep, ct).ConfigureAwait(false);
            await SendStepPromptAsync(chatId, userId, nextFixStep, isFa, ct).ConfigureAwait(false);
            return;
        }

        await _stateStore.SetStateAsync(userId, "kyc_step_photo", ct).ConfigureAwait(false);

        // Send sample photo first
        try
        {
            var photoPath = Path.Combine(AppContext.BaseDirectory, SamplePhotoPath);
            if (File.Exists(photoPath))
            {
                await _sender.SendPhotoAsync(chatId, photoPath,
                    isFa ? "نمونه عکس تأیید هویت" : "Sample verification photo", ct).ConfigureAwait(false);
                await TrackLastBotMsgAsync(userId, ct).ConfigureAwait(false);
            }
        }
        catch { /* if photo send fails, continue without it */ }

        var photoMsg = isFa
            ? "کشور شما ثبت شد.\n\n" +
              "اکنون لطفاً یک عکس سلفی از خود ارسال کنید که در آن:\n" +
              "- یک تکه کاغذ در کنار صورت شما باشد\n" +
              "- روی کاغذ عبارت <b>AbroadQs</b> نوشته شده باشد\n\n" +
              "مطابق تصویر نمونه بالا عکس بگیرید."
            : "Country saved.\n\n" +
              "Now please send a selfie photo where:\n" +
              "- You hold a piece of paper next to your face\n" +
              "- The paper has <b>AbroadQs</b> written on it\n\n" +
              "Take the photo similar to the sample above.";
        await _sender.SendTextMessageAsync(chatId, photoMsg, ct).ConfigureAwait(false);
        await TrackLastBotMsgAsync(userId, ct).ConfigureAwait(false);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Flow message tracking + cleanup
    // ═══════════════════════════════════════════════════════════════════

    private async Task TrackIncomingAsync(long userId, int? messageId, CancellationToken ct)
    {
        if (messageId.HasValue)
            await _stateStore.AddFlowMessageIdAsync(userId, messageId.Value, ct).ConfigureAwait(false);
    }

    private async Task TrackLastBotMsgAsync(long userId, CancellationToken ct)
    {
        if (_msgStateRepo == null) return;
        try
        {
            var state = await _msgStateRepo.GetUserMessageStateAsync(userId, ct).ConfigureAwait(false);
            if (state?.LastBotTelegramMessageId is > 0)
                await _stateStore.AddFlowMessageIdAsync(userId, (int)state.LastBotTelegramMessageId, ct).ConfigureAwait(false);
        }
        catch { /* best effort */ }
    }

    private async Task CleanupFlowMessagesAsync(long chatId, long userId, bool cleanMode, CancellationToken ct)
    {
        if (!cleanMode) return;
        try
        {
            var ids = await _stateStore.GetAndClearFlowMessageIdsAsync(userId, ct).ConfigureAwait(false);
            foreach (var id in ids)
            {
                try { await _sender.DeleteMessageAsync(chatId, id, ct).ConfigureAwait(false); }
                catch { /* swallow individual failures */ }
            }
        }
        catch { /* best effort */ }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Re-submission helpers
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>Ordered list of KYC steps and their rejection field keys.</summary>
    private static readonly (string step, string field)[] KycSteps = new[]
    {
        ("kyc_step_name", "name"),
        ("kyc_step_phone", "phone"),
        ("kyc_step_email", "email"),
        ("kyc_step_country", "country"),
        ("kyc_step_photo", "photo"),
    };

    /// <summary>Get the first rejected step from the rejection JSON.</summary>
    private static string? GetNextRejectedStep(string? rejectionDataJson)
    {
        if (string.IsNullOrEmpty(rejectionDataJson)) return null;
        try
        {
            var dict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(rejectionDataJson);
            if (dict == null || dict.Count == 0) return null;
            foreach (var (step, field) in KycSteps)
            {
                if (dict.ContainsKey(field)) return step;
            }
        }
        catch { }
        return null;
    }

    /// <summary>After completing a step during fix, find the next rejected step or null if done.</summary>
    private static string? GetNextFixStep(string? rejectionDataJson, string completedField)
    {
        if (string.IsNullOrEmpty(rejectionDataJson)) return null;
        try
        {
            var dict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(rejectionDataJson);
            if (dict == null || dict.Count == 0) return null;
            bool foundCompleted = false;
            foreach (var (step, field) in KycSteps)
            {
                if (field == completedField) { foundCompleted = true; continue; }
                if (foundCompleted && dict.ContainsKey(field)) return step;
            }
        }
        catch { }
        return null; // all done → proceed to submission
    }

    /// <summary>Send the prompt for a given KYC step.</summary>
    private async Task SendStepPromptAsync(long chatId, long userId, string step, bool isFa, CancellationToken ct)
    {
        switch (step)
        {
            case "kyc_step_name":
                var nameMsg = isFa
                    ? "لطفاً نام و نام خانوادگی خود را در یک خط وارد کنید:\nمثال: <b>علی احمدی</b>"
                    : "Please enter your first and last name in one line:\nExample: <b>John Smith</b>";
                await _sender.SendTextMessageAsync(chatId, nameMsg, ct).ConfigureAwait(false);
                break;
            case "kyc_step_phone":
                var phoneMsg = isFa
                    ? "لطفاً شماره تلفن خود را با زدن دکمه زیر به اشتراک بگذارید:"
                    : "Please share your phone number by pressing the button below:";
                await _sender.SendContactRequestAsync(chatId, phoneMsg,
                    isFa ? "اشتراک‌گذاری شماره تلفن" : "Share Phone Number",
                    isFa ? "لغو" : "Cancel", ct).ConfigureAwait(false);
                break;
            case "kyc_step_email":
                var emailMsg = isFa
                    ? "لطفاً آدرس ایمیل خود را وارد کنید:"
                    : "Please enter your email address:";
                await _sender.SendTextMessageAsync(chatId, emailMsg, ct).ConfigureAwait(false);
                break;
            case "kyc_step_country":
                await SendCountrySelectionAsync(chatId, isFa, ct).ConfigureAwait(false);
                return; // already tracks
            case "kyc_step_photo":
                await GoToPhotoStepAsync(chatId, userId, isFa, ct).ConfigureAwait(false);
                return; // already tracks
        }
        await TrackLastBotMsgAsync(userId, ct).ConfigureAwait(false);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Utilities
    // ═══════════════════════════════════════════════════════════════════

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
