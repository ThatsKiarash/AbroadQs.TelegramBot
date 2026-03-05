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
        string jobType,
        CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IRemoteServerRepository>();
        var jobId = await repo.CreateInstallerJobAsync(
            new RemoteInstallerJobCreateDto(serverId, actorTelegramUserId, jobType, null), ct).ConfigureAwait(false);

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
                var script = BuildInstallScript(jobType);
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
                    jobType,
                    jobType,
                    status == "success",
                    result.ExitCode,
                    result.DurationMs,
                    Truncate(log, 2000),
                    result.Error,
                    json), ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{JobType} failed for server {ServerId}", jobType, serverId);
                using var failScope = _scopeFactory.CreateScope();
                var failRepo = failScope.ServiceProvider.GetRequiredService<IRemoteServerRepository>();
                await failRepo.UpdateInstallerJobAsync(
                    new RemoteInstallerJobUpdateDto(jobId, "failed", Truncate(ex.ToString(), 30000), null, null, DateTimeOffset.UtcNow), ct).ConfigureAwait(false);
            }
        }, ct);

        return (true, jobId, $"{jobType} installer job queued: {jobId}");
    }

    private static string BuildInstallScript(string jobType)
    {
        if (string.Equals(jobType, "slipnet_install", StringComparison.OrdinalIgnoreCase))
            return BuildSlipnetInstallScript();

        if (string.Equals(jobType, "dnstt_install", StringComparison.OrdinalIgnoreCase))
            return BuildDnsttInstallScript();

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

    private static string BuildSlipnetInstallScript()
    {
        var sb = new StringBuilder();
        sb.AppendLine("set -e");
        sb.AppendLine("echo '[1/4] Preflight' ");
        sb.AppendLine("uname -a || true");
        sb.AppendLine("echo '[2/4] Ensure Docker' ");
        sb.AppendLine("if ! command -v docker >/dev/null 2>&1; then curl -fsSL https://get.docker.com | sh; fi");
        sb.AppendLine("echo '[3/4] Deploy Slipnet-like tunnel stack (client + watchtower)' ");
        sb.AppendLine("mkdir -p ~/slipnet");
        sb.AppendLine("cat > ~/slipnet/docker-compose.yml <<'EOF'");
        sb.AppendLine("services:");
        sb.AppendLine("  slipnet-client:");
        sb.AppendLine("    image: ghcr.io/slippednet/client:latest");
        sb.AppendLine("    restart: unless-stopped");
        sb.AppendLine("    network_mode: host");
        sb.AppendLine("    environment:");
        sb.AppendLine("      - SLIPNET_TOKEN=change-me");
        sb.AppendLine("  watchtower:");
        sb.AppendLine("    image: containrrr/watchtower:latest");
        sb.AppendLine("    restart: unless-stopped");
        sb.AppendLine("    volumes:");
        sb.AppendLine("      - /var/run/docker.sock:/var/run/docker.sock");
        sb.AppendLine("    command: --interval 300");
        sb.AppendLine("EOF");
        sb.AppendLine("docker compose -f ~/slipnet/docker-compose.yml up -d");
        sb.AppendLine("echo '[4/4] Status' ");
        sb.AppendLine("docker ps --format 'table {{.Names}}\\t{{.Image}}\\t{{.Status}}' | head -n 10");
        return sb.ToString();
    }

    private static string BuildDnsttInstallScript()
    {
        var sb = new StringBuilder();
        sb.AppendLine("set -e");
        sb.AppendLine("echo '[1/5] Preflight' ");
        sb.AppendLine("uname -a || true");
        sb.AppendLine("echo '[2/5] Install dependencies' ");
        sb.AppendLine("if command -v apt-get >/dev/null 2>&1; then apt-get update -y && apt-get install -y curl wget tar; fi");
        sb.AppendLine("echo '[3/5] Download dnstt binaries' ");
        sb.AppendLine("mkdir -p /usr/local/dnstt");
        sb.AppendLine("cd /usr/local/dnstt");
        sb.AppendLine("if [ ! -f dnstt-server ]; then");
        sb.AppendLine("  wget -qO dnstt.tar.gz https://github.com/tladesignz/dnstt/releases/latest/download/dnstt-linux-amd64.tar.gz || true");
        sb.AppendLine("  if [ -f dnstt.tar.gz ]; then tar -xzf dnstt.tar.gz || true; fi");
        sb.AppendLine("fi");
        sb.AppendLine("chmod +x /usr/local/dnstt/dnstt-server || true");
        sb.AppendLine("echo '[4/5] Create systemd unit template' ");
        sb.AppendLine("cat > /etc/systemd/system/dnstt.service <<'EOF'");
        sb.AppendLine("[Unit]");
        sb.AppendLine("Description=dnstt server");
        sb.AppendLine("After=network.target");
        sb.AppendLine("[Service]");
        sb.AppendLine("Type=simple");
        sb.AppendLine("ExecStart=/usr/local/dnstt/dnstt-server -udp :5300 -privkey-file /usr/local/dnstt/server.key example.com 127.0.0.1:8080");
        sb.AppendLine("Restart=always");
        sb.AppendLine("RestartSec=5");
        sb.AppendLine("[Install]");
        sb.AppendLine("WantedBy=multi-user.target");
        sb.AppendLine("EOF");
        sb.AppendLine("systemctl daemon-reload || true");
        sb.AppendLine("echo '[5/5] Done (manual key/domain config required)' ");
        sb.AppendLine("echo 'dnstt binary placed in /usr/local/dnstt; edit /etc/systemd/system/dnstt.service then systemctl enable --now dnstt'");
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
