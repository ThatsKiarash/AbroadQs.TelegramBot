using AbroadQs.Bot.Contracts;

namespace AbroadQs.Bot.Modules.Common;

public sealed class ServerOpsHandler : IUpdateHandler
{
    private const string BtnMenu = "مدیریت سرورها";
    private const string BtnMenuEn = "Server Management";
    private const string BtnList = "لیست سرورها";
    private const string BtnAdd = "افزودن سرور";
    private const string BtnConnect = "اتصال";
    private const string BtnCommand = "اجرای دستور";
    private const string BtnDisconnect = "قطع اتصال";
    private const string BtnDelete = "حذف سرور";
    private const string BtnInstallers = "نصب ابزارها";
    private const string BtnOpenClaw = "نصب OpenClaw";
    private const string BtnSlipnet = "نصب Slipnet";
    private const string BtnDnstt = "نصب DNSTT";
    private const string BtnBackMain = "بازگشت به منوی اصلی";
    private const string BtnGuide = "راهنما";
    private const string BtnCancel = "انصراف";

    private readonly IResponseSender _sender;
    private readonly IRemoteServerRepository _repo;
    private readonly IRemoteServerRuntimeService _runtime;
    private readonly IUserConversationStateStore _stateStore;
    private readonly IBotStageRepository _stageRepo;
    private readonly IPermissionRepository _permRepo;
    private readonly ITelegramUserRepository _userRepo;

    public ServerOpsHandler(
        IResponseSender sender,
        IRemoteServerRepository repo,
        IRemoteServerRuntimeService runtime,
        IUserConversationStateStore stateStore,
        IBotStageRepository stageRepo,
        IPermissionRepository permRepo,
        ITelegramUserRepository userRepo)
    {
        _sender = sender;
        _repo = repo;
        _runtime = runtime;
        _stateStore = stateStore;
        _stageRepo = stageRepo;
        _permRepo = permRepo;
        _userRepo = userRepo;
    }

    public string? Command => null;

