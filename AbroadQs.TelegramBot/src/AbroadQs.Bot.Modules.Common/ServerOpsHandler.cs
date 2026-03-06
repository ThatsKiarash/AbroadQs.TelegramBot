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
    private const string BtnShellExit = "خروج از سرور";

    private const string FlowShellServerId = "srv_shell_server_id";
    private const string FlowShellServerName = "srv_shell_server_name";

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
            await ShowServerListInlineAsync(context.ChatId, userId, null, cancellationToken).ConfigureAwait(false);
            return true;
        }

        if (text == BtnConnect)
        {
            await ShowServerListInlineAsync(context.ChatId, userId, "یک سرور را انتخاب کن تا پنل عملیاتش باز شود:", cancellationToken).ConfigureAwait(false);
            return true;
        }

        if (text == BtnCommand)
        {
            await ShowServerListInlineAsync(context.ChatId, userId, "یک سرور را انتخاب کن و سپس وارد ترمینال شو:", cancellationToken).ConfigureAwait(false);
            return true;
        }

        if (text == BtnDisconnect)
        {
            await ShowServerListInlineAsync(context.ChatId, userId, "سرور موردنظر برای عملیات را انتخاب کن:", cancellationToken).ConfigureAwait(false);
            return true;
        }

        if (text == BtnDelete)
        {
            await ShowServerListInlineAsync(context.ChatId, userId, "سرور موردنظر برای حذف را انتخاب کن:", cancellationToken).ConfigureAwait(false);
            return true;
        }

        if (text == BtnInstallers)
        {
            await ShowServerListInlineAsync(context.ChatId, userId, "سرور هدف برای نصب ابزار را انتخاب کن:", cancellationToken).ConfigureAwait(false);
            return true;
        }

        if (text == BtnOpenClaw)
        {
            var serverId = await GetActiveShellServerIdAsync(userId, cancellationToken).ConfigureAwait(false);
            if (serverId is null)
            {
                await ShowServerListInlineAsync(context.ChatId, userId, "ابتدا یک سرور را انتخاب کن:", cancellationToken).ConfigureAwait(false);
                return true;
            }
            await RunInstallerWithProgressAsync(context.ChatId, userId, serverId.Value, "openclaw", cancellationToken).ConfigureAwait(false);
            return true;
        }

        if (text == BtnSlipnet)
        {
            var serverId = await GetActiveShellServerIdAsync(userId, cancellationToken).ConfigureAwait(false);
            if (serverId is null)
            {
                await ShowServerListInlineAsync(context.ChatId, userId, "ابتدا یک سرور را انتخاب کن:", cancellationToken).ConfigureAwait(false);
                return true;
            }
            await RunInstallerWithProgressAsync(context.ChatId, userId, serverId.Value, "slipnet", cancellationToken).ConfigureAwait(false);
            return true;
        }

        if (text == BtnDnstt)
        {
            var serverId = await GetActiveShellServerIdAsync(userId, cancellationToken).ConfigureAwait(false);
            if (serverId is null)
            {
                await ShowServerListInlineAsync(context.ChatId, userId, "ابتدا یک سرور را انتخاب کن:", cancellationToken).ConfigureAwait(false);
                return true;
            }
            await RunInstallerWithProgressAsync(context.ChatId, userId, serverId.Value, "dnstt", cancellationToken).ConfigureAwait(false);
            return true;
        }

        if (text == BtnCancel)
        {
            await _stateStore.ClearStateAsync(userId, cancellationToken).ConfigureAwait(false);
            await _stateStore.ClearAllFlowDataAsync(userId, cancellationToken).ConfigureAwait(false);
            await ShowMainMenuAsync(context.ChatId, cancellationToken).ConfigureAwait(false);
            return true;
        }

        if (text == BtnShellExit)
        {
            await ExitShellModeAsync(context.ChatId, userId, cancellationToken).ConfigureAwait(false);
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

        if (callback == "srv_list")
        {
            await ShowServerListInlineAsync(context.ChatId, userId, null, ct).ConfigureAwait(false);
            return;
        }

        if (callback.StartsWith("srv_focus:", StringComparison.Ordinal))
        {
            if (int.TryParse(callback["srv_focus:".Length..], out var serverId))
                await ShowServerOperationsInlineAsync(context.ChatId, userId, serverId, context.CallbackMessageId, ct).ConfigureAwait(false);
            return;
        }

        if (callback.StartsWith("srv_shell:", StringComparison.Ordinal))
        {
            if (int.TryParse(callback["srv_shell:".Length..], out var serverId))
                await EnterShellModeAsync(context.ChatId, userId, serverId, ct).ConfigureAwait(false);
            return;
        }

        if (callback.StartsWith("srv_shell_exit:", StringComparison.Ordinal))
        {
            await ExitShellModeAsync(context.ChatId, userId, ct).ConfigureAwait(false);
            return;
        }

        if (callback == "srv_back_main")
        {
            await _stateStore.ClearStateAsync(userId, ct).ConfigureAwait(false);
            await _stateStore.ClearAllFlowDataAsync(userId, ct).ConfigureAwait(false);
            await ShowCoreMainMenuAsync(context.ChatId, userId, ct).ConfigureAwait(false);
            return;
        }

        if (callback.StartsWith("srv_connect:", StringComparison.Ordinal))
        {
            if (int.TryParse(callback["srv_connect:".Length..], out var serverId))
            {
                var result = await _runtime.ConnectAsync(userId, serverId, ct).ConfigureAwait(false);
                await SendProgressResultAsync(
                    context.ChatId,
                    $"اتصال به سرور #{serverId} در حال انجام است...",
                    $"{result.Message}",
                    BuildServerOperationsKeyboard(serverId),
                    ct).ConfigureAwait(false);
                await ShowServerOperationsInlineAsync(context.ChatId, userId, serverId, null, ct).ConfigureAwait(false);
            }
            return;
        }

        if (callback.StartsWith("srv_disconnect:", StringComparison.Ordinal))
        {
            if (int.TryParse(callback["srv_disconnect:".Length..], out var serverId))
            {
                var result = await _runtime.DisconnectAsync(userId, serverId, ct).ConfigureAwait(false);
                await SendProgressResultAsync(
                    context.ChatId,
                    $"قطع اتصال سرور #{serverId}...",
                    result.Message,
                    BuildServerOperationsKeyboard(serverId),
                    ct).ConfigureAwait(false);
            }
            return;
        }

        if (callback.StartsWith("srv_del:", StringComparison.Ordinal))
        {
            if (int.TryParse(callback["srv_del:".Length..], out var serverId))
            {
                var ok = await _repo.DeleteAsync(serverId, userId, ct).ConfigureAwait(false);
                await _sender.SendTextMessageAsync(context.ChatId, ok ? "سرور حذف شد." : "سرور پیدا نشد یا دسترسی نداری.", ct).ConfigureAwait(false);
                await ShowServerListInlineAsync(context.ChatId, userId, null, ct).ConfigureAwait(false);
            }
            return;
        }

        if (callback.StartsWith("srv_cmd:", StringComparison.Ordinal))
        {
            if (int.TryParse(callback["srv_cmd:".Length..], out var serverId))
            {
                await EnterShellModeAsync(context.ChatId, userId, serverId, ct).ConfigureAwait(false);
            }
            return;
        }

        if (callback.StartsWith("srv_install_menu:", StringComparison.Ordinal))
        {
            if (int.TryParse(callback["srv_install_menu:".Length..], out var serverId))
                await ShowInstallerMenuInlineAsync(context.ChatId, serverId, context.CallbackMessageId, ct).ConfigureAwait(false);
            return;
        }

        if (callback.StartsWith("srv_install:", StringComparison.Ordinal))
        {
            var parts = callback.Split(':', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 3 && int.TryParse(parts[2], out var serverId))
            {
                await RunInstallerWithProgressAsync(context.ChatId, userId, serverId, parts[1], ct).ConfigureAwait(false);
            }
            return;
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
            case "srv_shell":
            {
                var serverId = await GetActiveShellServerIdAsync(userId, ct).ConfigureAwait(false);
                if (serverId is null)
                {
                    await ExitShellModeAsync(context.ChatId, userId, ct).ConfigureAwait(false);
                    return true;
                }

                if (text == BtnShellExit)
                {
                    await ExitShellModeAsync(context.ChatId, userId, ct).ConfigureAwait(false);
                    return true;
                }

                if (text == BtnBackMain)
                {
                    await ExitShellModeAsync(context.ChatId, userId, ct).ConfigureAwait(false);
                    await ShowCoreMainMenuAsync(context.ChatId, userId, ct).ConfigureAwait(false);
                    return true;
                }

                if (text == BtnDisconnect)
                {
                    var dis = await _runtime.DisconnectAsync(userId, serverId.Value, ct).ConfigureAwait(false);
                    await SendProgressResultAsync(
                        context.ChatId,
                        $"قطع اتصال سرور #{serverId.Value}...",
                        dis.Message,
                        BuildShellKeyboard(serverId.Value),
                        ct).ConfigureAwait(false);
                    return true;
                }

                if (text == BtnInstallers)
                {
                    await ShowInstallerMenuInlineAsync(context.ChatId, serverId.Value, null, ct).ConfigureAwait(false);
                    return true;
                }

                if (text == BtnOpenClaw || text == BtnSlipnet || text == BtnDnstt)
                {
                    var tool = text == BtnOpenClaw ? "openclaw" : text == BtnSlipnet ? "slipnet" : "dnstt";
                    await RunInstallerWithProgressAsync(context.ChatId, userId, serverId.Value, tool, ct).ConfigureAwait(false);
                    return true;
                }

                // In shell mode: any non-button text is treated as an SSH command.
                await RunShellCommandWithProgressAsync(context.ChatId, userId, serverId.Value, text, ct).ConfigureAwait(false);
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
                new[] { BtnDnstt, BtnShellExit },
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
                new[] { BtnAdd },
                new[] { BtnConnect, BtnList, BtnDisconnect },
                new[] { BtnCommand, BtnInstallers },
                new[] { BtnGuide, BtnDelete },
                new[] { BtnBackMain }
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
        keyboard.Add(new[] { new InlineButton("بازگشت", "srv_menu") });
        await _sender.SendTextMessageWithInlineKeyboardAsync(chatId, title, keyboard, ct).ConfigureAwait(false);
    }

    private async Task ShowServerListInlineAsync(long chatId, long userId, string? title, CancellationToken ct)
    {
        var list = await _repo.ListByOwnerAsync(userId, ct).ConfigureAwait(false);
        if (list.Count == 0)
        {
            await _sender.SendTextMessageAsync(chatId, "هنوز سروری ثبت نکردی. از «افزودن سرور» استفاده کن.", ct).ConfigureAwait(false);
            return;
        }

        var keyboard = new List<IReadOnlyList<InlineButton>>();
        foreach (var srv in list.Take(20))
            keyboard.Add(new[] { new InlineButton($"{srv.Name} ({srv.Host}:{srv.Port})", $"srv_focus:{srv.Id}") });

        keyboard.Add(new[] { new InlineButton("بازگشت به مدیریت سرورها", "srv_menu") });
        keyboard.Add(new[] { new InlineButton("بازگشت به منوی اصلی", "srv_back_main") });

        var txt = title ?? "لیست سرورها:\nیک سرور را انتخاب کن تا عملیاتش باز شود.";
        await _sender.SendTextMessageWithInlineKeyboardAsync(chatId, txt, keyboard, ct).ConfigureAwait(false);
    }

    private async Task ShowServerOperationsInlineAsync(long chatId, long userId, int serverId, int? editMessageId, CancellationToken ct)
    {
        var list = await _repo.ListByOwnerAsync(userId, ct).ConfigureAwait(false);
        var srv = list.FirstOrDefault(x => x.Id == serverId);
        if (srv is null)
        {
            await _sender.SendTextMessageAsync(chatId, "سرور پیدا نشد یا دسترسی نداری.", ct).ConfigureAwait(false);
            return;
        }

        var text =
            $"<b>{srv.Name}</b>\n" +
            $"<code>{srv.Username}@{srv.Host}:{srv.Port}</code>\n\n" +
            "یک عملیات را انتخاب کن:";

        await EditOrSendInlineAsync(chatId, text, BuildServerOperationsKeyboard(serverId), editMessageId, ct).ConfigureAwait(false);
    }

    private static List<IReadOnlyList<InlineButton>> BuildServerOperationsKeyboard(int serverId) =>
        new()
        {
            new[] { new InlineButton("اتصال", $"srv_connect:{serverId}"), new InlineButton("ورود به ترمینال", $"srv_shell:{serverId}") },
            new[] { new InlineButton("نصب ابزارها", $"srv_install_menu:{serverId}") },
            new[] { new InlineButton("قطع اتصال", $"srv_disconnect:{serverId}"), new InlineButton("حذف سرور", $"srv_del:{serverId}") },
            new[] { new InlineButton("بازگشت به لیست سرورها", "srv_list") },
            new[] { new InlineButton("بازگشت به منوی اصلی", "srv_back_main") }
        };

    private static List<IReadOnlyList<InlineButton>> BuildShellKeyboard(int serverId) =>
        new()
        {
            new[] { new InlineButton("قطع اتصال", $"srv_disconnect:{serverId}"), new InlineButton("نصب ابزارها", $"srv_install_menu:{serverId}") },
            new[] { new InlineButton("خروج از سرور", $"srv_shell_exit:{serverId}"), new InlineButton("بازگشت به منوی اصلی", "srv_back_main") }
        };

    private async Task ShowInstallerMenuInlineAsync(long chatId, int serverId, int? editMessageId, CancellationToken ct)
    {
        var text = $"نصب ابزار روی سرور #{serverId}\nیک گزینه را انتخاب کن:";
        var keyboard = new List<IReadOnlyList<InlineButton>>
        {
            new[] { new InlineButton("نصب OpenClaw", $"srv_install:openclaw:{serverId}") },
            new[] { new InlineButton("نصب Slipnet", $"srv_install:slipnet:{serverId}") },
            new[] { new InlineButton("نصب DNSTT", $"srv_install:dnstt:{serverId}") },
            new[] { new InlineButton("بازگشت به عملیات سرور", $"srv_focus:{serverId}") }
        };
        await EditOrSendInlineAsync(chatId, text, keyboard, editMessageId, ct).ConfigureAwait(false);
    }

    private async Task EnterShellModeAsync(long chatId, long userId, int serverId, CancellationToken ct)
    {
        var connect = await _runtime.ConnectAsync(userId, serverId, ct).ConfigureAwait(false);
        if (!connect.Success)
        {
            await _sender.SendTextMessageAsync(chatId, connect.Message, ct).ConfigureAwait(false);
            return;
        }

        var list = await _repo.ListByOwnerAsync(userId, ct).ConfigureAwait(false);
        var srv = list.FirstOrDefault(x => x.Id == serverId);
        var srvName = srv?.Name ?? $"#{serverId}";
        await _stateStore.SetFlowDataAsync(userId, FlowShellServerId, serverId.ToString(), ct).ConfigureAwait(false);
        await _stateStore.SetFlowDataAsync(userId, FlowShellServerName, srvName, ct).ConfigureAwait(false);
        await _stateStore.SetStateAsync(userId, "srv_shell", ct).ConfigureAwait(false);

        var text =
            $"ورود به ترمینال سرور <b>{srvName}</b> انجام شد.\n" +
            "از این لحظه هر متنی که بفرستی، به عنوان دستور SSH اجرا می‌شود.\n\n" +
            "نمونه: <code>ls -la</code> یا <code>docker ps</code>";

        await _sender.SendTextMessageWithInlineKeyboardAsync(chatId, text, BuildShellKeyboard(serverId), ct).ConfigureAwait(false);
    }

    private async Task ExitShellModeAsync(long chatId, long userId, CancellationToken ct)
    {
        var serverId = await GetActiveShellServerIdAsync(userId, ct).ConfigureAwait(false);
        if (serverId.HasValue)
        {
            try { await _runtime.DisconnectAsync(userId, serverId.Value, ct).ConfigureAwait(false); } catch { }
        }

        await _stateStore.ClearStateAsync(userId, ct).ConfigureAwait(false);
        await _stateStore.ClearAllFlowDataAsync(userId, ct).ConfigureAwait(false);
        await ShowMainMenuAsync(chatId, ct).ConfigureAwait(false);
    }

    private async Task<int?> GetActiveShellServerIdAsync(long userId, CancellationToken ct)
    {
        var id = await _stateStore.GetFlowDataAsync(userId, FlowShellServerId, ct).ConfigureAwait(false);
        return int.TryParse(id, out var serverId) ? serverId : null;
    }

    private async Task RunShellCommandWithProgressAsync(long chatId, long userId, int serverId, string command, CancellationToken ct)
    {
        var progress = $"در حال اجرای دستور روی سرور #{serverId}\n<code>{EscapeHtml(command)}</code>\n{BuildProgressBar(1)}";
        var result = await _runtime.ExecuteCommandAsync(userId, serverId, command, ct).ConfigureAwait(false);
        var output = string.IsNullOrWhiteSpace(result.Output) ? "(no output)" : Limit(result.Output, 3000);
        var finalText =
            $"<b>نتیجه دستور</b>\n" +
            $"Server: <code>{serverId}</code>\n" +
            $"ExitCode: <code>{result.ExitCode}</code>\n\n" +
            $"<pre>{EscapeHtml(output)}</pre>";

        await SendProgressResultAsync(chatId, progress, finalText, BuildShellKeyboard(serverId), ct).ConfigureAwait(false);
    }

    private async Task RunInstallerWithProgressAsync(long chatId, long userId, int serverId, string installerType, CancellationToken ct)
    {
        var friendly = installerType.ToLowerInvariant() switch
        {
            "openclaw" => "OpenClaw",
            "slipnet" => "Slipnet",
            "dnstt" => "DNSTT",
            _ => installerType
        };

        var startText = $"شروع نصب {friendly} روی سرور #{serverId}\n{BuildProgressBar(1)}";
        var loadingId = await _sender.SendLoadingWithRemoveReplyKbAsync(chatId, ct).ConfigureAwait(false);

        async Task EditOrSendAsync(string text)
        {
            if (loadingId.HasValue)
            {
                try
                {
                    await _sender.EditMessageTextWithInlineKeyboardAsync(chatId, loadingId.Value, text, BuildShellKeyboard(serverId), ct).ConfigureAwait(false);
                    return;
                }
                catch { }
            }
            await _sender.SendTextMessageWithInlineKeyboardAsync(chatId, text, BuildShellKeyboard(serverId), ct).ConfigureAwait(false);
        }

        await EditOrSendAsync(startText).ConfigureAwait(false);

        RuntimeActionResult start = installerType.ToLowerInvariant() switch
        {
            "openclaw" => await _runtime.InstallOpenClawAsync(userId, serverId, ct).ConfigureAwait(false),
            "slipnet" => await _runtime.InstallSlipnetAsync(userId, serverId, ct).ConfigureAwait(false),
            "dnstt" => await _runtime.InstallDnsttAsync(userId, serverId, ct).ConfigureAwait(false),
            _ => new RuntimeActionResult(false, "Installer نامعتبر است.")
        };

        if (!start.Success || start.JobId is null)
        {
            await EditOrSendAsync($"شروع نصب {friendly} ناموفق بود:\n{start.Message}").ConfigureAwait(false);
            return;
        }

        await EditOrSendAsync($"نصب {friendly} شروع شد.\nJobId: <code>{start.JobId}</code>\nدر حال بررسی وضعیت...\n{BuildProgressBar(2)}").ConfigureAwait(false);

        string? lastStatus = null;
        for (var i = 0; i < 24; i++)
        {
            await Task.Delay(2500, ct).ConfigureAwait(false);
            var job = await _runtime.GetInstallerJobAsync(userId, start.JobId.Value, ct).ConfigureAwait(false);
            if (job is null) continue;

            var status = job.Status ?? "unknown";
            var progress = status.Equals("completed", StringComparison.OrdinalIgnoreCase) ? BuildProgressBar(6)
                : status.Equals("failed", StringComparison.OrdinalIgnoreCase) ? BuildProgressBar(6)
                : BuildProgressBar(Math.Min(5, 2 + (i / 5)));

            var logTail = Limit(job.LogText ?? "", 1200);
            var body =
                $"Installer: <b>{friendly}</b>\n" +
                $"Server: <code>{serverId}</code>\n" +
                $"JobId: <code>{job.Id}</code>\n" +
                $"Status: <b>{EscapeHtml(status)}</b>\n" +
                $"{progress}\n\n" +
                $"<pre>{EscapeHtml(string.IsNullOrWhiteSpace(logTail) ? "(no logs yet)" : logTail)}</pre>";

            if (!string.Equals(lastStatus, status, StringComparison.OrdinalIgnoreCase) || i % 4 == 0)
                await EditOrSendAsync(body).ConfigureAwait(false);

            lastStatus = status;
            if (status.Equals("completed", StringComparison.OrdinalIgnoreCase) || status.Equals("failed", StringComparison.OrdinalIgnoreCase))
                break;
        }
    }

    private async Task SendProgressResultAsync(
        long chatId,
        string progressText,
        string finalText,
        IReadOnlyList<IReadOnlyList<InlineButton>> keyboard,
        CancellationToken ct)
    {
        var loadingId = await _sender.SendLoadingWithRemoveReplyKbAsync(chatId, ct).ConfigureAwait(false);
        if (loadingId.HasValue)
        {
            try
            {
                await _sender.EditMessageTextWithInlineKeyboardAsync(chatId, loadingId.Value, progressText, keyboard, ct).ConfigureAwait(false);
                await _sender.EditMessageTextWithInlineKeyboardAsync(chatId, loadingId.Value, finalText, keyboard, ct).ConfigureAwait(false);
                return;
            }
            catch { }
        }

        await _sender.SendTextMessageWithInlineKeyboardAsync(chatId, finalText, keyboard, ct).ConfigureAwait(false);
    }

    private async Task EditOrSendInlineAsync(long chatId, string text, IReadOnlyList<IReadOnlyList<InlineButton>> keyboard, int? editMessageId, CancellationToken ct)
    {
        if (editMessageId.HasValue)
        {
            try
            {
                await _sender.EditMessageTextWithInlineKeyboardAsync(chatId, editMessageId.Value, text, keyboard, ct).ConfigureAwait(false);
                return;
            }
            catch { }
        }
        await _sender.SendTextMessageWithInlineKeyboardAsync(chatId, text, keyboard, ct).ConfigureAwait(false);
    }

    private static string BuildProgressBar(int step)
    {
        var safe = Math.Clamp(step, 0, 6);
        return $"[{new string('■', safe)}{new string('□', 6 - safe)}]";
    }

    private static string EscapeHtml(string value) =>
        value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);

    private static string Limit(string value, int max) =>
        value.Length <= max ? value : value[..max] + "\n... (truncated)";

    private static string HelpText() =>
        """
        <b>راهنمای کامل مدیریت سرورها (SSH داخل ربات)</b>

        1) ورود به بخش سرورها:
        - از منوی اصلی: «مدیریت سرورها»
        - یا دستور: /ssh

        2) افزودن سرور (مرحله‌ای):
        - «افزودن سرور» را بزن
        - به ترتیب وارد کن: Name -> Host/IP -> Port -> Username -> Password
        - مثال:
          Name: vps-iran
          Host: 91.99.179.17
          Port: 2200
          Username: root
          Password: ********

        3) کار با سرور از لیست:
        - «لیست سرورها» را بزن
        - روی سرور مورد نظر کلیک کن
        - پنل عملیات همان سرور باز می‌شود:
          اتصال | ورود به ترمینال | نصب ابزارها | قطع اتصال | حذف سرور

        4) حالت ترمینال (شبیه SSH/PuTTY):
        - در پنل سرور، «ورود به ترمینال» را بزن
        - از آن لحظه، هر متنی که بفرستی به‌عنوان دستور SSH اجرا می‌شود
        - جواب دستور با ادیت پیام و همراه وضعیت پردازش نمایش داده می‌شود
        - دکمه‌های زیر خروجی:
          قطع اتصال | نصب ابزارها | خروج از سرور | بازگشت به منوی اصلی

        5) نصب ابزارها:
        - از پنل سرور یا داخل ترمینال: «نصب ابزارها»
        - ابزارها:
          نصب OpenClaw
          نصب Slipnet
          نصب DNSTT
        - وضعیت نصب مرحله‌به‌مرحله با همان پیام (edit) بروزرسانی می‌شود

        6) خروج:
        - «خروج از سرور» -> بازگشت به مدیریت سرورها
        - «بازگشت به منوی اصلی» -> بازگشت به main menu

        7) دستورات مستقیم (اختیاری):
        /serveradd <name> <host> <port> <username> <password>
        /serverconnect <serverId>
        /servercmd <serverId> <command>
        /serverdisconnect <serverId>
        /serverdel <serverId>
        /openclaw_install <serverId>
        /slipnet_install <serverId>
        /dnstt_install <serverId>
        /openclaw_status <jobId>
        """;
}
