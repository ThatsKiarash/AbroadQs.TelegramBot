using AbroadQs.Bot.Contracts;

namespace AbroadQs.Bot.Modules.Common;

/// <summary>
/// Handles the "profile" stage display and "awaiting_profile_name" text entry.
/// When user enters "stage:profile", this shows their profile info with edit/verify options.
/// </summary>
public sealed class ProfileStateHandler : IUpdateHandler
{
    private readonly IResponseSender _sender;
    private readonly ITelegramUserRepository _userRepo;
    private readonly IUserConversationStateStore _stateStore;

    public ProfileStateHandler(IResponseSender sender, ITelegramUserRepository userRepo, IUserConversationStateStore stateStore)
    {
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
        _userRepo = userRepo ?? throw new ArgumentNullException(nameof(userRepo));
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
    }

    public string? Command => null;

    public bool CanHandle(BotUpdateContext context)
    {
        if (context.UserId == null || context.IsCallbackQuery) return false;
        if (string.IsNullOrWhiteSpace(context.MessageText)) return false;
        if (context.Command != null) return false;
        return true;
    }

    public async Task<bool> HandleAsync(BotUpdateContext context, CancellationToken cancellationToken)
    {
        if (context.UserId == null) return false;
        var state = await _stateStore.GetStateAsync(context.UserId.Value, cancellationToken).ConfigureAwait(false);
        if (state != "awaiting_profile_name") return false;

        var text = context.MessageText!.Trim();
        var parts = text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var firstName = parts.Length > 0 ? parts[0].Trim() : null;
        var lastName = parts.Length > 1 ? parts[1].Trim() : null;

        await _stateStore.ClearStateAsync(context.UserId.Value, cancellationToken).ConfigureAwait(false);
        await _userRepo.UpdateProfileAsync(context.UserId.Value, firstName, lastName, null, cancellationToken).ConfigureAwait(false);

        var user = await _userRepo.GetByTelegramUserIdAsync(context.UserId.Value, cancellationToken).ConfigureAwait(false);
        var lang = user?.PreferredLanguage;
        var isFa = lang == "fa";
        var saved = isFa
            ? $"نام شما ذخیره شد.\nنام: {Escape(firstName ?? "—")}\nنام خانوادگی: {Escape(lastName ?? "—")}"
            : $"Name saved.\nFirst name: {Escape(firstName ?? "—")}\nLast name: {Escape(lastName ?? "—")}";
        var back = isFa ? "بازگشت" : "Back";
        await _sender.SendTextMessageWithInlineKeyboardAsync(context.ChatId, saved,
            new[] { new[] { new InlineButton(back, "stage:profile") } }, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>Build profile info text with inline buttons. Called from DynamicStageHandler.</summary>
    public static (string text, List<IReadOnlyList<InlineButton>> keyboard) BuildProfileView(TelegramUserDto? user, bool isFa)
    {
        var name = $"{Escape(user?.FirstName ?? "—")} {Escape(user?.LastName ?? "—")}";
        var phone = user?.PhoneNumber != null ? user.PhoneNumber : (isFa ? "ثبت نشده" : "Not set");
        var verifiedLabel = user?.IsVerified == true
            ? (isFa ? "تأیید شده" : "Verified")
            : (isFa ? "تأیید نشده" : "Not verified");

        var text = isFa
            ? $"<b>پروفایل من</b>\n\n" +
              $"نام: <b>{name}</b>\n" +
              $"شماره تلفن: <b>{Escape(phone)}</b>\n" +
              $"وضعیت احراز هویت: <b>{verifiedLabel}</b>"
            : $"<b>My Profile</b>\n\n" +
              $"Name: <b>{name}</b>\n" +
              $"Phone: <b>{Escape(phone)}</b>\n" +
              $"Verification: <b>{verifiedLabel}</b>";

        var keyboard = new List<IReadOnlyList<InlineButton>>();

        if (user?.IsVerified != true)
        {
            // Not verified: show edit name + start KYC
            keyboard.Add(new[] { new InlineButton(isFa ? "ویرایش نام" : "Edit Name", "stage:profile_edit_name") });
            keyboard.Add(new[] { new InlineButton(isFa ? "شروع احراز هویت" : "Start Verification", "start_kyc") });
        }
        // Always show back button
        keyboard.Add(new[] { new InlineButton(isFa ? "بازگشت" : "Back", "stage:main_menu") });

        return (text, keyboard);
    }

    private static string Escape(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
