using AbroadQs.Bot.Contracts;

namespace AbroadQs.Bot.Modules.Common;

/// <summary>
/// Handles multi-step KYC (identity verification) flow:
///   kyc_step_name  → user enters first + last name
///   kyc_step_phone → user shares phone contact
///   kyc_step_otp:XXXX → user enters OTP code (XXXX = expected)
///   kyc_step_photo → user sends selfie with paper
/// </summary>
public sealed class KycStateHandler : IUpdateHandler
{
    private readonly IResponseSender _sender;
    private readonly ITelegramUserRepository _userRepo;
    private readonly IUserConversationStateStore _stateStore;
    private readonly ISmsService? _smsService;

    public KycStateHandler(
        IResponseSender sender,
        ITelegramUserRepository userRepo,
        IUserConversationStateStore stateStore,
        ISmsService? smsService = null)
    {
        _sender = sender;
        _userRepo = userRepo;
        _stateStore = stateStore;
        _smsService = smsService;
    }

    public string? Command => null;

    public bool CanHandle(BotUpdateContext context)
    {
        if (context.UserId == null || context.IsCallbackQuery) return false;
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

        var state = await _stateStore.GetStateAsync(userId, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrEmpty(state) || !state.StartsWith("kyc_step_", StringComparison.OrdinalIgnoreCase))
            return false;

        var user = await _userRepo.GetByTelegramUserIdAsync(userId, cancellationToken).ConfigureAwait(false);
        var isFa = (user?.PreferredLanguage ?? "fa") == "fa";

        // ── Step 1: Name entry ────────────────────────────────────────
        if (state == "kyc_step_name")
        {
            var text = context.MessageText?.Trim();
            if (string.IsNullOrEmpty(text))
            {
                var msg = isFa ? "لطفاً نام و نام خانوادگی خود را وارد کنید:" : "Please enter your first and last name:";
                await _sender.SendTextMessageAsync(chatId, msg, cancellationToken).ConfigureAwait(false);
                return true;
            }

            var parts = text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            var firstName = parts.Length > 0 ? parts[0].Trim() : null;
            var lastName = parts.Length > 1 ? parts[1].Trim() : null;

            if (string.IsNullOrEmpty(firstName) || string.IsNullOrEmpty(lastName))
            {
                var msg = isFa
                    ? "لطفاً نام و نام خانوادگی خود را در یک خط وارد کنید.\nمثال: <b>علی احمدی</b>"
                    : "Please enter both first and last name in one line.\nExample: <b>John Smith</b>";
                await _sender.SendTextMessageAsync(chatId, msg, cancellationToken).ConfigureAwait(false);
                return true;
            }

            await _userRepo.UpdateProfileAsync(userId, firstName, lastName, null, cancellationToken).ConfigureAwait(false);
            await _stateStore.SetStateAsync(userId, "kyc_step_phone", cancellationToken).ConfigureAwait(false);

            var phoneMsg = isFa
                ? $"نام شما ثبت شد: <b>{Esc(firstName)} {Esc(lastName)}</b>\n\nاکنون لطفاً شماره تلفن خود را با زدن دکمه زیر به اشتراک بگذارید:"
                : $"Name saved: <b>{Esc(firstName)} {Esc(lastName)}</b>\n\nNow please share your phone number by pressing the button below:";
            var btnLabel = isFa ? "اشتراک‌گذاری شماره تلفن" : "Share Phone Number";
            var cancelLabel = isFa ? "لغو" : "Cancel";
            await _sender.SendContactRequestAsync(chatId, phoneMsg, btnLabel, cancelLabel, cancellationToken).ConfigureAwait(false);
            return true;
        }

        // ── Step 2: Phone sharing ─────────────────────────────────────
        if (state == "kyc_step_phone")
        {
            // Check for cancel
            var txt = context.MessageText?.Trim();
            if (!string.IsNullOrEmpty(txt) && (txt == "لغو" || string.Equals(txt, "Cancel", StringComparison.OrdinalIgnoreCase)))
            {
                await _stateStore.ClearStateAsync(userId, cancellationToken).ConfigureAwait(false);
                var cancelMsg = isFa ? "فرایند احراز هویت لغو شد." : "Verification process cancelled.";
                await _sender.RemoveReplyKeyboardAsync(chatId, cancelMsg, cancellationToken).ConfigureAwait(false);

                var backKeyboard = new List<IReadOnlyList<InlineButton>>
                {
                    new[] { new InlineButton(isFa ? "بازگشت" : "Back", "stage:main_menu") }
                };
                await _sender.SendTextMessageWithInlineKeyboardAsync(chatId, cancelMsg, backKeyboard, cancellationToken).ConfigureAwait(false);
                return true;
            }

            if (!context.HasContact)
            {
                var msg = isFa ? "لطفاً از دکمه زیر برای اشتراک‌گذاری شماره تلفن استفاده کنید:" : "Please use the button below to share your phone number:";
                await _sender.SendTextMessageAsync(chatId, msg, cancellationToken).ConfigureAwait(false);
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
                    var errMsg = isFa ? "خطا در ارسال کد تأیید. لطفاً دوباره تلاش کنید." : "Error sending verification code. Please try again.";
                    await _stateStore.SetStateAsync(userId, "kyc_step_phone", cancellationToken).ConfigureAwait(false);
                    await _sender.SendTextMessageAsync(chatId, errMsg, cancellationToken).ConfigureAwait(false);
                    return true;
                }
            }

            var masked = MaskPhone(phone);
            var otpMsg = isFa
                ? $"شماره تلفن شما ثبت شد.\nکد تأیید به شماره <b>{masked}</b> ارسال شد.\n\nلطفاً کد ۵ رقمی را وارد کنید:"
                : $"Phone number saved.\nVerification code sent to <b>{masked}</b>.\n\nPlease enter the 5-digit code:";
            await _sender.RemoveReplyKeyboardAsync(chatId, otpMsg, cancellationToken).ConfigureAwait(false);
            return true;
        }

