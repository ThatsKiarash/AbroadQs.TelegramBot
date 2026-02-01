using Microsoft.Extensions.Primitives;
using Yarp.ReverseProxy.Configuration;

var builder = WebApplication.CreateBuilder(args);

var backendUrl = builder.Configuration["ReverseProxy:BackendUrl"]?.TrimEnd('/')
    ?? "http://localhost:5252";

builder.Services.AddReverseProxy();
builder.Services.AddSingleton<IProxyConfigProvider>(sp =>
{
    var url = sp.GetRequiredService<IConfiguration>()["ReverseProxy:BackendUrl"]?.TrimEnd('/')
        ?? "http://localhost:5252";
    return new ProxyConfigProvider(url);
});

var app = builder.Build();

app.MapReverseProxy();

app.MapGet("/", () => Results.Ok(new
{
    service = "AbroadQs Reverse Proxy",
    backend = backendUrl,
    health = "ok"
}));

app.MapGet("/health", () => Results.Ok("ok"));

app.Run();

/// <summary>
/// وقتی در appsettings مسیر/کلاستر تعریف نشده، با این provider همهٔ درخواست‌ها به BackendUrl فرستاده می‌شوند.
/// </summary>
file sealed class ProxyConfigProvider : IProxyConfigProvider
{
    private readonly ProxyConfig _config;

    public ProxyConfigProvider(string backendUrl)
    {
        _config = new ProxyConfig(
            new[]
            {
                new RouteConfig
                {
                    RouteId = "default",
                    ClusterId = "backend",
                    Match = new RouteMatch { Path = "/{**catch-all}" }
                }
            },
            new[]
            {
                new ClusterConfig
                {
                    ClusterId = "backend",
                    Destinations = new Dictionary<string, DestinationConfig>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["default"] = new DestinationConfig { Address = backendUrl }
                    }
                }
            });
    }

    public IProxyConfig GetConfig() => _config;
}

file sealed class ProxyConfig : IProxyConfig
{
    public IReadOnlyList<RouteConfig> Routes { get; }
    public IReadOnlyList<ClusterConfig> Clusters { get; }
    public IChangeToken ChangeToken { get; } = new NullChangeToken();

    public ProxyConfig(IReadOnlyList<RouteConfig> routes, IReadOnlyList<ClusterConfig> clusters)
    {
        Routes = routes;
        Clusters = clusters;
    }
}

file sealed class NullChangeToken : IChangeToken
{
    public bool HasChanged => false;
    public bool ActiveChangeCallbacks => false;
    public IDisposable RegisterChangeCallback(Action<object?> callback, object? state) => EmptyDisposable.Instance;
}

file sealed class EmptyDisposable : IDisposable
{
    public static readonly EmptyDisposable Instance = new();
    public void Dispose() { }
}
