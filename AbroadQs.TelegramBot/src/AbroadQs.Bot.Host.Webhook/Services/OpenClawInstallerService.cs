using AbroadQs.Bot.Contracts;
using System.Text;

namespace AbroadQs.Bot.Host.Webhook.Services;

public sealed class OpenClawInstallerService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OpenClawInstallerService> _logger;

    public OpenClawInstallerService(
        IServiceScopeFactory scopeFactory,
        ILogger<OpenClawInstallerService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<(bool Success, long JobId, string Message)> QueueAndRunAsync(
        int serverId,
        long actorTelegramUserId,
        CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IRemoteServerRepository>();
        var jobId = await repo.CreateInstallerJobAsync(
            new RemoteInstallerJobCreateDto(serverId, actorTelegramUserId, "openclaw_install", null), ct).ConfigureAwait(false);

        _ = Task.Run(async () =>
        {
            try
            {
                using var runScope = _scopeFactory.CreateScope();
                var runRepo = runScope.ServiceProvider.GetRequiredService<IRemoteServerRepository>();
                var runCrypto = runScope.ServiceProvider.GetRequiredService<SecretCryptoService>();
                var runSsh = runScope.ServiceProvider.GetRequiredService<SshSessionManager>();

                await runRepo.UpdateInstallerJobAsync(
                    new RemoteInstallerJobUpdateDto(jobId, "running", null, null, DateTimeOffset.UtcNow, null), ct).ConfigureAwait(false);

                var server = await runRepo.GetByIdAsync(serverId, ct).ConfigureAwait(false);
                if (server is null)
                {
                    await runRepo.UpdateInstallerJobAsync(
                        new RemoteInstallerJobUpdateDto(jobId, "failed", "Server not found.", null, null, DateTimeOffset.UtcNow), ct).ConfigureAwait(false);
                    return;
                }

                var secret = runCrypto.Decrypt(server.EncryptedSecret, server.SecretNonce, server.SecretTag);
                var script = BuildInstallScript();
                var result = await runSsh.RunOneOffAsync(
                    server.Host, server.Port, server.Username, secret, server.AuthType, script, 420, ct).ConfigureAwait(false);

                var log = Redact($"{result.StdOut}\n{result.StdErr}");
                var status = result.Success && result.ExitCode.GetValueOrDefault(1) == 0 ? "success" : "failed";
                var json = $"{{\"exitCode\":{result.ExitCode?.ToString() ?? "null"},\"durationMs\":{result.DurationMs}}}";

                await runRepo.UpdateInstallerJobAsync(
                    new RemoteInstallerJobUpdateDto(jobId, status, Truncate(log, 30000), json, null, DateTimeOffset.UtcNow), ct).ConfigureAwait(false);

                await runRepo.AddAuditAsync(new RemoteServerAuditCreateDto(
                    serverId,
                    actorTelegramUserId,
                    "openclaw_install",
                    "openclaw_install",
                    status == "success",
                    result.ExitCode,
                    result.DurationMs,
                    Truncate(log, 2000),
                    result.Error,
                    json), ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OpenClaw install failed for server {ServerId}", serverId);
                using var failScope = _scopeFactory.CreateScope();
                var failRepo = failScope.ServiceProvider.GetRequiredService<IRemoteServerRepository>();
                await failRepo.UpdateInstallerJobAsync(
                    new RemoteInstallerJobUpdateDto(jobId, "failed", Truncate(ex.ToString(), 30000), null, null, DateTimeOffset.UtcNow), ct).ConfigureAwait(false);
            }
        }, ct);

        return (true, jobId, $"OpenClaw installer job queued: {jobId}");
    }

    private static string BuildInstallScript()
    {
        var sb = new StringBuilder();
        sb.AppendLine("set -e");
        sb.AppendLine("echo '[1/5] Preflight checks' ");
        sb.AppendLine("uname -a || true");
        sb.AppendLine("id || true");
        sb.AppendLine("echo '[2/5] Ensure Docker' ");
        sb.AppendLine("if ! command -v docker >/dev/null 2>&1; then curl -fsSL https://get.docker.com | sh; fi");
        sb.AppendLine("echo '[3/5] Ensure Docker Compose plugin' ");
        sb.AppendLine("if ! docker compose version >/dev/null 2>&1; then");
        sb.AppendLine("  (apt-get update -y && apt-get install -y docker-compose-plugin) || true");
        sb.AppendLine("fi");
        sb.AppendLine("echo '[4/5] Deploy OpenClaw (docker-first profile)' ");
        sb.AppendLine("mkdir -p ~/openclaw");
        sb.AppendLine("cat > ~/openclaw/docker-compose.yml <<'EOF'");
        sb.AppendLine("services:");
        sb.AppendLine("  openclaw:");
        sb.AppendLine("    image: ghcr.io/openclawai/openclaw:latest");
        sb.AppendLine("    restart: unless-stopped");
        sb.AppendLine("    ports:");
        sb.AppendLine("      - \"3000:3000\"");
        sb.AppendLine("EOF");
        sb.AppendLine("docker compose -f ~/openclaw/docker-compose.yml up -d");
        sb.AppendLine("echo '[5/5] Health check' ");
        sb.AppendLine("docker ps --format 'table {{.Names}}\\t{{.Image}}\\t{{.Status}}' | head -n 10");
        return sb.ToString();
    }

    private static string Truncate(string? text, int max)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return text.Length <= max ? text : text[..max];
    }

    private static string Redact(string text)
    {
        var redacted = text.Replace("password", "***", StringComparison.OrdinalIgnoreCase);
        redacted = redacted.Replace("token", "***", StringComparison.OrdinalIgnoreCase);
        return redacted;
    }
}
