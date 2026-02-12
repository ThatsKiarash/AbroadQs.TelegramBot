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
        if (context.UserId == null) return false;
        // Handle callbacks: profile_edit:*, view_profile:*
        if (context.IsCallbackQuery)
        {
            var cb = context.MessageText?.Trim() ?? "";
            return cb.StartsWith("profile_edit:", StringComparison.Ordinal)
                || cb.StartsWith("view_profile:", StringComparison.Ordinal);
        }
        if (string.IsNullOrWhiteSpace(context.MessageText)) return false;
        if (context.Command != null) return false;
        return true;
    }

    public async Task<bool> HandleAsync(BotUpdateContext context, CancellationToken cancellationToken)
    {
        if (context.UserId == null) return false;
        var userId = context.UserId.Value;
        var chatId = context.ChatId;

        // ── Callback queries ──────────────────────────────────
        if (context.IsCallbackQuery)
        {
            var cb = context.MessageText?.Trim() ?? "";
            if (context.CallbackQueryId != null)
                try { await _sender.AnswerCallbackQueryAsync(context.CallbackQueryId, null, cancellationToken).ConfigureAwait(false); } catch { }
            var eid = context.CallbackMessageId;

            // view_profile:{targetUserId} — Phase 3: public profile
            if (cb.StartsWith("view_profile:"))
            {
                if (long.TryParse(cb["view_profile:".Length..], out var targetId))
                {
                    var target = await _userRepo.GetByTelegramUserIdAsync(targetId, cancellationToken).ConfigureAwait(false);
                    var user2 = await _userRepo.GetByTelegramUserIdAsync(userId, cancellationToken).ConfigureAwait(false);
                    var isFa2 = (user2?.PreferredLanguage ?? "fa") == "fa";
                    var (txt, kb) = BuildPublicProfileView(target, isFa2);
                    if (eid.HasValue) try { await _sender.EditMessageTextWithInlineKeyboardAsync(chatId, eid.Value, txt, kb, cancellationToken).ConfigureAwait(false); return true; } catch { }
                    await _sender.SendTextMessageWithInlineKeyboardAsync(chatId, txt, kb, cancellationToken).ConfigureAwait(false);
                }
                return true;
            }

            // profile_edit:bio — start bio editing
            if (cb == "profile_edit:bio")
            {
                if (eid.HasValue) try { await _sender.DeleteMessageAsync(chatId, eid.Value, cancellationToken).ConfigureAwait(false); } catch { }
                await _stateStore.SetStateAsync(userId, "awaiting_profile_bio", cancellationToken).ConfigureAwait(false);
                var user2 = await _userRepo.GetByTelegramUserIdAsync(userId, cancellationToken).ConfigureAwait(false);
                var isFa2 = (user2?.PreferredLanguage ?? "fa") == "fa";
                var msg = isFa2 ? "📝 بیوی خود را بنویسید (حداکثر ۵۰۰ کاراکتر):" : "📝 Write your bio (max 500 characters):";
                await _sender.SendTextMessageAsync(chatId, msg, cancellationToken).ConfigureAwait(false);
                return true;
            }

            // profile_edit:links — show link editing sub-menu
            if (cb == "profile_edit:links")
            {
                var user2 = await _userRepo.GetByTelegramUserIdAsync(userId, cancellationToken).ConfigureAwait(false);
                var isFa2 = (user2?.PreferredLanguage ?? "fa") == "fa";
                var linkKb = new List<IReadOnlyList<InlineButton>>
                {
                    new[] { new InlineButton("GitHub", "profile_edit:github"), new InlineButton("LinkedIn", "profile_edit:linkedin") },
                    new[] { new InlineButton("Instagram", "profile_edit:instagram") },
                    new[] { new InlineButton(isFa2 ? "🔙 بازگشت" : "🔙 Back", "stage:profile") },
                };
                var linkTxt = isFa2 ? "<b>🔗 ویرایش لینک‌ها</b>\n\nکدام لینک را می‌خواهید ویرایش کنید؟" : "<b>🔗 Edit Links</b>\n\nWhich link do you want to edit?";
                if (eid.HasValue) try { await _sender.EditMessageTextWithInlineKeyboardAsync(chatId, eid.Value, linkTxt, linkKb, cancellationToken).ConfigureAwait(false); return true; } catch { }
                await _sender.SendTextMessageWithInlineKeyboardAsync(chatId, linkTxt, linkKb, cancellationToken).ConfigureAwait(false);
                return true;
            }

            // profile_edit:github/linkedin/instagram — start link editing
            if (cb == "profile_edit:github" || cb == "profile_edit:linkedin" || cb == "profile_edit:instagram")
            {
                var linkType = cb["profile_edit:".Length..];
                if (eid.HasValue) try { await _sender.DeleteMessageAsync(chatId, eid.Value, cancellationToken).ConfigureAwait(false); } catch { }
                await _stateStore.SetStateAsync(userId, $"awaiting_profile_{linkType}", cancellationToken).ConfigureAwait(false);
                var user2 = await _userRepo.GetByTelegramUserIdAsync(userId, cancellationToken).ConfigureAwait(false);
                var isFa2 = (user2?.PreferredLanguage ?? "fa") == "fa";
                var msg = isFa2 ? $"🔗 لینک {linkType} خود را ارسال کنید:" : $"🔗 Send your {linkType} URL:";
                await _sender.SendTextMessageAsync(chatId, msg, cancellationToken).ConfigureAwait(false);
                return true;
            }

            return false;
        }

        // ── Text input states ─────────────────────────────────
        var state = await _stateStore.GetStateAsync(userId, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrEmpty(state) || !state.StartsWith("awaiting_profile_")) return false;

        var text = context.MessageText!.Trim();
        var user = await _userRepo.GetByTelegramUserIdAsync(userId, cancellationToken).ConfigureAwait(false);
        var lang = user?.PreferredLanguage;
        var isFa = (lang ?? "fa") == "fa";

        switch (state)
        {
            case "awaiting_profile_name":
            {
                var parts = text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                var firstName = parts.Length > 0 ? parts[0].Trim() : null;
                var lastName = parts.Length > 1 ? parts[1].Trim() : null;
                await _stateStore.ClearStateAsync(userId, cancellationToken).ConfigureAwait(false);
                await _userRepo.UpdateProfileAsync(userId, firstName, lastName, null, cancellationToken).ConfigureAwait(false);
                var saved = isFa
                    ? $"نام شما ذخیره شد.\nنام: {Escape(firstName ?? "—")}\nنام خانوادگی: {Escape(lastName ?? "—")}"
                    : $"Name saved.\nFirst name: {Escape(firstName ?? "—")}\nLast name: {Escape(lastName ?? "—")}";
                await _sender.SendTextMessageWithInlineKeyboardAsync(chatId, saved,
                    new[] { new[] { new InlineButton(isFa ? "بازگشت" : "Back", "stage:profile") } }, cancellationToken).ConfigureAwait(false);
                return true;
            }
            case "awaiting_profile_bio":
            {
                await _stateStore.ClearStateAsync(userId, cancellationToken).ConfigureAwait(false);
                await _userRepo.SetBioAsync(userId, text.Length > 500 ? text[..500] : text, cancellationToken).ConfigureAwait(false);
                var msg = isFa ? "✅ بیو ذخیره شد." : "✅ Bio saved.";
                await _sender.SendTextMessageWithInlineKeyboardAsync(chatId, msg,
                    new[] { new[] { new InlineButton(isFa ? "بازگشت" : "Back", "stage:profile") } }, cancellationToken).ConfigureAwait(false);
                return true;
            }
            case "awaiting_profile_github":
            {
                await _stateStore.ClearStateAsync(userId, cancellationToken).ConfigureAwait(false);
                await _userRepo.SetGitHubUrlAsync(userId, text, cancellationToken).ConfigureAwait(false);
                var msg = isFa ? "✅ لینک GitHub ذخیره شد." : "✅ GitHub URL saved.";
                await _sender.SendTextMessageWithInlineKeyboardAsync(chatId, msg,
                    new[] { new[] { new InlineButton(isFa ? "بازگشت" : "Back", "stage:profile") } }, cancellationToken).ConfigureAwait(false);
                return true;
            }
            case "awaiting_profile_linkedin":
            {
                await _stateStore.ClearStateAsync(userId, cancellationToken).ConfigureAwait(false);
                await _userRepo.SetLinkedInUrlAsync(userId, text, cancellationToken).ConfigureAwait(false);
                var msg = isFa ? "✅ لینک LinkedIn ذخیره شد." : "✅ LinkedIn URL saved.";
                await _sender.SendTextMessageWithInlineKeyboardAsync(chatId, msg,
                    new[] { new[] { new InlineButton(isFa ? "بازگشت" : "Back", "stage:profile") } }, cancellationToken).ConfigureAwait(false);
                return true;
            }
            case "awaiting_profile_instagram":
            {
                await _stateStore.ClearStateAsync(userId, cancellationToken).ConfigureAwait(false);
                await _userRepo.SetInstagramUrlAsync(userId, text, cancellationToken).ConfigureAwait(false);
                var msg = isFa ? "✅ لینک Instagram ذخیره شد." : "✅ Instagram URL saved.";
                await _sender.SendTextMessageWithInlineKeyboardAsync(chatId, msg,
                    new[] { new[] { new InlineButton(isFa ? "بازگشت" : "Back", "stage:profile") } }, cancellationToken).ConfigureAwait(false);
                return true;
            }
        }
        return false;
    }

    /// <summary>Build profile info text with inline buttons. Called from DynamicStageHandler.</summary>
    public static (string text, List<IReadOnlyList<InlineButton>> keyboard) BuildProfileView(TelegramUserDto? user, bool isFa)
    {
        var name = $"{Escape(user?.FirstName ?? "—")} {Escape(user?.LastName ?? "—")}";
        var phone = user?.PhoneNumber != null ? user.PhoneNumber : (isFa ? "ثبت نشده" : "Not set");
        var email = user?.Email != null ? user.Email : (isFa ? "ثبت نشده" : "Not set");
        var emailVerified = user?.EmailVerified == true ? (isFa ? " (تأیید شده)" : " (verified)") : "";
        var country = user?.Country ?? (isFa ? "ثبت نشده" : "Not set");
        var bio = user?.Bio ?? (isFa ? "ثبت نشده" : "Not set");
        var github = user?.GitHubUrl ?? "";
        var linkedin = user?.LinkedInUrl ?? "";
        var instagram = user?.InstagramUrl ?? "";

        var kycStatus = user?.KycStatus ?? "none";
        var verifiedLabel = kycStatus switch
        {
            "approved" => isFa ? "✅ تأیید شده" : "✅ Verified",
            "pending_review" => isFa ? "🟡 در انتظار بررسی" : "🟡 Pending Review",
            "rejected" => isFa ? "🔴 رد شده" : "🔴 Rejected",
            _ => isFa ? "⚪ تأیید نشده" : "⚪ Not verified"
        };

        // Phase 3: Profile completion %
        var completionPct = CalcCompletion(user);
        var bar = completionPct >= 80 ? "🟢" : completionPct >= 50 ? "🟡" : "🔴";

        var text = isFa
            ? $"<b>پروفایل من</b>\n━━━━━━━━━━━━━━━━━━━\n\n" +
              $"👤 نام: <b>{name}</b>\n" +
              $"📱 شماره تلفن: <b>{Escape(phone)}</b>\n" +
              $"📧 ایمیل: <b>{Escape(email)}{emailVerified}</b>\n" +
              $"🌍 کشور: <b>{Escape(country)}</b>\n" +
              $"🔐 احراز هویت: <b>{verifiedLabel}</b>\n\n" +
              $"📝 بیو: {Escape(bio)}\n" +
              (!string.IsNullOrEmpty(github) ? $"🔗 GitHub: {Escape(github)}\n" : "") +
              (!string.IsNullOrEmpty(linkedin) ? $"🔗 LinkedIn: {Escape(linkedin)}\n" : "") +
              (!string.IsNullOrEmpty(instagram) ? $"🔗 Instagram: {Escape(instagram)}\n" : "") +
              $"\n{bar} تکمیل پروفایل: <b>{completionPct}%</b>"
            : $"<b>My Profile</b>\n━━━━━━━━━━━━━━━━━━━\n\n" +
              $"👤 Name: <b>{name}</b>\n" +
              $"📱 Phone: <b>{Escape(phone)}</b>\n" +
              $"📧 Email: <b>{Escape(email)}{emailVerified}</b>\n" +
              $"🌍 Country: <b>{Escape(country)}</b>\n" +
              $"🔐 Verification: <b>{verifiedLabel}</b>\n\n" +
              $"📝 Bio: {Escape(bio)}\n" +
              (!string.IsNullOrEmpty(github) ? $"🔗 GitHub: {Escape(github)}\n" : "") +
              (!string.IsNullOrEmpty(linkedin) ? $"🔗 LinkedIn: {Escape(linkedin)}\n" : "") +
              (!string.IsNullOrEmpty(instagram) ? $"🔗 Instagram: {Escape(instagram)}\n" : "") +
              $"\n{bar} Profile completion: <b>{completionPct}%</b>";

        var keyboard = new List<IReadOnlyList<InlineButton>>();

        if (!string.Equals(kycStatus, "approved", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(kycStatus, "rejected", StringComparison.OrdinalIgnoreCase))
            {
                keyboard.Add(new[] { new InlineButton(isFa ? "اصلاح و ارسال مجدد" : "Fix and Resubmit", "start_kyc_fix") });
            }
            else if (string.Equals(kycStatus, "pending_review", StringComparison.OrdinalIgnoreCase))
            {
                // Pending review: status only
            }
            else
            {
                keyboard.Add(new[] { new InlineButton(isFa ? "ویرایش نام" : "Edit Name", "stage:profile_edit_name") });
                keyboard.Add(new[] { new InlineButton(isFa ? "شروع احراز هویت" : "Start Verification", "start_kyc") });
            }
        }
        // Phase 3: Edit profile fields
        keyboard.Add(new[]
        {
            new InlineButton(isFa ? "📝 بیو" : "📝 Bio", "profile_edit:bio"),
            new InlineButton(isFa ? "🔗 لینک‌ها" : "🔗 Links", "profile_edit:links"),
        });
        keyboard.Add(new[] { new InlineButton(isFa ? "بازگشت" : "Back", "stage:main_menu") });

        return (text, keyboard);
    }

    /// <summary>Build a public profile view for any user (Phase 3).</summary>
    public static (string text, List<IReadOnlyList<InlineButton>> keyboard) BuildPublicProfileView(TelegramUserDto? user, bool isFa)
    {
        if (user == null)
        {
            var notFound = isFa ? "⚠️ کاربر یافت نشد." : "⚠️ User not found.";
            return (notFound, new List<IReadOnlyList<InlineButton>> { new[] { new InlineButton(isFa ? "بازگشت" : "Back", "stage:main_menu") } });
        }
        var name = $"{Escape(user.FirstName ?? "—")} {Escape(user.LastName ?? "—")}";
        var country = user.Country ?? (isFa ? "نامشخص" : "Unknown");
        var bio = user.Bio ?? "";
        var kycBadge = string.Equals(user.KycStatus, "approved", StringComparison.OrdinalIgnoreCase)
            ? (isFa ? "✅ تأیید شده" : "✅ Verified") : (isFa ? "⚪ تأیید نشده" : "⚪ Not verified");

        var text = isFa
            ? $"<b>👤 پروفایل عمومی</b>\n━━━━━━━━━━━━━━━━━━━\n\n" +
              $"نام: <b>{name}</b>\n🌍 کشور: {Escape(country)}\n🔐 {kycBadge}\n" +
              (!string.IsNullOrEmpty(bio) ? $"\n📝 {Escape(bio)}\n" : "") +
              (!string.IsNullOrEmpty(user.GitHubUrl) ? $"🔗 GitHub: {Escape(user.GitHubUrl)}\n" : "") +
              (!string.IsNullOrEmpty(user.LinkedInUrl) ? $"🔗 LinkedIn: {Escape(user.LinkedInUrl)}\n" : "") +
              (!string.IsNullOrEmpty(user.InstagramUrl) ? $"🔗 Instagram: {Escape(user.InstagramUrl)}\n" : "")
            : $"<b>👤 Public Profile</b>\n━━━━━━━━━━━━━━━━━━━\n\n" +
              $"Name: <b>{name}</b>\n🌍 Country: {Escape(country)}\n🔐 {kycBadge}\n" +
              (!string.IsNullOrEmpty(bio) ? $"\n📝 {Escape(bio)}\n" : "") +
              (!string.IsNullOrEmpty(user.GitHubUrl) ? $"🔗 GitHub: {Escape(user.GitHubUrl)}\n" : "") +
              (!string.IsNullOrEmpty(user.LinkedInUrl) ? $"🔗 LinkedIn: {Escape(user.LinkedInUrl)}\n" : "") +
              (!string.IsNullOrEmpty(user.InstagramUrl) ? $"🔗 Instagram: {Escape(user.InstagramUrl)}\n" : "");

        var kb = new List<IReadOnlyList<InlineButton>> { new[] { new InlineButton(isFa ? "بازگشت" : "Back", "stage:main_menu") } };
        return (text, kb);
    }

    /// <summary>Calculate profile completion percentage (Phase 3).</summary>
    public static int CalcCompletion(TelegramUserDto? u)
    {
        if (u == null) return 0;
        int score = 0, total = 8;
        if (!string.IsNullOrEmpty(u.FirstName)) score++;
        if (!string.IsNullOrEmpty(u.LastName)) score++;
        if (!string.IsNullOrEmpty(u.PhoneNumber)) score++;
        if (!string.IsNullOrEmpty(u.Email)) score++;
        if (!string.IsNullOrEmpty(u.Country)) score++;
        if (string.Equals(u.KycStatus, "approved", StringComparison.OrdinalIgnoreCase)) score++;
        if (!string.IsNullOrEmpty(u.Bio)) score++;
        if (!string.IsNullOrEmpty(u.GitHubUrl) || !string.IsNullOrEmpty(u.LinkedInUrl) || !string.IsNullOrEmpty(u.InstagramUrl)) score++;
        return (int)Math.Round(score * 100.0 / total);
    }

    private static string Escape(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
