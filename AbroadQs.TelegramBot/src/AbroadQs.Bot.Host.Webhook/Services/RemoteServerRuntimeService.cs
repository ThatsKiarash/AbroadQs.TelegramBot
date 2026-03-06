using AbroadQs.Bot.Contracts;

namespace AbroadQs.Bot.Host.Webhook.Services;

public sealed class RemoteServerRuntimeService : IRemoteServerRuntimeService
{
    private readonly IRemoteServerRepository _repo;
    private readonly SecretCryptoService _crypto;
    private readonly SshSessionManager _ssh;
    private readonly OpenClawInstallerService _installer;
    private readonly ActionRateLimiter _limiter;

    public RemoteServerRuntimeService(
        IRemoteServerRepository repo,
        SecretCryptoService crypto,
        SshSessionManager ssh,
        OpenClawInstallerService installer,
        ActionRateLimiter limiter)
    {
        _repo = repo;
        _crypto = crypto;
        _ssh = ssh;
        _installer = installer;
        _limiter = limiter;
    }

    public async Task<RuntimeActionResult> AddServerAsync(
        long actorTelegramUserId,
        string name,
        string host,
        int port,
        string username,
        string secret,
        string authType = "password",
        string? tags = null,
        string? description = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(secret))
            return new(false, "ورودی‌ها کامل نیست.");
        if (port is < 1 or > 65535)
            return new(false, "پورت نامعتبر است.");

        var enc = _crypto.Encrypt(secret);
        var dto = await _repo.AddAsync(new RemoteServerCreateDto(
            actorTelegramUserId,
            name.Trim(),
            host.Trim(),
            port,
            username.Trim(),
            authType,
            enc.CipherText,
            enc.Nonce,
            enc.Tag,
            tags,
            description), ct).ConfigureAwait(false);

        await _repo.AddAuditAsync(new RemoteServerAuditCreateDto(
            dto.Id,
            actorTelegramUserId,
            "server_create",
            null,
            true,
            null,
            null,
            null,
            null,
            null), ct).ConfigureAwait(false);

