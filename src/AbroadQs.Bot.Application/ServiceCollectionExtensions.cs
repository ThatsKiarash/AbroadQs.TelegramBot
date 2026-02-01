using AbroadQs.Bot.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace AbroadQs.Bot.Application;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the update dispatcher and all IUpdateHandler from the same assembly and referenced modules.
    /// Call after registering handlers (e.g. from Modules).
    /// </summary>
    public static IServiceCollection AddBotApplication(this IServiceCollection services)
    {
        services.AddSingleton<UpdateDispatcher>();
        return services;
    }

    /// <summary>
    /// Registers a single handler. Modules should call this for each of their handlers.
    /// </summary>
    public static IServiceCollection AddUpdateHandler<THandler>(this IServiceCollection services)
        where THandler : class, IUpdateHandler
    {
        services.AddSingleton<IUpdateHandler, THandler>();
        return services;
    }
}
