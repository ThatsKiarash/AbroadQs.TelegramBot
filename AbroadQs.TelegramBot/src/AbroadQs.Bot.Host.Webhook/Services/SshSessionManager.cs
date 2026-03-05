using Renci.SshNet;
using Renci.SshNet.Common;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace AbroadQs.Bot.Host.Webhook.Services;

public sealed class SshSessionManager : IDisposable
{
    private readonly ConcurrentDictionary<string, SshClient> _sessions = new();
    private readonly ILogger<SshSessionManager> _logger;

    public SshSessionManager(ILogger<SshSessionManager> logger)
    {
        _logger = logger;
    }

    public string BuildKey(long userId, int serverId) => $"{userId}:{serverId}";

    public async Task<(bool Success, string? Error)> ConnectAsync(
        string key,
        string host,
        int port,
        string username,
        string secret,
        string authType,
        CancellationToken ct)
    {
        try
        {
            await Task.Run(() =>
            {
                Disconnect(key);
                var client = CreateClient(host, port, username, secret, authType);
                client.ConnectionInfo.Timeout = TimeSpan.FromSeconds(12);
                client.Connect();
                _sessions[key] = client;
            }, ct).ConfigureAwait(false);
            return (true, null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SSH connect failed for key {Key}", key);
            return (false, ex.Message);
        }
    }

    public async Task<SshCommandResult> ExecuteCommandAsync(string key, string commandText, int timeoutSeconds, CancellationToken ct)
    {
        if (!_sessions.TryGetValue(key, out var client) || !client.IsConnected)
            return new(false, null, null, null, "No active SSH session.");

        try
        {
            return await Task.Run(() =>
            {
                var sw = Stopwatch.StartNew();
                var cmd = client.CreateCommand(commandText);
                cmd.CommandTimeout = TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 1, 300));
                var output = cmd.Execute();
                sw.Stop();
                var err = string.IsNullOrWhiteSpace(cmd.Error) ? null : cmd.Error;
                return new SshCommandResult(true, output, err, cmd.ExitStatus, null, sw.ElapsedMilliseconds);
            }, ct).ConfigureAwait(false);
        }
        catch (SshOperationTimeoutException ex)
        {
            return new(false, null, null, null, $"Command timeout: {ex.Message}");
        }
        catch (Exception ex)
        {
            return new(false, null, null, null, ex.Message);
        }
    }

    public bool Disconnect(string key)
    {
        if (!_sessions.TryRemove(key, out var client))
            return false;
        try
        {
            if (client.IsConnected) client.Disconnect();
            client.Dispose();
        }
        catch { }
        return true;
    }

    public bool IsConnected(string key)
        => _sessions.TryGetValue(key, out var client) && client.IsConnected;

    public async Task<SshCommandResult> RunOneOffAsync(
        string host,
        int port,
        string username,
        string secret,
        string authType,
        string commandText,
        int timeoutSeconds,
        CancellationToken ct)
    {
        try
        {
            return await Task.Run(() =>
            {
                using var client = CreateClient(host, port, username, secret, authType);
                client.ConnectionInfo.Timeout = TimeSpan.FromSeconds(12);
                client.Connect();
                var sw = Stopwatch.StartNew();
                var cmd = client.CreateCommand(commandText);
                cmd.CommandTimeout = TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 1, 600));
                var output = cmd.Execute();
                sw.Stop();
                var err = string.IsNullOrWhiteSpace(cmd.Error) ? null : cmd.Error;
                if (client.IsConnected) client.Disconnect();
                return new SshCommandResult(true, output, err, cmd.ExitStatus, null, sw.ElapsedMilliseconds);
            }, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new(false, null, null, null, ex.Message);
        }
    }

    private static SshClient CreateClient(string host, int port, string username, string secret, string authType)
    {
        if (string.Equals(authType, "private_key", StringComparison.OrdinalIgnoreCase))
        {
            var keyBytes = Encoding.UTF8.GetBytes(secret);
            using var ms = new MemoryStream(keyBytes);
            var key = new PrivateKeyFile(ms);
            return new SshClient(host, port, username, key);
        }
        return new SshClient(host, port, username, secret);
    }

    public void Dispose()
    {
        foreach (var key in _sessions.Keys.ToArray())
            Disconnect(key);
    }
}

public sealed record SshCommandResult(
    bool Success,
    string? StdOut,
    string? StdErr,
    int? ExitCode,
    string? Error,
    long DurationMs = 0);
