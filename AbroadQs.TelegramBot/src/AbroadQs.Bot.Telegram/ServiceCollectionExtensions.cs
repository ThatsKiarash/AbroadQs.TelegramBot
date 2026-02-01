using Microsoft.Extensions.DependencyInjection;
using Telegram.Bot;

namespace AbroadQs.Bot.Telegram;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers Telegram bot client and response sender. Call from Host project.
    /// </summary>
    public static IServiceCollection AddTelegramBot(this IServiceCollection services, string botToken)
    {
        services.AddSingleton<ITelegramBotClient>(_ => new TelegramBotClient(botToken));
        // Scoped so it can receive IProcessingContext for tracking
        services.AddScoped<AbroadQs.Bot.Contracts.IResponseSender, TelegramResponseSender>();
        return services;
    }
}
