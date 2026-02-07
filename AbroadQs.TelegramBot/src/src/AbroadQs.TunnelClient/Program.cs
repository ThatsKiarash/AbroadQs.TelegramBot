using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

var JsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
var serverUrl = "https://webhook.abroadqs.com";
var localPort = 5252;
for (var i = 0; i < args.Length; i++)
{
    if (args[i] is "--url" or "-u" && i + 1 < args.Length) { serverUrl = args[i + 1].TrimEnd('/'); i++; }
    else if (args[i] is "--local" or "-l" && i + 1 < args.Length) { localPort = int.Parse(args[i + 1]); i++; }
}

var wsUrl = serverUrl.Replace("https://", "wss://").Replace("http://", "ws://") + "/tunnel";
var localBase = $"http://127.0.0.1:{localPort}";

Console.WriteLine($"Tunnel client: {wsUrl} -> {localBase}");
Console.WriteLine("Connecting...");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

while (!cts.Token.IsCancellationRequested)
{
    try
    {
        using var ws = new ClientWebSocket();
        await ws.ConnectAsync(new Uri(wsUrl), cts.Token).ConfigureAwait(false);
        Console.WriteLine("Connected. Forwarding requests to localhost:{0}...", localPort);

        var buffer = new byte[1024 * 256];
        var segment = new ArraySegment<byte>(buffer);
        var sendSem = new SemaphoreSlim(1, 1);
        while (ws.State == WebSocketState.Open && !cts.Token.IsCancellationRequested)
        {
            var result = await ws.ReceiveAsync(segment, cts.Token).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close) break;
            if (result.MessageType != WebSocketMessageType.Text || result.Count == 0) continue;

            var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
            TunnelRequest? req;
            try { req = JsonSerializer.Deserialize<TunnelRequest>(json, JsonOptions); } catch { continue; }
            if (req?.Id == null) continue;

            _ = Task.Run(async () =>
            {
                try
                {
                    var response = await ForwardToLocalAsync(req, localBase).ConfigureAwait(false);
                    var responseJson = JsonSerializer.Serialize(response, JsonOptions);
                    await sendSem.WaitAsync(cts.Token).ConfigureAwait(false);
                    try { await ws.SendAsync(Encoding.UTF8.GetBytes(responseJson), WebSocketMessageType.Text, true, CancellationToken.None).ConfigureAwait(false); }
                    finally { sendSem.Release(); }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Error: " + ex.Message);
                    try
                    {
                        var err = new TunnelResponse(req.Id, 502, new Dictionary<string, string> { ["Content-Type"] = "text/plain" }, Convert.ToBase64String(Encoding.UTF8.GetBytes("Bad Gateway: " + ex.Message)));
                        await sendSem.WaitAsync(cts.Token).ConfigureAwait(false);
                        try { await ws.SendAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(err, JsonOptions)), WebSocketMessageType.Text, true, CancellationToken.None).ConfigureAwait(false); }
                        finally { sendSem.Release(); }
                    }
                    catch { }
                }
            }, cts.Token);
        }
    }
    catch (OperationCanceledException) { break; }
    catch (Exception ex)
    {
        var inner = ex.InnerException != null ? $" -> {ex.InnerException.Message}" : "";
        var inner2 = ex.InnerException?.InnerException != null ? $" -> {ex.InnerException.InnerException.Message}" : "";
        Console.WriteLine($"Disconnected: {ex.Message}{inner}{inner2}");
    }

    if (cts.Token.IsCancellationRequested) break;
    Console.WriteLine("Reconnecting in 5s...");
    await Task.Delay(5000, cts.Token).ConfigureAwait(false);
}

Console.WriteLine("Bye.");

async Task<TunnelResponse> ForwardToLocalAsync(TunnelRequest req, string localBase)
{
    var path = req.Path ?? "/";
    if (path.StartsWith("?", StringComparison.Ordinal)) path = "/" + path;
    else if (string.IsNullOrEmpty(path) || path[0] != '/') path = "/" + path;
    var url = localBase + path;
    using var hc = new HttpClient();
    var request = new HttpRequestMessage(new HttpMethod(req.Method ?? "GET"), url);
    if (req.Headers != null)
        foreach (var (k, v) in req.Headers)
            if (!string.Equals(k, "Host", StringComparison.OrdinalIgnoreCase))
                request.Headers.TryAddWithoutValidation(k, v);
    if (req.BodyBase64 != null)
    {
        request.Content = new ByteArrayContent(Convert.FromBase64String(req.BodyBase64));
        if (req.Headers != null && req.Headers.TryGetValue("Content-Type", out var ct) && !string.IsNullOrEmpty(ct))
            request.Content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(ct);
    }
    var resp = await hc.SendAsync(request).ConfigureAwait(false);
    byte[]? body = null;
    var stream = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false);
    using (var ms = new MemoryStream())
    {
        await stream.CopyToAsync(ms).ConfigureAwait(false);
        body = ms.ToArray();
    }
    var headers = new Dictionary<string, string>();
    foreach (var h in resp.Headers)
        headers[h.Key] = string.Join(", ", h.Value);
    if (resp.Content.Headers.ContentType != null)
                headers["Content-Type"] = resp.Content.Headers.ContentType.ToString();
    return new TunnelResponse(req.Id, (int)resp.StatusCode, headers, body != null ? Convert.ToBase64String(body) : null);
}

file record TunnelRequest(string? Id, string? Method, string? Path, Dictionary<string, string>? Headers, string? BodyBase64);
file record TunnelResponse(string? Id, int StatusCode, Dictionary<string, string>? Headers, string? BodyBase64);
