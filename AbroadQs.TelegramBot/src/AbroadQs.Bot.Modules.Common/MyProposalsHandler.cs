using AbroadQs.Bot.Contracts;
using static AbroadQs.Bot.Contracts.BilingualHelper;

namespace AbroadQs.Bot.Modules.Common;

/// <summary>
/// Phase 4: My Proposals — aggregates user's bids/proposals across all modules.
/// Callback prefix: myprop_
/// </summary>
public sealed class MyProposalsHandler : IUpdateHandler
{
    private readonly IResponseSender _sender;
    private readonly ITelegramUserRepository _userRepo;
    private readonly IBidRepository? _bidRepo;
    private readonly IProjectBidRepository? _projectBidRepo;

    public MyProposalsHandler(IResponseSender sender, ITelegramUserRepository userRepo,
        IBidRepository? bidRepo = null, IProjectBidRepository? projectBidRepo = null)
    {
        _sender = sender; _userRepo = userRepo; _bidRepo = bidRepo; _projectBidRepo = projectBidRepo;
    }

    public string? Command => null;

    public bool CanHandle(BotUpdateContext context)
    {
        if (context.UserId == null || !context.IsCallbackQuery) return false;
        var cb = context.MessageText?.Trim() ?? "";
        return cb.StartsWith("myprop_", StringComparison.Ordinal);
    }

    public async Task<bool> HandleAsync(BotUpdateContext context, CancellationToken ct)
    {
        if (context.UserId == null) return false;
        var userId = context.UserId.Value;
        var chatId = context.ChatId;
        var user = await SafeGetUser(userId, ct);
        var lang = user?.PreferredLanguage;
        var cb = context.MessageText?.Trim() ?? "";
        if (context.CallbackQueryId != null) try { await _sender.AnswerCallbackQueryAsync(context.CallbackQueryId, null, ct).ConfigureAwait(false); } catch { }
        var eid = context.CallbackMessageId;

        if (cb == "myprop_menu") { await ShowMenu(chatId, userId, lang, eid, ct); return true; }
        if (cb == "myprop_exchange") { await ShowExchangeBids(chatId, userId, lang, 0, eid, ct); return true; }
        if (cb.StartsWith("myprop_exc_p:")) { int.TryParse(cb["myprop_exc_p:".Length..], out var p); await ShowExchangeBids(chatId, userId, lang, p, eid, ct); return true; }
        if (cb == "myprop_project") { await ShowProjectBids(chatId, userId, lang, 0, eid, ct); return true; }
        if (cb.StartsWith("myprop_proj_p:")) { int.TryParse(cb["myprop_proj_p:".Length..], out var p2); await ShowProjectBids(chatId, userId, lang, p2, eid, ct); return true; }
        return false;
    }

    public async Task ShowMenu(long chatId, long userId, string? lang, int? editMsgId, CancellationToken ct)
    {
        var text = L("<b>📋 پیشنهادات من</b>\n━━━━━━━━━━━━━━━━━━━\n\nپیشنهادات ارسال‌شده در بخش‌های مختلف:",
                     "<b>📋 My Proposals</b>\n━━━━━━━━━━━━━━━━━━━\n\nProposals submitted across modules:", lang);
        var kb = new List<IReadOnlyList<InlineButton>>
        {
            new[] { new InlineButton(L("💱 پیشنهادات تبادل", "💱 Exchange Bids", lang), "myprop_exchange") },
            new[] { new InlineButton(L("📁 پیشنهادات پروژه", "📁 Project Proposals", lang), "myprop_project") },
            new[] { new InlineButton(L("🔙 بازگشت", "🔙 Back", lang), "stage:main_menu") },
        };
        if (editMsgId.HasValue) try { await _sender.EditMessageTextWithInlineKeyboardAsync(chatId, editMsgId.Value, text, kb, ct).ConfigureAwait(false); return; } catch { }
        try { await _sender.SendTextMessageWithInlineKeyboardAsync(chatId, text, kb, ct).ConfigureAwait(false); } catch { }
    }

