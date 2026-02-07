using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

var backendUrl = builder.Configuration["Tunnel:BackendUrl"]?.TrimEnd('/') ?? "http://localhost:5252";

// یک کلاینت تانل متصل (اختیاری)
var tunnelConnection = new TunnelConnectionHolder();
var PendingResponses = new ConcurrentDictionary<string, TaskCompletionSource<TunnelResponse>>();
var JsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

var app = builder.Build();

app.UseWebSockets();

// WebSocket endpoint برای کلاینت تانل (مثل ngrok client)
app.Map("/tunnel", async (HttpContext ctx) =>
{
    if (!ctx.WebSockets.IsWebSocketRequest)
    {
        ctx.Response.StatusCode = 400;
        return;
    }
    var ws = await ctx.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);
    tunnelConnection.Set(ws);
    try
    {
        await ReceiveLoopAsync(ws).ConfigureAwait(false);
    }
    finally
    {
        tunnelConnection.Clear();
    }
});

// همهٔ درخواست‌های دیگر: یا به تانل کلاینت یا به بک‌اند (مسیرهای / و /health را به endpointهای پایین بفرست)
app.Use(async (HttpContext ctx, RequestDelegate next) =>
{
    if (ctx.Request.Path.StartsWithSegments("/tunnel") || ctx.Request.Path == "/" || ctx.Request.Path == "/health")
    {
        await next(ctx).ConfigureAwait(false);
        return;
    }

    var ws = tunnelConnection.Get();
    if (ws != null && ws.State == System.Net.WebSockets.WebSocketState.Open)
    {
        await ForwardToTunnelClientAsync(ctx, ws).ConfigureAwait(false);
    }
    else
    {
        ctx.Response.StatusCode = 503;
        ctx.Response.Headers["Retry-After"] = "5";
        ctx.Response.ContentType = "text/plain; charset=utf-8";
        await ctx.Response.WriteAsync("Tunnel client disconnected. Start Tunnel Client (local) so requests reach your machine. Retry after connecting.").ConfigureAwait(false);
    }
});

app.MapGet("/", () => Results.Ok(new
{
    service = "AbroadQs Tunnel Server",
    backend = backendUrl,
    tunnelConnected = tunnelConnection.Get()?.State == System.Net.WebSockets.WebSocketState.Open,
    health = "ok"
}));

app.MapGet("/health", () => Results.Ok("ok"));

app.Run();

async Task ReceiveLoopAsync(System.Net.WebSockets.WebSocket ws)
{
    var buffer = new byte[1024 * 64];
    var segment = new ArraySegment<byte>(buffer);
    while (ws.State == System.Net.WebSockets.WebSocketState.Open)
    {
        var result = await ws.ReceiveAsync(segment, CancellationToken.None).ConfigureAwait(false);
        if (result.MessageType == System.Net.WebSockets.WebSocketMessageType.Close)
            break;
        if (result.MessageType == System.Net.WebSockets.WebSocketMessageType.Text && result.Count > 0)
        {
            var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
            OnTunnelResponse(json);
        }
    }
}

async Task ForwardToTunnelClientAsync(HttpContext ctx, System.Net.WebSockets.WebSocket ws)
{
    var id = Guid.NewGuid().ToString("N");
    byte[]? body = null;
    if (ctx.Request.ContentLength is > 0)
    {
        using var ms = new MemoryStream();
        await ctx.Request.Body.CopyToAsync(ms).ConfigureAwait(false);
        body = ms.ToArray();
    }
    var req = new TunnelRequest(
        id,
        ctx.Request.Method,
        ctx.Request.Path + ctx.Request.QueryString,
        ctx.Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString()),
        body != null ? Convert.ToBase64String(body) : null);
    var json = JsonSerializer.Serialize(req);
    await ws.SendAsync(Encoding.UTF8.GetBytes(json), System.Net.WebSockets.WebSocketMessageType.Text, true, CancellationToken.None).ConfigureAwait(false);

    var tcs = new TaskCompletionSource<TunnelResponse>();
    PendingResponses.TryAdd(id, tcs);
    try
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        using (cts.Token.Register(() => tcs.TrySetCanceled()))
        {
            TunnelResponse response;
            try
            {
                response = await tcs.Task.WaitAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                ctx.Response.StatusCode = 504;
                await ctx.Response.WriteAsync("Tunnel client timeout.").ConfigureAwait(false);
                return;
            }
            ctx.Response.StatusCode = response.StatusCode;
            foreach (var (k, v) in response.Headers ?? [])
                ctx.Response.Headers[k] = v;
            if (response.BodyBase64 != null)
            {
                var bytes = Convert.FromBase64String(response.BodyBase64);
                await ctx.Response.Body.WriteAsync(bytes).ConfigureAwait(false);
            }
        }
    }
    finally
    {
        PendingResponses.TryRemove(id, out _);
    }
}

void OnTunnelResponse(string json)
{
    var response = JsonSerializer.Deserialize<TunnelResponse>(json, JsonOptions);
    if (response?.Id != null && PendingResponses.TryRemove(response.Id, out var tcs))
        tcs.TrySetResult(response);
}

async Task ProxyToBackendAsync(HttpContext ctx, string backendUrl)
{
    var target = new Uri(backendUrl + ctx.Request.Path + ctx.Request.QueryString);
    using var client = new HttpClient();
    var req = new HttpRequestMessage(new HttpMethod(ctx.Request.Method), target);
    foreach (var h in ctx.Request.Headers)
    {
        if (h.Key.Equals("Host", StringComparison.OrdinalIgnoreCase)) continue;
        req.Headers.TryAddWithoutValidation(h.Key, h.Value.ToArray());
    }
    if (ctx.Request.ContentLength is > 0)
    {
        req.Content = new StreamContent(ctx.Request.Body);
        if (ctx.Request.ContentType != null)
            req.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(ctx.Request.ContentType);
    }
    var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ctx.RequestAborted).ConfigureAwait(false);
    ctx.Response.StatusCode = (int)resp.StatusCode;
    foreach (var h in resp.Headers)
        ctx.Response.Headers[h.Key] = h.Value.ToArray();
    if (resp.Content.Headers.ContentType != null)
        ctx.Response.Headers.ContentType = resp.Content.Headers.ContentType.ToString();
    await resp.Content.CopyToAsync(ctx.Response.Body).ConfigureAwait(false);
}

file sealed class TunnelConnectionHolder
{
    private System.Net.WebSockets.WebSocket? _ws;
    public void Set(System.Net.WebSockets.WebSocket ws) => Interlocked.Exchange(ref _ws, ws);
    public System.Net.WebSockets.WebSocket? Get() => _ws;
    public void Clear() => Interlocked.Exchange(ref _ws, null);
}

file record TunnelRequest(string Id, string Method, string Path, Dictionary<string, string>? Headers, string? BodyBase64);
file record TunnelResponse(string? Id, int StatusCode, Dictionary<string, string>? Headers, string? BodyBase64);
