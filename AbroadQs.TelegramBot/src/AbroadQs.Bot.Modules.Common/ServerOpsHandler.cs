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
    private const string BtnInstallStatus = "وضعیت نصب ابزارها";
    private const string BtnAccessGuide = "راهنمای دسترسی ابزارها";
    private const string BtnBackMain = "بازگشت به منوی اصلی";
    private const string BtnGuide = "راهنما";
    private const string BtnCancel = "انصراف";
    private const string BtnShellExit = "خروج از سرور";
    private const string BtnBackServers = "بازگشت به لیست سرورها";
    private const string BtnBackPrev = "بازگشت به منوی قبلی";

    private const string FlowShellServerId = "srv_shell_server_id";
    private const string FlowShellServerName = "srv_shell_server_name";
    private const string FlowJobOpenClaw = "srv_job_openclaw";
    private const string FlowJobSlipnet = "srv_job_slipnet";
    private const string FlowJobDnstt = "srv_job_dnstt";

    private readonly IResponseSender _sender;
    private readonly IRemoteServerRepository _repo;
    private readonly IRemoteServerRuntimeService _runtime;
    private readonly IUserConversationStateStore _stateStore;
    private readonly IBotStageRepository _stageRepo;
    private readonly IPermissionRepository _permRepo;
    private readonly ITelegramUserRepository _userRepo;
    private readonly IUserMessageStateRepository? _msgStateRepo;

    public ServerOpsHandler(
        IResponseSender sender,
        IRemoteServerRepository repo,
        IRemoteServerRuntimeService runtime,
        IUserConversationStateStore stateStore,
        IBotStageRepository stageRepo,
        IPermissionRepository permRepo,
        ITelegramUserRepository userRepo,
        IUserMessageStateRepository? msgStateRepo = null)
    {
        _sender = sender;
        _repo = repo;
        _runtime = runtime;
        _stateStore = stateStore;
        _stageRepo = stageRepo;
        _permRepo = permRepo;
        _userRepo = userRepo;
        _msgStateRepo = msgStateRepo;
    }

    public string? Command => null;

    public bool CanHandle(BotUpdateContext context)
    {
        var t = context.MessageText?.Trim();
        if (string.IsNullOrWhiteSpace(t)) return false;
        if (context.IsCallbackQuery && t.StartsWith("srv_", StringComparison.OrdinalIgnoreCase))
            return true;

        var normalized = NormalizeButtonText(t);
        var isServerMenuText =
            normalized == NormalizeButtonText(BtnMenu) ||
            normalized == NormalizeButtonText(BtnMenuEn) ||
            (normalized.Contains("مدیریت", StringComparison.Ordinal) && normalized.Contains("سرور", StringComparison.Ordinal));

        // For non-callback messages we must allow pass-through, because in srv_shell state
        // any arbitrary text (e.g. "ls -la", "docker ps") is a valid command.
        if (!context.IsCallbackQuery)
            return true;

        return t.StartsWith("/server", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("/ssh", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("/openclaw", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("/slipnet", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("/dnstt", StringComparison.OrdinalIgnoreCase)
            || isServerMenuText
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
            || t.Equals(BtnInstallStatus, StringComparison.Ordinal)
            || t.Equals(BtnAccessGuide, StringComparison.Ordinal)
            || t.Equals(BtnGuide, StringComparison.Ordinal)
            || t.Equals(BtnBackMain, StringComparison.Ordinal)
            || t.Equals(BtnBackServers, StringComparison.Ordinal)
            || t.Equals(BtnBackPrev, StringComparison.Ordinal)
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
        var normalizedText = NormalizeButtonText(text);
        var isServerMenuText =
            normalizedText == NormalizeButtonText(BtnMenu) ||
            normalizedText == NormalizeButtonText(BtnMenuEn) ||
            (normalizedText.Contains("مدیریت", StringComparison.Ordinal) && normalizedText.Contains("سرور", StringComparison.Ordinal));

        if (cmd == "/serverhelp" || text == BtnGuide)
        {
            await ShowMainMenuAsync(context.ChatId, userId, cancellationToken).ConfigureAwait(false);
            await UpsertServerMessageAsync(context.ChatId, userId, HelpText(), null, cancellationToken).ConfigureAwait(false);
            return true;
        }

        if (isServerMenuText)
        {
            await ShowMainMenuAsync(context.ChatId, userId, cancellationToken).ConfigureAwait(false);
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
            await ShowServerListAsync(context.ChatId, userId, null, cancellationToken).ConfigureAwait(false);
            return true;
        }

        if (text == BtnConnect)
        {
            await StartServerPickFlowAsync(context.ChatId, userId, "connect", "یک سرور را انتخاب کن تا پنل عملیاتش باز شود:", cancellationToken).ConfigureAwait(false);
            return true;
        }

        if (text == BtnCommand)
        {
            await StartServerPickFlowAsync(context.ChatId, userId, "shell", "یک سرور را انتخاب کن و سپس وارد ترمینال شو:", cancellationToken).ConfigureAwait(false);
            return true;
        }

        if (text == BtnDisconnect)
        {
            await StartServerPickFlowAsync(context.ChatId, userId, "disconnect", "سرور موردنظر برای عملیات را انتخاب کن:", cancellationToken).ConfigureAwait(false);
            return true;
        }

        if (text == BtnDelete)
        {
            await StartServerPickFlowAsync(context.ChatId, userId, "delete", "سرور موردنظر برای حذف را انتخاب کن:", cancellationToken).ConfigureAwait(false);
            return true;
        }

        if (text == BtnInstallers)
        {
            await StartServerPickFlowAsync(context.ChatId, userId, "installers", "سرور هدف برای نصب ابزار را انتخاب کن:", cancellationToken).ConfigureAwait(false);
            return true;
        }

        if (text == BtnOpenClaw)
        {
            var serverId = await GetActiveShellServerIdAsync(userId, cancellationToken).ConfigureAwait(false);
            if (serverId is null)
            {
                await StartServerPickFlowAsync(context.ChatId, userId, "installers", "ابتدا یک سرور را انتخاب کن:", cancellationToken).ConfigureAwait(false);
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
                await StartServerPickFlowAsync(context.ChatId, userId, "installers", "ابتدا یک سرور را انتخاب کن:", cancellationToken).ConfigureAwait(false);
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
                await StartServerPickFlowAsync(context.ChatId, userId, "installers", "ابتدا یک سرور را انتخاب کن:", cancellationToken).ConfigureAwait(false);
                return true;
            }
            await RunInstallerWithProgressAsync(context.ChatId, userId, serverId.Value, "dnstt", cancellationToken).ConfigureAwait(false);
            return true;
        }

        if (text == BtnInstallStatus)
        {
            var serverId = await GetActiveShellServerIdAsync(userId, cancellationToken).ConfigureAwait(false);
            if (serverId is null)
            {
                await UpsertServerMessageAsync(context.ChatId, userId, "ابتدا وارد یک سرور شو تا وضعیت نصب همان سرور نمایش داده شود.", null, cancellationToken).ConfigureAwait(false);
                return true;
            }

            await ShowInstallerStatusAsync(context.ChatId, userId, serverId.Value, cancellationToken).ConfigureAwait(false);
            return true;
        }

        if (text == BtnAccessGuide)
        {
            var serverId = await GetActiveShellServerIdAsync(userId, cancellationToken).ConfigureAwait(false);
            if (serverId is null)
            {
                await UpsertServerMessageAsync(context.ChatId, userId, "ابتدا وارد یک سرور شو تا راهنمای دسترسی همان سرور نمایش داده شود.", null, cancellationToken).ConfigureAwait(false);
                return true;
            }

            await ShowAccessGuideAsync(context.ChatId, userId, serverId.Value, cancellationToken).ConfigureAwait(false);
            return true;
        }

        if (text == BtnCancel)
        {
            await _stateStore.ClearStateAsync(userId, cancellationToken).ConfigureAwait(false);
            await _stateStore.ClearAllFlowDataAsync(userId, cancellationToken).ConfigureAwait(false);
            await ShowMainMenuAsync(context.ChatId, userId, cancellationToken).ConfigureAwait(false);
            return true;
        }

        if (text == BtnBackServers)
        {
            await ShowServerListAsync(context.ChatId, userId, null, cancellationToken).ConfigureAwait(false);
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
            await UpsertServerMessageAsync(context.ChatId, userId, ok ? "سرور حذف شد." : "سرور پیدا نشد یا دسترسی نداری.", BuildMainServerReplyKeyboard(), cancellationToken).ConfigureAwait(false);
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
            await UpsertServerMessageAsync(context.ChatId, userId, result.Message, BuildMainServerReplyKeyboard(), cancellationToken).ConfigureAwait(false);
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
            await UpsertServerMessageAsync(context.ChatId, userId, $"{result.Message}\nExit: {result.ExitCode}{output}", BuildMainServerReplyKeyboard(), cancellationToken).ConfigureAwait(false);
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
            await UpsertServerMessageAsync(context.ChatId, userId, result.Message, BuildMainServerReplyKeyboard(), cancellationToken).ConfigureAwait(false);
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
            await UpsertServerMessageAsync(context.ChatId, userId, $"{result.Message}\nJobId: {result.JobId}", BuildShellReplyKeyboard(), cancellationToken).ConfigureAwait(false);
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
            await UpsertServerMessageAsync(context.ChatId, userId, $"{result.Message}\nJobId: {result.JobId}", BuildShellReplyKeyboard(), cancellationToken).ConfigureAwait(false);
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
            await UpsertServerMessageAsync(context.ChatId, userId, $"{result.Message}\nJobId: {result.JobId}", BuildShellReplyKeyboard(), cancellationToken).ConfigureAwait(false);
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
                await UpsertServerMessageAsync(context.ChatId, userId, "Job پیدا نشد.", BuildMainServerReplyKeyboard(), cancellationToken).ConfigureAwait(false);
                return true;
            }
            await UpsertServerMessageAsync(
                context.ChatId,
                userId,
                $"Job #{job.Id}\nStatus: {job.Status}\nServerId: {job.ServerId}\nCreated: {job.CreatedAt:u}\nStarted: {job.StartedAt:u}\nFinished: {job.FinishedAt:u}\n\nLog:\n{job.LogText}",
                BuildMainServerReplyKeyboard(),
                cancellationToken).ConfigureAwait(false);
            return true;
        }

        if (cmd == "/ssh")
        {
            await ShowMainMenuAsync(context.ChatId, userId, cancellationToken).ConfigureAwait(false);
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
            await ShowMainMenuAsync(context.ChatId, userId, ct).ConfigureAwait(false);
            return;
        }

        if (callback == "srv_list")
        {
            await ShowServerListAsync(context.ChatId, userId, null, ct).ConfigureAwait(false);
            return;
        }

        if (callback.StartsWith("srv_focus:", StringComparison.Ordinal))
        {
            if (int.TryParse(callback["srv_focus:".Length..], out var serverId))
                await ShowServerOperationsAsync(context.ChatId, userId, serverId, ct).ConfigureAwait(false);
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
                    userId,
                    $"اتصال به سرور #{serverId} در حال انجام است...",
                    $"{result.Message}",
                    BuildServerOperationsReplyKeyboard(),
                    ct).ConfigureAwait(false);
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
                    userId,
                    $"قطع اتصال سرور #{serverId}...",
                    result.Message,
                    BuildServerOperationsReplyKeyboard(),
                    ct).ConfigureAwait(false);
            }
            return;
        }

        if (callback.StartsWith("srv_del:", StringComparison.Ordinal))
        {
            if (int.TryParse(callback["srv_del:".Length..], out var serverId))
            {
                var ok = await _repo.DeleteAsync(serverId, userId, ct).ConfigureAwait(false);
                await UpsertServerMessageAsync(context.ChatId, userId, ok ? "سرور حذف شد." : "سرور پیدا نشد یا دسترسی نداری.", BuildMainServerReplyKeyboard(), ct).ConfigureAwait(false);
                await ShowServerListAsync(context.ChatId, userId, null, ct).ConfigureAwait(false);
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
                await ShowInstallerMenuAsync(context.ChatId, userId, serverId, ct).ConfigureAwait(false);
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
        if (text == BtnBackMain)
        {
            await _stateStore.ClearStateAsync(userId, ct).ConfigureAwait(false);
            await _stateStore.ClearAllFlowDataAsync(userId, ct).ConfigureAwait(false);
            await ShowCoreMainMenuAsync(context.ChatId, userId, ct).ConfigureAwait(false);
            return true;
        }

        if (text == BtnCancel)
        {
            await _stateStore.ClearStateAsync(userId, ct).ConfigureAwait(false);
            await _stateStore.ClearAllFlowDataAsync(userId, ct).ConfigureAwait(false);
            await ShowMainMenuAsync(context.ChatId, userId, ct).ConfigureAwait(false);
            return true;
        }

        switch (state)
        {
            case "srv_server_ops":
            {
                var serverId = await GetActiveShellServerIdAsync(userId, ct).ConfigureAwait(false);
                if (serverId is null)
                {
                    await _stateStore.ClearStateAsync(userId, ct).ConfigureAwait(false);
                    await ShowServerListAsync(context.ChatId, userId, "ابتدا یک سرور را انتخاب کن:", ct).ConfigureAwait(false);
                    return true;
                }

                if (text == BtnBackServers || text == BtnBackPrev)
                {
                    await ShowServerListAsync(context.ChatId, userId, null, ct).ConfigureAwait(false);
                    return true;
                }

                if (text == BtnConnect)
                {
                    var result = await _runtime.ConnectAsync(userId, serverId.Value, ct).ConfigureAwait(false);
                    await SendProgressResultAsync(context.ChatId, userId, $"اتصال به سرور #{serverId.Value} در حال انجام است...", result.Message, BuildServerOperationsReplyKeyboard(), ct).ConfigureAwait(false);
                    return true;
                }

                if (text == BtnDisconnect)
                {
                    var result = await _runtime.DisconnectAsync(userId, serverId.Value, ct).ConfigureAwait(false);
                    await SendProgressResultAsync(context.ChatId, userId, $"قطع اتصال سرور #{serverId.Value}...", result.Message, BuildServerOperationsReplyKeyboard(), ct).ConfigureAwait(false);
                    return true;
                }

                if (text == BtnCommand)
                {
                    await EnterShellModeAsync(context.ChatId, userId, serverId.Value, ct).ConfigureAwait(false);
                    return true;
                }

                if (text == BtnInstallers)
                {
                    await ShowInstallerMenuAsync(context.ChatId, userId, serverId.Value, ct).ConfigureAwait(false);
                    return true;
                }

                if (text == BtnInstallStatus)
                {
                    await ShowInstallerStatusAsync(context.ChatId, userId, serverId.Value, ct).ConfigureAwait(false);
                    return true;
                }

                if (text == BtnAccessGuide)
                {
                    await ShowAccessGuideAsync(context.ChatId, userId, serverId.Value, ct).ConfigureAwait(false);
                    return true;
                }

                if (text == BtnDelete)
                {
                    var ok = await _repo.DeleteAsync(serverId.Value, userId, ct).ConfigureAwait(false);
                    await _stateStore.ClearStateAsync(userId, ct).ConfigureAwait(false);
                    await _stateStore.ClearAllFlowDataAsync(userId, ct).ConfigureAwait(false);
                    await UpsertServerMessageAsync(context.ChatId, userId, ok ? "سرور حذف شد." : "سرور پیدا نشد یا دسترسی نداری.", BuildMainServerReplyKeyboard(), ct).ConfigureAwait(false);
                    await ShowServerListAsync(context.ChatId, userId, null, ct).ConfigureAwait(false);
                    return true;
                }

                return true;
            }
            case "srv_pick_server":
            {
                if (!TryParseServerIdFromSelection(text, out var selectedId))
                {
                    await UpsertServerMessageAsync(context.ChatId, userId, "انتخاب نامعتبر است. یکی از سرورها را از دکمه‌ها انتخاب کن.", BuildMainServerReplyKeyboard(), ct).ConfigureAwait(false);
                    return true;
                }

                var action = await _stateStore.GetFlowDataAsync(userId, "srv_pick_action", ct).ConfigureAwait(false) ?? "focus";
                await _stateStore.ClearStateAsync(userId, ct).ConfigureAwait(false);
                await _stateStore.SetFlowDataAsync(userId, FlowShellServerId, selectedId.ToString(), ct).ConfigureAwait(false);
                await HandleServerSelectionByActionAsync(context.ChatId, userId, selectedId, action, ct).ConfigureAwait(false);
                return true;
            }
            case "srv_add_name":
                await _stateStore.SetFlowDataAsync(userId, "srv_name", text, ct).ConfigureAwait(false);
                await _stateStore.SetStateAsync(userId, "srv_add_host", ct).ConfigureAwait(false);
                await UpsertServerMessageAsync(context.ChatId, userId, "IP یا Host سرور را وارد کن:", new List<IReadOnlyList<string>> { new[] { BtnCancel } }, ct).ConfigureAwait(false);
                return true;
            case "srv_add_host":
                await _stateStore.SetFlowDataAsync(userId, "srv_host", text, ct).ConfigureAwait(false);
                await _stateStore.SetStateAsync(userId, "srv_add_port", ct).ConfigureAwait(false);
                await UpsertServerMessageAsync(context.ChatId, userId, "Port را وارد کن (مثلا 22):", new List<IReadOnlyList<string>> { new[] { BtnCancel } }, ct).ConfigureAwait(false);
                return true;
            case "srv_add_port":
                if (!int.TryParse(text, out var port) || port is < 1 or > 65535)
                {
                    await UpsertServerMessageAsync(context.ChatId, userId, "پورت نامعتبر است. یک عدد بین 1 تا 65535 وارد کن.", new List<IReadOnlyList<string>> { new[] { BtnCancel } }, ct).ConfigureAwait(false);
                    return true;
                }
                await _stateStore.SetFlowDataAsync(userId, "srv_port", port.ToString(), ct).ConfigureAwait(false);
                await _stateStore.SetStateAsync(userId, "srv_add_username", ct).ConfigureAwait(false);
                await UpsertServerMessageAsync(context.ChatId, userId, "Username را وارد کن:", new List<IReadOnlyList<string>> { new[] { BtnCancel } }, ct).ConfigureAwait(false);
                return true;
            case "srv_add_username":
                await _stateStore.SetFlowDataAsync(userId, "srv_username", text, ct).ConfigureAwait(false);
                await _stateStore.SetStateAsync(userId, "srv_add_password", ct).ConfigureAwait(false);
                await UpsertServerMessageAsync(context.ChatId, userId, "Password را وارد کن:", new List<IReadOnlyList<string>> { new[] { BtnCancel } }, ct).ConfigureAwait(false);
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
                await UpsertServerMessageAsync(context.ChatId, userId, result.Message, BuildMainServerReplyKeyboard(), ct).ConfigureAwait(false);
                await ShowMainMenuAsync(context.ChatId, userId, ct).ConfigureAwait(false);
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
                await UpsertServerMessageAsync(context.ChatId, userId, $"{result.Message}\nExit: {result.ExitCode}{output}", BuildShellReplyKeyboard(), ct).ConfigureAwait(false);
                await _stateStore.ClearStateAsync(userId, ct).ConfigureAwait(false);
                await _stateStore.ClearAllFlowDataAsync(userId, ct).ConfigureAwait(false);
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

                if (text == BtnBackPrev)
                {
                    await _stateStore.ClearStateAsync(userId, ct).ConfigureAwait(false);
                    await ShowServerOperationsAsync(context.ChatId, userId, serverId.Value, ct).ConfigureAwait(false);
                    return true;
                }

                if (text == BtnDisconnect)
                {
                    var dis = await _runtime.DisconnectAsync(userId, serverId.Value, ct).ConfigureAwait(false);
                    await SendProgressResultAsync(
                        context.ChatId,
                        userId,
                        $"قطع اتصال سرور #{serverId.Value}...",
                        dis.Message,
                        BuildShellReplyKeyboard(),
                        ct).ConfigureAwait(false);
                    return true;
                }

                if (text == BtnInstallers)
                {
                    await ShowInstallerMenuAsync(context.ChatId, userId, serverId.Value, ct).ConfigureAwait(false);
                    return true;
                }

                if (text == BtnOpenClaw || text == BtnSlipnet || text == BtnDnstt)
                {
                    var tool = text == BtnOpenClaw ? "openclaw" : text == BtnSlipnet ? "slipnet" : "dnstt";
                    await RunInstallerWithProgressAsync(context.ChatId, userId, serverId.Value, tool, ct).ConfigureAwait(false);
                    return true;
                }

                if (text == BtnInstallStatus)
                {
                    await ShowInstallerStatusAsync(context.ChatId, userId, serverId.Value, ct).ConfigureAwait(false);
                    return true;
                }

                if (text == BtnAccessGuide)
                {
                    await ShowAccessGuideAsync(context.ChatId, userId, serverId.Value, ct).ConfigureAwait(false);
                    return true;
                }

                // In shell mode: any non-button text is treated as an SSH command.
                if (context.IncomingMessageId.HasValue)
                {
                    try { await _sender.DeleteMessageAsync(context.ChatId, context.IncomingMessageId.Value, ct).ConfigureAwait(false); } catch { }
                }
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
        await UpsertServerMessageAsync(
            chatId,
            userId,
            "اسم سرور را وارد کن (مثلا: VPS-Iran):",
            new List<IReadOnlyList<string>> { new[] { BtnCancel } },
            ct).ConfigureAwait(false);
    }

    private async Task ShowInstallerMenuAsync(long chatId, long userId, int serverId, CancellationToken ct)
    {
        await _stateStore.SetFlowDataAsync(userId, FlowShellServerId, serverId.ToString(), ct).ConfigureAwait(false);
        await UpsertServerMessageAsync(
            chatId,
            userId,
            $"نصب ابزار روی سرور #{serverId}\nیکی از Installerها را انتخاب کن:",
            new List<IReadOnlyList<string>>
            {
                new[] { BtnOpenClaw, BtnSlipnet },
                new[] { BtnDnstt, BtnInstallStatus },
                new[] { BtnAccessGuide, BtnBackPrev },
                new[] { BtnCancel }
            },
            ct).ConfigureAwait(false);
    }

    private async Task ShowMainMenuAsync(long chatId, long userId, CancellationToken ct)
    {
        await UpsertServerMessageAsync(
            chatId,
            userId,
            "مدیریت سرورها:\nیک گزینه را انتخاب کن.",
            BuildMainServerReplyKeyboard(),
            ct).ConfigureAwait(false);
    }

    private static List<IReadOnlyList<string>> BuildMainServerReplyKeyboard() =>
        new()
        {
            new[] { BtnAdd },
            new[] { BtnList },
            new[] { BtnGuide },
            new[] { BtnBackMain }
        };

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
        await UpsertServerMessageAsync(chatId, userId, text, keyboard, ct).ConfigureAwait(false);
    }

    private async Task StartServerPickFlowAsync(long chatId, long userId, string action, string title, CancellationToken ct)
    {
        await _stateStore.SetStateAsync(userId, "srv_pick_server", ct).ConfigureAwait(false);
        await _stateStore.SetFlowDataAsync(userId, "srv_pick_action", action, ct).ConfigureAwait(false);
        await ShowServerPickerAsync(chatId, userId, title, ct).ConfigureAwait(false);
    }

    private async Task ShowServerPickerAsync(long chatId, long userId, string title, CancellationToken ct)
    {
        var list = await _repo.ListByOwnerAsync(userId, ct).ConfigureAwait(false);
        if (list.Count == 0)
        {
            await UpsertServerMessageAsync(chatId, userId, "هنوز سروری ثبت نکردی.", BuildMainServerReplyKeyboard(), ct).ConfigureAwait(false);
            return;
        }

        var keyboard = new List<IReadOnlyList<string>>();
        var details = new List<string>();
        foreach (var srv in list.Take(15))
        {
            keyboard.Add(new[] { BuildServerSelectionLabel(srv.Id, srv.Name, srv.Host, srv.Port) });
            details.Add($"• #{srv.Id} — {srv.Name} ({srv.Host}:{srv.Port})");
        }
        keyboard.Add(new[] { BtnCancel, BtnBackMain });
        var text = $"{title}\n\n{string.Join('\n', details)}";
        await UpsertServerMessageAsync(chatId, userId, text, keyboard, ct).ConfigureAwait(false);
    }

    private async Task ShowServerListAsync(long chatId, long userId, string? title, CancellationToken ct)
    {
        var list = await _repo.ListByOwnerAsync(userId, ct).ConfigureAwait(false);
        if (list.Count == 0)
        {
            await UpsertServerMessageAsync(chatId, userId, "هنوز سروری ثبت نکردی. از «افزودن سرور» استفاده کن.", BuildMainServerReplyKeyboard(), ct).ConfigureAwait(false);
            return;
        }

        var keyboard = new List<IReadOnlyList<string>>();
        var details = new List<string>();
        foreach (var srv in list.Take(20))
        {
            keyboard.Add(new[] { BuildServerSelectionLabel(srv.Id, srv.Name, srv.Host, srv.Port) });
            details.Add($"• #{srv.Id} — {srv.Name} ({srv.Host}:{srv.Port})");
        }

        keyboard.Add(new[] { BtnCancel, BtnBackMain });

        var txt = (title ?? "لیست سرورها:\nیک سرور را انتخاب کن تا عملیاتش باز شود.") + "\n\n" + string.Join('\n', details);
        await _stateStore.SetStateAsync(userId, "srv_pick_server", ct).ConfigureAwait(false);
        await _stateStore.SetFlowDataAsync(userId, "srv_pick_action", "focus", ct).ConfigureAwait(false);
        await UpsertServerMessageAsync(chatId, userId, txt, keyboard, ct).ConfigureAwait(false);
    }

    private async Task ShowServerOperationsAsync(long chatId, long userId, int serverId, CancellationToken ct)
    {
        var list = await _repo.ListByOwnerAsync(userId, ct).ConfigureAwait(false);
        var srv = list.FirstOrDefault(x => x.Id == serverId);
        if (srv is null)
        {
            await UpsertServerMessageAsync(chatId, userId, "سرور پیدا نشد یا دسترسی نداری.", BuildMainServerReplyKeyboard(), ct).ConfigureAwait(false);
            return;
        }

        var text =
            $"<b>{srv.Name}</b>\n" +
            $"<code>{srv.Username}@{srv.Host}:{srv.Port}</code>\n\n" +
            "یک عملیات را انتخاب کن:";
        await _stateStore.SetFlowDataAsync(userId, FlowShellServerId, serverId.ToString(), ct).ConfigureAwait(false);
        await _stateStore.SetStateAsync(userId, "srv_server_ops", ct).ConfigureAwait(false);
        await UpsertServerMessageAsync(chatId, userId, text, BuildServerOperationsReplyKeyboard(), ct).ConfigureAwait(false);
    }

    private static List<IReadOnlyList<string>> BuildServerOperationsReplyKeyboard() =>
        new()
        {
            new[] { BtnConnect, BtnCommand },
            new[] { BtnInstallers, BtnInstallStatus },
            new[] { BtnAccessGuide, BtnDisconnect },
            new[] { BtnDelete, BtnBackServers },
            new[] { BtnBackMain }
        };

    private async Task EnterShellModeAsync(long chatId, long userId, int serverId, CancellationToken ct)
    {
        var connect = await _runtime.ConnectAsync(userId, serverId, ct).ConfigureAwait(false);
        if (!connect.Success)
        {
            await UpsertServerMessageAsync(chatId, userId, connect.Message, BuildMainServerReplyKeyboard(), ct).ConfigureAwait(false);
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
        await UpsertServerMessageAsync(chatId, userId, text, BuildShellReplyKeyboard(), ct).ConfigureAwait(false);
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
        await ShowMainMenuAsync(chatId, userId, ct).ConfigureAwait(false);
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
            $"<b>دستور:</b> <code>{EscapeHtml(command)}</code>\n" +
            $"Server: <code>{serverId}</code>\n" +
            $"ExitCode: <code>{result.ExitCode}</code>\n\n" +
            $"<pre>{EscapeHtml(output)}</pre>";

        // For shell command responses, do not send keyboard with progress/result messages.
        // Keyboard is already present in chat; this keeps a single editable response message.
        await SendProgressResultAsync(chatId, userId, progress, finalText, null, ct).ConfigureAwait(false);
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
        await UpsertServerMessageAsync(chatId, userId, startText, BuildShellReplyKeyboard(), ct).ConfigureAwait(false);

        RuntimeActionResult start = installerType.ToLowerInvariant() switch
        {
            "openclaw" => await _runtime.InstallOpenClawAsync(userId, serverId, ct).ConfigureAwait(false),
            "slipnet" => await _runtime.InstallSlipnetAsync(userId, serverId, ct).ConfigureAwait(false),
            "dnstt" => await _runtime.InstallDnsttAsync(userId, serverId, ct).ConfigureAwait(false),
            _ => new RuntimeActionResult(false, "Installer نامعتبر است.")
        };

        if (!start.Success || start.JobId is null)
        {
            await UpsertServerMessageAsync(chatId, userId, $"شروع نصب {friendly} ناموفق بود:\n{start.Message}", BuildShellReplyKeyboard(), ct).ConfigureAwait(false);
            return;
        }

        var jobKey = installerType.ToLowerInvariant() switch
        {
            "openclaw" => FlowJobOpenClaw,
            "slipnet" => FlowJobSlipnet,
            "dnstt" => FlowJobDnstt,
            _ => ""
        };
        if (!string.IsNullOrEmpty(jobKey))
            await _stateStore.SetFlowDataAsync(userId, jobKey, start.JobId.Value.ToString(), ct).ConfigureAwait(false);

        await UpsertServerMessageAsync(chatId, userId, $"نصب {friendly} شروع شد.\nJobId: <code>{start.JobId}</code>\nدر حال بررسی وضعیت...\n{BuildProgressBar(2)}", BuildShellReplyKeyboard(), ct).ConfigureAwait(false);

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
                await UpsertServerMessageAsync(chatId, userId, body, BuildShellReplyKeyboard(), ct).ConfigureAwait(false);

            lastStatus = status;
            if (status.Equals("completed", StringComparison.OrdinalIgnoreCase) || status.Equals("failed", StringComparison.OrdinalIgnoreCase))
                break;
        }
    }

    private async Task SendProgressResultAsync(
        long chatId,
        long userId,
        string progressText,
        string finalText,
        IReadOnlyList<IReadOnlyList<string>>? keyboard,
        CancellationToken ct)
    {
        await UpsertServerMessageAsync(chatId, userId, progressText, keyboard, ct).ConfigureAwait(false);
        await UpsertServerMessageAsync(chatId, userId, finalText, keyboard, ct).ConfigureAwait(false);
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

    private static string NormalizeButtonText(string value) =>
        value
            .Trim()
            .Replace("‌", "", StringComparison.Ordinal) // ZWNJ
            .Replace(" ", "", StringComparison.Ordinal);

    private static List<IReadOnlyList<string>> BuildShellReplyKeyboard() =>
        new()
        {
            new[] { BtnDisconnect, BtnInstallers },
            new[] { BtnInstallStatus, BtnAccessGuide },
            new[] { BtnShellExit, BtnBackPrev }
        };

    private async Task ShowInstallerStatusAsync(long chatId, long userId, int serverId, CancellationToken ct)
    {
        async Task<string> ReadJobStatusAsync(string jobKey, string title)
        {
            var idText = await _stateStore.GetFlowDataAsync(userId, jobKey, ct).ConfigureAwait(false);
            if (!long.TryParse(idText, out var jobId))
                return $"{title}: هنوز اجرا نشده";

            var job = await _runtime.GetInstallerJobAsync(userId, jobId, ct).ConfigureAwait(false);
            if (job is null)
                return $"{title}: Job پیدا نشد";

            var status = job.Status ?? "unknown";
            var finished = job.FinishedAt.HasValue ? job.FinishedAt.Value.ToString("u") : "-";
            return $"{title}: {status} (JobId: {job.Id}, Finished: {finished})";
        }

        async Task<string> ReadRuntimeStatusAsync(string title, string checkCommand)
        {
            var run = await _runtime.ExecuteCommandAsync(userId, serverId, checkCommand, ct).ConfigureAwait(false);
            if (!run.Success)
                return $"{title} (Runtime): نامشخص - {run.Message}";

            var line = (run.Output ?? "").Trim();
            if (string.IsNullOrWhiteSpace(line))
                line = $"ExitCode={run.ExitCode}";
            return $"{title} (Runtime): {line}";
        }

        var lines = new List<string>
        {
            $"وضعیت نصب ابزارها - سرور #{serverId}",
            await ReadJobStatusAsync(FlowJobOpenClaw, "OpenClaw").ConfigureAwait(false),
            await ReadJobStatusAsync(FlowJobSlipnet, "Slipnet").ConfigureAwait(false),
            await ReadJobStatusAsync(FlowJobDnstt, "DNSTT").ConfigureAwait(false),
            "",
            await ReadRuntimeStatusAsync("OpenClaw", "docker ps --format '{{.Names}}' | grep -i '^openclaw$' || echo not_running").ConfigureAwait(false),
            await ReadRuntimeStatusAsync("Slipnet", "docker ps --format '{{.Names}}' | grep -i '^slipnet-client$' || echo not_running").ConfigureAwait(false),
            await ReadRuntimeStatusAsync("DNSTT", "systemctl is-active dnstt 2>/dev/null || echo inactive").ConfigureAwait(false)
        };

        await UpsertServerMessageAsync(chatId, userId, string.Join('\n', lines), BuildShellReplyKeyboard(), ct).ConfigureAwait(false);
    }

    private async Task ShowAccessGuideAsync(long chatId, long userId, int serverId, CancellationToken ct)
    {
        var srv = (await _repo.ListByOwnerAsync(userId, ct).ConfigureAwait(false)).FirstOrDefault(x => x.Id == serverId);
        var host = srv?.Host ?? "<server-ip>";
        var text =
            $"راهنمای دسترسی ابزارها - سرور #{serverId}\n\n" +
            $"OpenClaw:\n" +
            $"- URL: http://{host}:18789\n" +
            "- container: openclaw\n" +
            "- check: docker ps | grep openclaw\n" +
            "- logs: docker logs --tail 100 openclaw\n\n" +
            "Slipnet:\n" +
            "- container: slipnet-client\n" +
            "- check: docker ps | grep slipnet-client\n" +
            "- logs: docker logs --tail 100 slipnet-client\n\n" +
            "DNSTT:\n" +
            "- binary path: /usr/local/dnstt\n" +
            "- systemd unit: /etc/systemd/system/dnstt.service\n" +
            "- status: systemctl status dnstt\n" +
            "- logs: journalctl -u dnstt -n 100 --no-pager\n" +
            "- note: برای کارکرد کامل نیاز به domain + key صحیح در unit دارد.";

        await UpsertServerMessageAsync(chatId, userId, text, BuildShellReplyKeyboard(), ct).ConfigureAwait(false);
    }

    private async Task HandleServerSelectionByActionAsync(long chatId, long userId, int serverId, string action, CancellationToken ct)
    {
        switch (action)
        {
            case "connect":
            {
                var result = await _runtime.ConnectAsync(userId, serverId, ct).ConfigureAwait(false);
                await SendProgressResultAsync(chatId, userId, $"اتصال به سرور #{serverId} در حال انجام است...", result.Message, BuildServerOperationsReplyKeyboard(), ct).ConfigureAwait(false);
                break;
            }
            case "disconnect":
            {
                var result = await _runtime.DisconnectAsync(userId, serverId, ct).ConfigureAwait(false);
                await SendProgressResultAsync(chatId, userId, $"قطع اتصال سرور #{serverId}...", result.Message, BuildServerOperationsReplyKeyboard(), ct).ConfigureAwait(false);
                break;
            }
            case "delete":
            {
                var ok = await _repo.DeleteAsync(serverId, userId, ct).ConfigureAwait(false);
                await UpsertServerMessageAsync(chatId, userId, ok ? "سرور حذف شد." : "سرور پیدا نشد یا دسترسی نداری.", BuildMainServerReplyKeyboard(), ct).ConfigureAwait(false);
                break;
            }
            case "shell":
                await EnterShellModeAsync(chatId, userId, serverId, ct).ConfigureAwait(false);
                break;
            case "installers":
                await ShowInstallerMenuAsync(chatId, userId, serverId, ct).ConfigureAwait(false);
                break;
            case "focus":
            default:
                await ShowServerOperationsAsync(chatId, userId, serverId, ct).ConfigureAwait(false);
                break;
        }
    }

    private static string BuildServerSelectionLabel(int serverId, string name, string host, int port) =>
        $"🖥 {name} (#{serverId}) {host}:{port}";

    private static bool TryParseServerIdFromSelection(string text, out int serverId)
    {
        serverId = 0;
        if (int.TryParse(text, out var directId) && directId > 0)
        {
            serverId = directId;
            return true;
        }

        var markerIndex = text.IndexOf("(#", StringComparison.Ordinal);
        if (markerIndex < 0) return false;
        var from = markerIndex + 2;
        var to = text.IndexOf(')', from);
        if (to <= from) return false;
        return int.TryParse(text[from..to], out serverId) && serverId > 0;
    }

    private async Task UpsertServerMessageAsync(
        long chatId,
        long userId,
        string text,
        IReadOnlyList<IReadOnlyList<string>>? keyboard,
        CancellationToken ct)
    {
        if (keyboard != null)
            await _sender.UpdateReplyKeyboardSilentAsync(chatId, keyboard, ct).ConfigureAwait(false);

        if (await TryEditLastBotMessageAsync(chatId, userId, text, ct).ConfigureAwait(false))
            return;

        if (keyboard != null)
            await _sender.SendTextMessageWithReplyKeyboardAsync(chatId, text, keyboard, ct).ConfigureAwait(false);
        else
            await _sender.SendTextMessageAsync(chatId, text, ct).ConfigureAwait(false);
    }

    private async Task<bool> TryEditLastBotMessageAsync(long chatId, long userId, string text, CancellationToken ct)
    {
        if (_msgStateRepo == null) return false;
        try
        {
            var state = await _msgStateRepo.GetUserMessageStateAsync(userId, ct).ConfigureAwait(false);
            if (state?.LastBotTelegramMessageId is > 0 and <= int.MaxValue)
            {
                await _sender.EditMessageTextAsync(chatId, (int)state.LastBotTelegramMessageId.Value, text, ct).ConfigureAwait(false);
                return true;
            }
        }
        catch
        {
            // Fall back to normal send if edit fails (e.g. message is too old or deleted).
        }
        return false;
    }

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
        - دکمه‌ها به صورت Reply Keyboard هستند (نه inline زیر پیام)
        - دکمه‌های حالت سرور:
          قطع اتصال | نصب ابزارها | وضعیت نصب ابزارها | راهنمای دسترسی ابزارها | خروج از سرور | بازگشت به منوی اصلی

        5) نصب ابزارها:
        - از پنل سرور یا داخل ترمینال: «نصب ابزارها»
        - ابزارها:
          نصب OpenClaw
          نصب Slipnet
          نصب DNSTT
        - وضعیت نصب مرحله‌به‌مرحله با همان پیام (edit) بروزرسانی می‌شود

        6) وضعیت نصب و دسترسی:
        - «وضعیت نصب ابزارها» آخرین وضعیت هر ابزار را نشان می‌دهد
        - «راهنمای دسترسی ابزارها» آدرس/دستورهای دسترسی و لاگ هر ابزار را می‌دهد

        7) خروج:
        - «خروج از سرور» -> بازگشت به مدیریت سرورها
        - «بازگشت به منوی اصلی» -> بازگشت به main menu

        8) دستورات مستقیم (اختیاری):
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