        return new(true, $"سرور ثبت شد. ServerId: {dto.Id}");
    }

    public async Task<RuntimeActionResult> ConnectAsync(long actorTelegramUserId, int serverId, CancellationToken ct = default)
    {
        if (!_limiter.IsAllowed($"connect:{actorTelegramUserId}:{serverId}", TimeSpan.FromSeconds(3)))
            return new(false, "درخواست اتصال خیلی سریع تکرار شد. چند ثانیه صبر کن.");

        var server = await _repo.GetByIdAsync(serverId, ct).ConfigureAwait(false);
        if (server is null || server.OwnerTelegramUserId != actorTelegramUserId)
            return new(false, "سرور پیدا نشد.");

        var secret = _crypto.Decrypt(server.EncryptedSecret, server.SecretNonce, server.SecretTag);
        var key = _ssh.BuildKey(actorTelegramUserId, serverId);
        var result = await _ssh.ConnectAsync(key, server.Host, server.Port, server.Username, secret, server.AuthType, ct).ConfigureAwait(false);
        await _repo.AddAuditAsync(new RemoteServerAuditCreateDto(
            serverId,
            actorTelegramUserId,
            "connect",
            null,
            result.Success,
            null,
            null,
            null,
            result.Error,
            null), ct).ConfigureAwait(false);

        if (result.Success)
        {
            await _repo.TouchLastConnectedAsync(serverId, ct).ConfigureAwait(false);
            return new(true, $"اتصال SSH برقرار شد: {server.Name} ({server.Host}:{server.Port})");
        }
        return new(false, $"اتصال برقرار نشد: {result.Error}");
    }

    public async Task<RuntimeActionResult> ExecuteCommandAsync(long actorTelegramUserId, int serverId, string commandText, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(commandText))
            return new(false, "دستور خالی است.");
        if (!_limiter.IsAllowed($"cmd:{actorTelegramUserId}:{serverId}", TimeSpan.FromMilliseconds(800)))
            return new(false, "دستورها خیلی سریع ارسال می‌شوند. کمی صبر کن.");

        var server = await _repo.GetByIdAsync(serverId, ct).ConfigureAwait(false);
        if (server is null || server.OwnerTelegramUserId != actorTelegramUserId)
            return new(false, "سرور پیدا نشد.");

        var key = _ssh.BuildKey(actorTelegramUserId, serverId);
        if (!_ssh.IsConnected(key))
        {
            var secret = _crypto.Decrypt(server.EncryptedSecret, server.SecretNonce, server.SecretTag);
            var conn = await _ssh.ConnectAsync(key, server.Host, server.Port, server.Username, secret, server.AuthType, ct).ConfigureAwait(false);
            if (!conn.Success)
                return new(false, $"جلسه SSH فعال نیست و اتصال خودکار شکست خورد: {conn.Error}");
        }

        var exec = await _ssh.ExecuteCommandAsync(key, commandText, 60, ct).ConfigureAwait(false);
        var output = $"{exec.StdOut}\n{exec.StdErr}".Trim();
        var preview = Truncate(Redact(output), 1800);
        var safeCommandText = SanitizeCommandText(commandText);
        await _repo.AddAuditAsync(new RemoteServerAuditCreateDto(
            serverId,
            actorTelegramUserId,
            "command",
            safeCommandText,
            exec.Success,
            exec.ExitCode,
            exec.DurationMs,
            preview,
            exec.Error,
            null), ct).ConfigureAwait(false);

        if (!exec.Success)
            return new(false, $"اجرای دستور ناموفق بود: {exec.Error ?? "unknown"}");

        return new(true, "دستور اجرا شد.", exec.ExitCode, preview, null, exec.DurationMs);
    }

    public async Task<RuntimeActionResult> DisconnectAsync(long actorTelegramUserId, int serverId, CancellationToken ct = default)
    {
        var server = await _repo.GetByIdAsync(serverId, ct).ConfigureAwait(false);
        if (server is null || server.OwnerTelegramUserId != actorTelegramUserId)
            return new(false, "سرور پیدا نشد.");
        var key = _ssh.BuildKey(actorTelegramUserId, serverId);
        var closed = _ssh.Disconnect(key);
        await _repo.AddAuditAsync(new RemoteServerAuditCreateDto(
            serverId,
            actorTelegramUserId,
            "disconnect",
            null,
            closed,
            null,
            null,
            null,
            null,
            null), ct).ConfigureAwait(false);
        return closed ? new(true, "جلسه SSH بسته شد.") : new(false, "جلسه فعالی وجود نداشت.");
    }

    public async Task<RuntimeActionResult> InstallOpenClawAsync(long actorTelegramUserId, int serverId, CancellationToken ct = default)
    {
        if (!_limiter.IsAllowed($"openclaw:{actorTelegramUserId}:{serverId}", TimeSpan.FromSeconds(10)))
            return new(false, "درخواست نصب خیلی سریع تکرار شد.");

        var server = await _repo.GetByIdAsync(serverId, ct).ConfigureAwait(false);
        if (server is null || server.OwnerTelegramUserId != actorTelegramUserId)
            return new(false, "سرور پیدا نشد.");

        var job = await _installer.QueueAndRunAsync(serverId, actorTelegramUserId, "openclaw_install", ct).ConfigureAwait(false);
        return new(job.Success, job.Message, null, null, job.JobId);
    }

    public async Task<RuntimeActionResult> InstallSlipnetAsync(long actorTelegramUserId, int serverId, CancellationToken ct = default)
    {
        if (!_limiter.IsAllowed($"slipnet:{actorTelegramUserId}:{serverId}", TimeSpan.FromSeconds(10)))
            return new(false, "درخواست نصب خیلی سریع تکرار شد.");

        var server = await _repo.GetByIdAsync(serverId, ct).ConfigureAwait(false);
        if (server is null || server.OwnerTelegramUserId != actorTelegramUserId)
            return new(false, "سرور پیدا نشد.");

        var job = await _installer.QueueAndRunAsync(serverId, actorTelegramUserId, "slipnet_install", ct).ConfigureAwait(false);
        return new(job.Success, job.Message, null, null, job.JobId);
    }

    public async Task<RuntimeActionResult> InstallDnsttAsync(long actorTelegramUserId, int serverId, CancellationToken ct = default)
    {
        if (!_limiter.IsAllowed($"dnstt:{actorTelegramUserId}:{serverId}", TimeSpan.FromSeconds(10)))
            return new(false, "درخواست نصب خیلی سریع تکرار شد.");

        var server = await _repo.GetByIdAsync(serverId, ct).ConfigureAwait(false);
        if (server is null || server.OwnerTelegramUserId != actorTelegramUserId)
            return new(false, "سرور پیدا نشد.");

        var job = await _installer.QueueAndRunAsync(serverId, actorTelegramUserId, "dnstt_install", ct).ConfigureAwait(false);
        return new(job.Success, job.Message, null, null, job.JobId);
    }

    public async Task<RemoteInstallerJobDto?> GetInstallerJobAsync(long actorTelegramUserId, long jobId, CancellationToken ct = default)
    {
        var job = await _repo.GetInstallerJobAsync(jobId, ct).ConfigureAwait(false);
        if (job is null) return null;
        return job.ActorTelegramUserId == actorTelegramUserId ? job : null;
    }

    private static string Truncate(string? text, int max)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return text.Length <= max ? text : text[..max];
    }

    private static string Redact(string text)
    {
        var x = text.Replace("password", "***", StringComparison.OrdinalIgnoreCase);
        x = x.Replace("token", "***", StringComparison.OrdinalIgnoreCase);
        return x;
    }

    private static string SanitizeCommandText(string commandText)
    {
        if (commandText.Contains("--token", StringComparison.OrdinalIgnoreCase)
            || commandText.Contains("token ", StringComparison.OrdinalIgnoreCase)
            || commandText.Contains("token=", StringComparison.OrdinalIgnoreCase))
        {
            return "[REDACTED: sensitive command]";
        }

        return commandText.Length <= 400 ? commandText : commandText[..400];
    }
}