    public bool CanHandle(BotUpdateContext context)
    {
        var t = context.MessageText?.Trim();
        if (string.IsNullOrWhiteSpace(t)) return false;
        if (context.IsCallbackQuery && t.StartsWith("srv_", StringComparison.OrdinalIgnoreCase))
            return true;
        return t.StartsWith("/server", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("/ssh", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("/openclaw", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("/slipnet", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("/dnstt", StringComparison.OrdinalIgnoreCase)
            || t.Equals(BtnMenu, StringComparison.Ordinal)
            || t.Equals(BtnMenuEn, StringComparison.Ordinal)
            || t.Equals(BtnList, StringComparison.Ordinal)
            || t.Equals(BtnAdd, StringComparison.Ordinal)
            || t.Equals(BtnConnect, StringComparison.Ordinal)
            || t.Equals(BtnCommand, StringComparison.Ordinal)
            || t.Equals(BtnDisconnect, StringComparison.Ordinal)
            || t.Equals(BtnDelete, StringComparison.Ordinal)
            || t.Equals(BtnInstallers, StringComparison.Ordinal)
            || t.Equals(BtnOpenClaw, StringComparison.Ordinal)
            || t.Equals(BtnSlipnet, StringComparison.Ordinal)
            || t.Equals(BtnDnstt, StringComparison.Ordinal)
            || t.Equals(BtnGuide, StringComparison.Ordinal)
            || t.Equals(BtnBackMain, StringComparison.Ordinal)
            || t.Equals(BtnCancel, StringComparison.Ordinal);
    }

    public async Task<bool> HandleAsync(BotUpdateContext context, CancellationToken cancellationToken)
    {
        if (!context.UserId.HasValue)
        {
            await _sender.SendTextMessageAsync(context.ChatId, "کاربر نامعتبر است.", cancellationToken).ConfigureAwait(false);
            return true;
        }

        var userId = context.UserId.Value;
        var text = context.MessageText!.Trim();
        if (context.IsCallbackQuery)
        {
            await HandleCallbackAsync(context, userId, text, cancellationToken).ConfigureAwait(false);
            return true;
        }

        var currentState = await _stateStore.GetStateAsync(userId, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(currentState))
        {
            var handledState = await HandleStateAsync(context, userId, currentState, text, cancellationToken).ConfigureAwait(false);
            if (handledState) return true;
        }

        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var cmd = parts[0].ToLowerInvariant();

        if (cmd == "/serverhelp" || text == BtnMenu || text == BtnMenuEn || text == BtnGuide)
        {
            await ShowMainMenuAsync(context.ChatId, cancellationToken).ConfigureAwait(false);
            await _sender.SendTextMessageAsync(context.ChatId, HelpText(), cancellationToken).ConfigureAwait(false);
            return true;
        }

        if (cmd == "/serveradd" || text == BtnAdd)
        {
            if (parts.Length < 6 && (cmd == "/serveradd" || text == BtnAdd))
            {
                await StartAddFlowAsync(context.ChatId, userId, cancellationToken).ConfigureAwait(false);
                return true;
            }

            if (parts.Length < 6)
            {
                await _sender.SendTextMessageAsync(context.ChatId, "فرمت: /serveradd <name> <host> <port> <username> <password>", cancellationToken).ConfigureAwait(false);
                return true;
            }

            if (!int.TryParse(parts[3], out var port) || port < 1 || port > 65535)
            {
                await _sender.SendTextMessageAsync(context.ChatId, "پورت نامعتبر است.", cancellationToken).ConfigureAwait(false);
                return true;
            }

            var name = parts[1];
            var host = parts[2];
            var username = parts[4];
            var password = string.Join(' ', parts.Skip(5));

            var result = await _runtime.AddServerAsync(
                userId, name, host, port, username, password, "password", null, null, cancellationToken).ConfigureAwait(false);
            await _sender.SendTextMessageAsync(context.ChatId, result.Message, cancellationToken).ConfigureAwait(false);
            return true;
        }

        if (cmd == "/serverlist" || text == BtnList)
        {
            var list = await _repo.ListByOwnerAsync(userId, cancellationToken).ConfigureAwait(false);
            if (list.Count == 0)
            {
                await _sender.SendTextMessageAsync(context.ChatId, "هنوز سروری ثبت نکردی. /serverhelp", cancellationToken).ConfigureAwait(false);
                return true;
            }
            var lines = list.Select(s => $"{s.Id}) {s.Name} - {s.Username}@{s.Host}:{s.Port}");
            await _sender.SendTextMessageAsync(context.ChatId, "سرورهای شما:\n" + string.Join('\n', lines), cancellationToken).ConfigureAwait(false);
            return true;
        }

        if (text == BtnConnect)
        {
            await ShowServerPickerAsync(context.ChatId, userId, "srv_connect", "یک سرور برای اتصال انتخاب کن:", cancellationToken).ConfigureAwait(false);
            return true;
        }

        if (text == BtnCommand)
        {
            await ShowServerPickerAsync(context.ChatId, userId, "srv_cmd", "سرور مقصد برای اجرای دستور را انتخاب کن:", cancellationToken).ConfigureAwait(false);
            return true;
        }

        if (text == BtnDisconnect)
        {
            await ShowServerPickerAsync(context.ChatId, userId, "srv_disconnect", "یک سرور برای قطع اتصال انتخاب کن:", cancellationToken).ConfigureAwait(false);
            return true;
        }

        if (text == BtnDelete)
        {
            await ShowServerPickerAsync(context.ChatId, userId, "srv_del", "یک سرور برای حذف انتخاب کن:", cancellationToken).ConfigureAwait(false);
            return true;
        }

        if (text == BtnInstallers)
        {
            await ShowInstallerMenuAsync(context.ChatId, cancellationToken).ConfigureAwait(false);
            return true;
        }

        if (text == BtnOpenClaw)
        {
            await ShowServerPickerAsync(context.ChatId, userId, "srv_install:openclaw", "سرور هدف برای نصب OpenClaw:", cancellationToken).ConfigureAwait(false);
            return true;
        }

        if (text == BtnSlipnet)
        {
            await ShowServerPickerAsync(context.ChatId, userId, "srv_install:slipnet", "سرور هدف برای نصب Slipnet:", cancellationToken).ConfigureAwait(false);
            return true;
        }

        if (text == BtnDnstt)
        {
            await ShowServerPickerAsync(context.ChatId, userId, "srv_install:dnstt", "سرور هدف برای نصب DNSTT:", cancellationToken).ConfigureAwait(false);
            return true;
        }

        if (text == BtnCancel)
        {
            await _stateStore.ClearStateAsync(userId, cancellationToken).ConfigureAwait(false);
            await _stateStore.ClearAllFlowDataAsync(userId, cancellationToken).ConfigureAwait(false);
            await ShowMainMenuAsync(context.ChatId, cancellationToken).ConfigureAwait(false);
            return true;
        }

        if (text == BtnBackMain)
        {
            await _stateStore.ClearStateAsync(userId, cancellationToken).ConfigureAwait(false);
            await _stateStore.ClearAllFlowDataAsync(userId, cancellationToken).ConfigureAwait(false);
            await ShowCoreMainMenuAsync(context.ChatId, userId, cancellationToken).ConfigureAwait(false);
            return true;
        }

        if (cmd == "/serverdel")
        {
            if (parts.Length < 2 || !int.TryParse(parts[1], out var serverId))
            {
                await _sender.SendTextMessageAsync(context.ChatId, "فرمت: /serverdel <serverId>", cancellationToken).ConfigureAwait(false);
                return true;
            }
            var ok = await _repo.DeleteAsync(serverId, userId, cancellationToken).ConfigureAwait(false);
            await _sender.SendTextMessageAsync(context.ChatId, ok ? "سرور حذف شد." : "سرور پیدا نشد یا دسترسی نداری.", cancellationToken).ConfigureAwait(false);
            return true;
        }

        if (cmd == "/serverconnect")
        {
            if (parts.Length < 2 || !int.TryParse(parts[1], out var serverId))
            {
                await _sender.SendTextMessageAsync(context.ChatId, "فرمت: /serverconnect <serverId>", cancellationToken).ConfigureAwait(false);
                return true;
            }
            var result = await _runtime.ConnectAsync(userId, serverId, cancellationToken).ConfigureAwait(false);
            await _sender.SendTextMessageAsync(context.ChatId, result.Message, cancellationToken).ConfigureAwait(false);
            return true;
        }

        if (cmd == "/servercmd")
        {
            if (parts.Length < 3 || !int.TryParse(parts[1], out var serverId))
            {
                await _sender.SendTextMessageAsync(context.ChatId, "فرمت: /servercmd <serverId> <command>", cancellationToken).ConfigureAwait(false);
                return true;
            }
            var command = text.Split(' ', 3)[2];
            var result = await _runtime.ExecuteCommandAsync(userId, serverId, command, cancellationToken).ConfigureAwait(false);
            var output = string.IsNullOrWhiteSpace(result.Output) ? "" : $"\n\n{result.Output}";
            await _sender.SendTextMessageAsync(context.ChatId, $"{result.Message}\nExit: {result.ExitCode}{output}", cancellationToken).ConfigureAwait(false);
            return true;
        }

        if (cmd == "/serverdisconnect")
        {
            if (parts.Length < 2 || !int.TryParse(parts[1], out var serverId))
            {
                await _sender.SendTextMessageAsync(context.ChatId, "فرمت: /serverdisconnect <serverId>", cancellationToken).ConfigureAwait(false);
                return true;
            }
            var result = await _runtime.DisconnectAsync(userId, serverId, cancellationToken).ConfigureAwait(false);
            await _sender.SendTextMessageAsync(context.ChatId, result.Message, cancellationToken).ConfigureAwait(false);
            return true;
        }

        if (cmd == "/openclaw_install")
        {
            if (parts.Length < 2 || !int.TryParse(parts[1], out var serverId))
            {
                await _sender.SendTextMessageAsync(context.ChatId, "فرمت: /openclaw_install <serverId>", cancellationToken).ConfigureAwait(false);
                return true;
            }
            var result = await _runtime.InstallOpenClawAsync(userId, serverId, cancellationToken).ConfigureAwait(false);
            await _sender.SendTextMessageAsync(context.ChatId, $"{result.Message}\nJobId: {result.JobId}", cancellationToken).ConfigureAwait(false);
            return true;
        }

        if (cmd == "/slipnet_install")
        {
            if (parts.Length < 2 || !int.TryParse(parts[1], out var serverId))
            {
                await _sender.SendTextMessageAsync(context.ChatId, "فرمت: /slipnet_install <serverId>", cancellationToken).ConfigureAwait(false);
                return true;
            }
            var result = await _runtime.InstallSlipnetAsync(userId, serverId, cancellationToken).ConfigureAwait(false);
            await _sender.SendTextMessageAsync(context.ChatId, $"{result.Message}\nJobId: {result.JobId}", cancellationToken).ConfigureAwait(false);
            return true;
        }

        if (cmd == "/dnstt_install")
        {
            if (parts.Length < 2 || !int.TryParse(parts[1], out var serverId))
            {
                await _sender.SendTextMessageAsync(context.ChatId, "فرمت: /dnstt_install <serverId>", cancellationToken).ConfigureAwait(false);
                return true;
            }
            var result = await _runtime.InstallDnsttAsync(userId, serverId, cancellationToken).ConfigureAwait(false);
            await _sender.SendTextMessageAsync(context.ChatId, $"{result.Message}\nJobId: {result.JobId}", cancellationToken).ConfigureAwait(false);
            return true;
        }

        if (cmd == "/openclaw_status")
        {
            if (parts.Length < 2 || !long.TryParse(parts[1], out var jobId))
            {
                await _sender.SendTextMessageAsync(context.ChatId, "فرمت: /openclaw_status <jobId>", cancellationToken).ConfigureAwait(false);
                return true;
            }
            var job = await _runtime.GetInstallerJobAsync(userId, jobId, cancellationToken).ConfigureAwait(false);
            if (job is null)
            {
                await _sender.SendTextMessageAsync(context.ChatId, "Job پیدا نشد.", cancellationToken).ConfigureAwait(false);
                return true;
            }
            await _sender.SendTextMessageAsync(
                context.ChatId,
                $"Job #{job.Id}\nStatus: {job.Status}\nServerId: {job.ServerId}\nCreated: {job.CreatedAt:u}\nStarted: {job.StartedAt:u}\nFinished: {job.FinishedAt:u}\n\nLog:\n{job.LogText}",
                cancellationToken).ConfigureAwait(false);
            return true;
        }

        if (cmd == "/ssh")
        {
            await ShowMainMenuAsync(context.ChatId, cancellationToken).ConfigureAwait(false);
            return true;
        }

        return false;
    }

    private async Task HandleCallbackAsync(BotUpdateContext context, long userId, string callback, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(context.CallbackQueryId))
            await _sender.AnswerCallbackQueryAsync(context.CallbackQueryId, null, ct).ConfigureAwait(false);

        if (callback == "srv_menu")
        {
            await ShowMainMenuAsync(context.ChatId, ct).ConfigureAwait(false);
            return;
        }

        if (callback.StartsWith("srv_connect:", StringComparison.Ordinal))
        {
            if (int.TryParse(callback["srv_connect:".Length..], out var serverId))
            {
                var result = await _runtime.ConnectAsync(userId, serverId, ct).ConfigureAwait(false);
                await _sender.SendTextMessageAsync(context.ChatId, result.Message, ct).ConfigureAwait(false);
            }
            return;
        }

        if (callback.StartsWith("srv_disconnect:", StringComparison.Ordinal))
        {
            if (int.TryParse(callback["srv_disconnect:".Length..], out var serverId))
            {
                var result = await _runtime.DisconnectAsync(userId, serverId, ct).ConfigureAwait(false);
                await _sender.SendTextMessageAsync(context.ChatId, result.Message, ct).ConfigureAwait(false);
            }
            return;
        }

        if (callback.StartsWith("srv_del:", StringComparison.Ordinal))
        {
            if (int.TryParse(callback["srv_del:".Length..], out var serverId))
            {
                var ok = await _repo.DeleteAsync(serverId, userId, ct).ConfigureAwait(false);
                await _sender.SendTextMessageAsync(context.ChatId, ok ? "سرور حذف شد." : "سرور پیدا نشد یا دسترسی نداری.", ct).ConfigureAwait(false);
            }
            return;
        }

        if (callback.StartsWith("srv_cmd:", StringComparison.Ordinal))
        {
            if (int.TryParse(callback["srv_cmd:".Length..], out var serverId))
            {
                await _stateStore.SetFlowDataAsync(userId, "srv_cmd_server_id", serverId.ToString(), ct).ConfigureAwait(false);
                await _stateStore.SetStateAsync(userId, "srv_cmd_text", ct).ConfigureAwait(false);
                await _sender.SendTextMessageWithReplyKeyboardAsync(
                    context.ChatId,
                    $"دستور را برای سرور #{serverId} بفرست.",
                    new List<IReadOnlyList<string>> { new[] { BtnCancel } },
                    ct).ConfigureAwait(false);
            }
            return;
        }

        if (callback.StartsWith("srv_install:", StringComparison.Ordinal))
        {
            var parts = callback.Split(':', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 3 && int.TryParse(parts[2], out var serverId))
            {
                RuntimeActionResult result = parts[1] switch
                {
                    "openclaw" => await _runtime.InstallOpenClawAsync(userId, serverId, ct).ConfigureAwait(false),
                    "slipnet" => await _runtime.InstallSlipnetAsync(userId, serverId, ct).ConfigureAwait(false),
                    "dnstt" => await _runtime.InstallDnsttAsync(userId, serverId, ct).ConfigureAwait(false),
                    _ => new RuntimeActionResult(false, "Installer نامعتبر است.")
                };

                await _sender.SendTextMessageAsync(context.ChatId, $"{result.Message}\nJobId: {result.JobId}", ct).ConfigureAwait(false);
            }
        }
    }

    private async Task<bool> HandleStateAsync(BotUpdateContext context, long userId, string state, string text, CancellationToken ct)
    {
        if (text == BtnCancel)
        {
            await _stateStore.ClearStateAsync(userId, ct).ConfigureAwait(false);
            await _stateStore.ClearAllFlowDataAsync(userId, ct).ConfigureAwait(false);
            await ShowMainMenuAsync(context.ChatId, ct).ConfigureAwait(false);
            return true;
        }

        switch (state)
        {
            case "srv_add_name":
                await _stateStore.SetFlowDataAsync(userId, "srv_name", text, ct).ConfigureAwait(false);
                await _stateStore.SetStateAsync(userId, "srv_add_host", ct).ConfigureAwait(false);
                await _sender.SendTextMessageWithReplyKeyboardAsync(context.ChatId, "IP یا Host سرور را وارد کن:", new List<IReadOnlyList<string>> { new[] { BtnCancel } }, ct).ConfigureAwait(false);
                return true;
            case "srv_add_host":
                await _stateStore.SetFlowDataAsync(userId, "srv_host", text, ct).ConfigureAwait(false);
                await _stateStore.SetStateAsync(userId, "srv_add_port", ct).ConfigureAwait(false);
                await _sender.SendTextMessageWithReplyKeyboardAsync(context.ChatId, "Port را وارد کن (مثلا 22):", new List<IReadOnlyList<string>> { new[] { BtnCancel } }, ct).ConfigureAwait(false);
                return true;
            case "srv_add_port":
                if (!int.TryParse(text, out var port) || port is < 1 or > 65535)
                {
                    await _sender.SendTextMessageAsync(context.ChatId, "پورت نامعتبر است. یک عدد بین 1 تا 65535 وارد کن.", ct).ConfigureAwait(false);
                    return true;
                }
                await _stateStore.SetFlowDataAsync(userId, "srv_port", port.ToString(), ct).ConfigureAwait(false);
                await _stateStore.SetStateAsync(userId, "srv_add_username", ct).ConfigureAwait(false);
                await _sender.SendTextMessageWithReplyKeyboardAsync(context.ChatId, "Username را وارد کن:", new List<IReadOnlyList<string>> { new[] { BtnCancel } }, ct).ConfigureAwait(false);
                return true;
            case "srv_add_username":
                await _stateStore.SetFlowDataAsync(userId, "srv_username", text, ct).ConfigureAwait(false);
                await _stateStore.SetStateAsync(userId, "srv_add_password", ct).ConfigureAwait(false);
                await _sender.SendTextMessageWithReplyKeyboardAsync(context.ChatId, "Password را وارد کن:", new List<IReadOnlyList<string>> { new[] { BtnCancel } }, ct).ConfigureAwait(false);
                return true;
            case "srv_add_password":
            {
                var name = await _stateStore.GetFlowDataAsync(userId, "srv_name", ct).ConfigureAwait(false) ?? "";
                var host = await _stateStore.GetFlowDataAsync(userId, "srv_host", ct).ConfigureAwait(false) ?? "";
                var portText = await _stateStore.GetFlowDataAsync(userId, "srv_port", ct).ConfigureAwait(false) ?? "22";
                var username = await _stateStore.GetFlowDataAsync(userId, "srv_username", ct).ConfigureAwait(false) ?? "";
                var srvPort = int.TryParse(portText, out var parsedPort) ? parsedPort : 22;

                var result = await _runtime.AddServerAsync(userId, name, host, srvPort, username, text, "password", null, null, ct).ConfigureAwait(false);
                await _stateStore.ClearStateAsync(userId, ct).ConfigureAwait(false);
                await _stateStore.ClearAllFlowDataAsync(userId, ct).ConfigureAwait(false);
                await _sender.RemoveReplyKeyboardSilentAsync(context.ChatId, ct).ConfigureAwait(false);
                await _sender.SendTextMessageAsync(context.ChatId, result.Message, ct).ConfigureAwait(false);
                await ShowMainMenuAsync(context.ChatId, ct).ConfigureAwait(false);
                return true;
            }
            case "srv_cmd_text":
            {
                var serverIdText = await _stateStore.GetFlowDataAsync(userId, "srv_cmd_server_id", ct).ConfigureAwait(false);
                if (!int.TryParse(serverIdText, out var serverId))
                {
                    await _stateStore.ClearStateAsync(userId, ct).ConfigureAwait(false);
                    await _stateStore.ClearAllFlowDataAsync(userId, ct).ConfigureAwait(false);
                    return true;
                }

                var result = await _runtime.ExecuteCommandAsync(userId, serverId, text, ct).ConfigureAwait(false);
                var output = string.IsNullOrWhiteSpace(result.Output) ? "" : $"\n\n{result.Output}";
                await _sender.SendTextMessageAsync(context.ChatId, $"{result.Message}\nExit: {result.ExitCode}{output}", ct).ConfigureAwait(false);
                await _stateStore.ClearStateAsync(userId, ct).ConfigureAwait(false);
                await _stateStore.ClearAllFlowDataAsync(userId, ct).ConfigureAwait(false);
                await _sender.RemoveReplyKeyboardSilentAsync(context.ChatId, ct).ConfigureAwait(false);
                return true;
            }
            default:
                return false;
        }
    }

    private async Task StartAddFlowAsync(long chatId, long userId, CancellationToken ct)
    {
        await _stateStore.ClearAllFlowDataAsync(userId, ct).ConfigureAwait(false);
        await _stateStore.SetStateAsync(userId, "srv_add_name", ct).ConfigureAwait(false);
        await _sender.SendTextMessageWithReplyKeyboardAsync(
            chatId,
            "اسم سرور را وارد کن (مثلا: VPS-Iran):",
            new List<IReadOnlyList<string>> { new[] { BtnCancel } },
            ct).ConfigureAwait(false);
    }

    private async Task ShowInstallerMenuAsync(long chatId, CancellationToken ct)
    {
        await _sender.SendTextMessageWithReplyKeyboardAsync(
            chatId,
            "یکی از Installerها را انتخاب کن:",
            new List<IReadOnlyList<string>>
            {
                new[] { BtnOpenClaw, BtnSlipnet },
                new[] { BtnDnstt, BtnBackMain },
                new[] { BtnCancel }
            },
            ct).ConfigureAwait(false);
    }

    private async Task ShowMainMenuAsync(long chatId, CancellationToken ct)
    {
        await _sender.SendTextMessageWithReplyKeyboardAsync(
            chatId,
            "مدیریت سرورها:\nیک گزینه را انتخاب کن.",
            new List<IReadOnlyList<string>>
            {
                new[] { BtnList, BtnAdd },
                new[] { BtnConnect, BtnCommand },
                new[] { BtnDisconnect, BtnDelete },
                new[] { BtnInstallers, BtnBackMain },
                new[] { BtnGuide }
            },
            ct).ConfigureAwait(false);
    }

    private async Task ShowCoreMainMenuAsync(long chatId, long userId, CancellationToken ct)
    {
        var user = await _userRepo.GetByTelegramUserIdAsync(userId, ct).ConfigureAwait(false);
        var isFa = string.Equals(user?.PreferredLanguage ?? "fa", "fa", StringComparison.OrdinalIgnoreCase);

        var stage = await _stageRepo.GetByKeyAsync("main_menu", ct).ConfigureAwait(false);
        var text = stage is { IsEnabled: true }
            ? (isFa ? (stage.TextFa ?? stage.TextEn ?? "منوی اصلی:") : (stage.TextEn ?? stage.TextFa ?? "Main menu:"))
            : (isFa ? "منوی اصلی:" : "Main menu:");

        var allButtons = await _stageRepo.GetButtonsAsync("main_menu", ct).ConfigureAwait(false);
        var userPerms = await _permRepo.GetUserPermissionsAsync(userId, ct).ConfigureAwait(false);
        var permSet = new HashSet<string>(userPerms, StringComparer.OrdinalIgnoreCase);

        var keyboard = new List<IReadOnlyList<string>>();
        foreach (var row in allButtons
            .Where(b => b.IsEnabled)
            .Where(b => string.IsNullOrEmpty(b.RequiredPermission) || permSet.Contains(b.RequiredPermission))
            .GroupBy(b => b.Row)
            .OrderBy(g => g.Key))
        {
            var rowTexts = new List<string>();
            foreach (var btn in row.OrderBy(b => b.Column))
                rowTexts.Add(isFa ? (btn.TextFa ?? btn.TextEn ?? "?") : (btn.TextEn ?? btn.TextFa ?? "?"));

            if (rowTexts.Count > 0)
                keyboard.Add(rowTexts);
        }

        var serverBtn = isFa ? BtnMenu : BtnMenuEn;
        var settingsHint = isFa ? "تنظیم" : "setting";
        var attached = false;
        for (var i = 0; i < keyboard.Count; i++)
        {
            var row = keyboard[i].ToList();
            if (!row.Any(c => c.Contains(settingsHint, StringComparison.OrdinalIgnoreCase)))
                continue;
            if (!row.Contains(serverBtn, StringComparer.Ordinal))
                row.Add(serverBtn);
            keyboard[i] = row;
            attached = true;
            break;
        }
        if (!attached)
            keyboard.Add(new[] { serverBtn });

        await _stateStore.SetReplyStageAsync(userId, "main_menu", ct).ConfigureAwait(false);
        await _sender.SendTextMessageWithReplyKeyboardAsync(chatId, text, keyboard, ct).ConfigureAwait(false);
    }

    private async Task ShowServerPickerAsync(long chatId, long userId, string callbackPrefix, string title, CancellationToken ct)
    {
        var list = await _repo.ListByOwnerAsync(userId, ct).ConfigureAwait(false);
        if (list.Count == 0)
        {
            await _sender.SendTextMessageAsync(chatId, "هنوز سروری ثبت نکردی.", ct).ConfigureAwait(false);
            return;
        }

        var keyboard = new List<IReadOnlyList<InlineButton>>();
        foreach (var srv in list.Take(15))
        {
            keyboard.Add(new[]
            {
                new InlineButton($"{srv.Name} ({srv.Host}:{srv.Port})", $"{callbackPrefix}:{srv.Id}")
            });
        }
        keyboard.Add(new[] { new InlineButton("🔙 بازگشت", "srv_menu") });
        await _sender.SendTextMessageWithInlineKeyboardAsync(chatId, title, keyboard, ct).ConfigureAwait(false);
    }

    private static string HelpText() =>
        """
        <b>SSH / Server Commands</b>
        /ssh (نمایش منوی دکمه‌ای)
        /serverlist
        /serveradd (شروع مرحله‌ای)
        /serverconnect <serverId>
        /servercmd <serverId> <command>
        /serverdisconnect <serverId>
        /serverdel <serverId>
        /openclaw_install <serverId>
        /slipnet_install <serverId>
        /dnstt_install <serverId>
        /openclaw_status <jobId>

        راهنمای افزودن سرور (مرحله‌ای):
        1) افزودن سرور
        2) نام سرور (مثال: vps-test)
        3) Host/IP (مثال: 91.99.179.17)
        4) Port (مثال: 2200)
        5) Username (مثال: root)
        6) Password

        نمونه کامند مستقیم:
        /serveradd vps-test 91.99.179.17 2200 root Kia135724!
        """;
}