    private async Task ShowExchangeBids(long chatId, long userId, string? lang, int page, int? editMsgId, CancellationToken ct)
    {
        if (_bidRepo == null) return;
        var bids = await _bidRepo.ListBidsByUserAsync(userId, page, 10, ct).ConfigureAwait(false);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(L("<b>💱 پیشنهادات تبادل</b>", "<b>💱 Exchange Bids</b>", lang));
        sb.AppendLine("━━━━━━━━━━━━━━━━━━━\n");
        if (bids.Count == 0) sb.AppendLine(L("📭 پیشنهادی یافت نشد.", "📭 No bids found.", lang));
        foreach (var b in bids)
        {
            var statusIcon = b.Status switch { "accepted" => "✅", "rejected" => "❌", _ => "🟡" };
            sb.AppendLine($"{statusIcon} #{b.ExchangeRequestId} — {b.BidRate:N0} — {b.CreatedAt:yyyy/MM/dd}");
        }
        var kb = new List<IReadOnlyList<InlineButton>>();
        var nav = new List<InlineButton>();
        if (page > 0) nav.Add(new InlineButton("◀️", $"myprop_exc_p:{page - 1}"));
        if (bids.Count == 10) nav.Add(new InlineButton("▶️", $"myprop_exc_p:{page + 1}"));
        if (nav.Count > 0) kb.Add(nav);
        kb.Add(new[] { new InlineButton(L("🔙 بازگشت", "🔙 Back", lang), "myprop_menu") });
        if (editMsgId.HasValue) try { await _sender.EditMessageTextWithInlineKeyboardAsync(chatId, editMsgId.Value, sb.ToString(), kb, ct).ConfigureAwait(false); return; } catch { }
        try { await _sender.SendTextMessageWithInlineKeyboardAsync(chatId, sb.ToString(), kb, ct).ConfigureAwait(false); } catch { }
    }

    private async Task ShowProjectBids(long chatId, long userId, string? lang, int page, int? editMsgId, CancellationToken ct)
    {
        if (_projectBidRepo == null) return;
        var bids = await _projectBidRepo.ListByBidderAsync(userId, page, 10, ct).ConfigureAwait(false);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(L("<b>📁 پیشنهادات پروژه</b>", "<b>📁 Project Proposals</b>", lang));
        sb.AppendLine("━━━━━━━━━━━━━━━━━━━\n");
        if (bids.Count == 0) sb.AppendLine(L("📭 پیشنهادی یافت نشد.", "📭 No proposals found.", lang));
        foreach (var b in bids)
        {
            var statusIcon = b.Status switch { "accepted" => "✅", "rejected" => "❌", _ => "🟡" };
            sb.AppendLine($"{statusIcon} #{b.ProjectId} — {b.ProposedAmount:N0} — {b.CreatedAt:yyyy/MM/dd}");
        }
        var kb = new List<IReadOnlyList<InlineButton>>();
        var nav = new List<InlineButton>();
        if (page > 0) nav.Add(new InlineButton("◀️", $"myprop_proj_p:{page - 1}"));
        if (bids.Count == 10) nav.Add(new InlineButton("▶️", $"myprop_proj_p:{page + 1}"));
        if (nav.Count > 0) kb.Add(nav);
        kb.Add(new[] { new InlineButton(L("🔙 بازگشت", "🔙 Back", lang), "myprop_menu") });
        if (editMsgId.HasValue) try { await _sender.EditMessageTextWithInlineKeyboardAsync(chatId, editMsgId.Value, sb.ToString(), kb, ct).ConfigureAwait(false); return; } catch { }
        try { await _sender.SendTextMessageWithInlineKeyboardAsync(chatId, sb.ToString(), kb, ct).ConfigureAwait(false); } catch { }
    }

    private async Task<TelegramUserDto?> SafeGetUser(long userId, CancellationToken ct)
    { try { return await _userRepo.GetByTelegramUserIdAsync(userId, ct).ConfigureAwait(false); } catch { return null; } }
}
