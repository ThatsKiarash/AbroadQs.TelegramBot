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
        services.AddScoped<IUpdateHandler, KycStateHandler>();         // Before DynamicStageHandler (state-driven)
        services.AddScoped<ExchangeStateHandler>();                    // Concrete registration for DI into DynamicStageHandler
        services.AddScoped<IUpdateHandler, ExchangeStateHandler>(sp => sp.GetRequiredService<ExchangeStateHandler>());
        services.AddScoped<BidStateHandler>();                          // Concrete registration for DI into StartHandler
        services.AddScoped<IUpdateHandler, BidStateHandler>(sp => sp.GetRequiredService<BidStateHandler>());
        services.AddScoped<GroupStateHandler>();                       // Concrete registration for DI into DynamicStageHandler
        services.AddScoped<IUpdateHandler, GroupStateHandler>(sp => sp.GetRequiredService<GroupStateHandler>());
        services.AddScoped<IUpdateHandler, ProfileStateHandler>();   // Before DynamicStageHandler (state-driven)
        services.AddScoped<IUpdateHandler, DynamicStageHandler>();
        services.AddScoped<IUpdateHandler, UnknownCommandHandler>();
        return services;
    }
}
