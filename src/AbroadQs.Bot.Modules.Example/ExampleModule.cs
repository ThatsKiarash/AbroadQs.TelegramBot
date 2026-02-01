using AbroadQs.Bot.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace AbroadQs.Bot.Modules.Example;

/// <summary>
/// Example module showing how to add a new feature (echo command).
/// Copy this pattern for new modules (Ads, Notifications, etc.).
/// </summary>
public sealed class ExampleModule : IModuleMarker
{
    public string ModuleName => "Example";
}

public static class ExampleModuleExtensions
{
    public static IServiceCollection AddExampleModule(this IServiceCollection services)
    {
        services.AddSingleton<IUpdateHandler, EchoHandler>();
        return services;
    }
}
