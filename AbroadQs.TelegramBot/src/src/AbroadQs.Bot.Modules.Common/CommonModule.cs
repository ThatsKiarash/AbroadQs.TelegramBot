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
        services.AddScoped<IUpdateHandler, StartHandler>();
        services.AddScoped<IUpdateHandler, HelpHandler>();
        services.AddScoped<IUpdateHandler, DynamicStageHandler>();
        services.AddScoped<IUpdateHandler, ProfileStateHandler>();
        services.AddScoped<IUpdateHandler, UnknownCommandHandler>();
        return services;
    }
}
