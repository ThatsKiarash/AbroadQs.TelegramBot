using AbroadQs.Bot.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace AbroadQs.Bot.Modules.Common;

/// <summary>
/// Marker for the Common module (start, help, etc.).
/// </summary>
public sealed class CommonModule : IModuleMarker
{
    public string ModuleName => "Common";
}

public static class CommonModuleExtensions
{
    public static IServiceCollection AddCommonModule(this IServiceCollection services)
    {
        services.AddSingleton<IUpdateHandler, StartHandler>();
        services.AddSingleton<IUpdateHandler, HelpHandler>();
        services.AddSingleton<IUpdateHandler, UnknownCommandHandler>();
        return services;
    }
}