        // ── Step 3: OTP verification ──────────────────────────────────
        if (state.StartsWith("kyc_step_otp:", StringComparison.OrdinalIgnoreCase))
        {
            var expectedOtp = state["kyc_step_otp:".Length..];
            var enteredOtp = context.MessageText?.Trim();

            if (string.IsNullOrEmpty(enteredOtp))
            {
                var msg = isFa ? "لطفاً کد تأیید ۵ رقمی را وارد کنید:" : "Please enter the 5-digit verification code:";
                await _sender.SendTextMessageAsync(chatId, msg, cancellationToken).ConfigureAwait(false);
                return true;
            }

            if (enteredOtp != expectedOtp)
            {
                var msg = isFa ? "کد وارد شده صحیح نیست. لطفاً دوباره تلاش کنید:" : "Incorrect code. Please try again:";
                await _sender.SendTextMessageAsync(chatId, msg, cancellationToken).ConfigureAwait(false);
                return true;
            }

            // OTP correct → go to photo step
            await _stateStore.SetStateAsync(userId, "kyc_step_photo", cancellationToken).ConfigureAwait(false);

            var photoMsg = isFa
                ? "شماره تلفن شما با موفقیت تأیید شد.\n\n" +
                  "اکنون لطفاً یک عکس سلفی از خود ارسال کنید که در آن:\n" +
                  "• یک تکه کاغذ در کنار صورت شما باشد\n" +
                  "• روی کاغذ عبارت <b>AbroadQs</b> نوشته شده باشد\n\n" +
                  "این مرحله برای تأیید هویت شما ضروری است."
                : "Phone number verified successfully.\n\n" +
                  "Now please send a selfie photo where:\n" +
                  "• You hold a piece of paper next to your face\n" +
                  "• The paper has <b>AbroadQs</b> written on it\n\n" +
                  "This step is required for identity verification.";
            await _sender.SendTextMessageAsync(chatId, photoMsg, cancellationToken).ConfigureAwait(false);
            return true;
        }

        // ── Step 4: Photo upload ──────────────────────────────────────
        if (state == "kyc_step_photo")
        {
            if (!context.HasPhoto)
            {
                var msg = isFa
                    ? "لطفاً یک عکس سلفی ارسال کنید (نه فایل).\nعکس باید شامل صورت شما و کاغذی با نوشته <b>AbroadQs</b> باشد."
                    : "Please send a selfie photo (not a file).\nThe photo must show your face and a paper with <b>AbroadQs</b> written on it.";
                await _sender.SendTextMessageAsync(chatId, msg, cancellationToken).ConfigureAwait(false);
                return true;
            }

            var photoFileId = context.PhotoFileId!;
            await _userRepo.SetVerifiedAsync(userId, photoFileId, cancellationToken).ConfigureAwait(false);
            await _stateStore.ClearStateAsync(userId, cancellationToken).ConfigureAwait(false);

            var successMsg = isFa
                ? "احراز هویت شما با موفقیت انجام شد!\n\nاکنون می‌توانید از تمامی خدمات تبادل ارز استفاده کنید."
                : "Your identity has been verified successfully!\n\nYou can now use all currency exchange services.";
            var keyboard = new List<IReadOnlyList<InlineButton>>
            {
                new[] { new InlineButton(isFa ? "بازگشت به منوی اصلی" : "Back to Main Menu", "stage:main_menu") }
            };
            await _sender.SendTextMessageWithInlineKeyboardAsync(chatId, successMsg, keyboard, cancellationToken).ConfigureAwait(false);
            return true;
        }

        return false;
    }

    private static string Esc(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    private static string MaskPhone(string phone)
    {
        if (phone.Length <= 4) return "****";
        return phone[..3] + new string('*', phone.Length - 5) + phone[^2..];
    }
}
