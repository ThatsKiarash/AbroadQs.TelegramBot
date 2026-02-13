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
        services.AddScoped<KycStateHandler>();                          // Concrete registration for DI into DynamicStageHandler
        services.AddScoped<IUpdateHandler, KycStateHandler>(sp => sp.GetRequiredService<KycStateHandler>());
        services.AddScoped<ExchangeStateHandler>();                    // Concrete registration for DI into DynamicStageHandler
        services.AddScoped<IUpdateHandler, ExchangeStateHandler>(sp => sp.GetRequiredService<ExchangeStateHandler>());
        services.AddScoped<BidStateHandler>();                          // Concrete registration for DI into StartHandler
        services.AddScoped<IUpdateHandler, BidStateHandler>(sp => sp.GetRequiredService<BidStateHandler>());
        services.AddScoped<GroupStateHandler>();                       // Concrete registration for DI into DynamicStageHandler
        services.AddScoped<IUpdateHandler, GroupStateHandler>(sp => sp.GetRequiredService<GroupStateHandler>());
        services.AddScoped<ProfileStateHandler>();                     // Concrete registration for DI into DynamicStageHandler
        services.AddScoped<IUpdateHandler, ProfileStateHandler>(sp => sp.GetRequiredService<ProfileStateHandler>());
        services.AddScoped<FinanceHandler>();                         // Phase 2: Financial module
        services.AddScoped<IUpdateHandler, FinanceHandler>(sp => sp.GetRequiredService<FinanceHandler>());
        services.AddScoped<TicketHandler>();                          // Phase 4: Support tickets
        services.AddScoped<IUpdateHandler, TicketHandler>(sp => sp.GetRequiredService<TicketHandler>());
        services.AddScoped<StudentProjectHandler>();                  // Phase 5: Student projects
        services.AddScoped<IUpdateHandler, StudentProjectHandler>(sp => sp.GetRequiredService<StudentProjectHandler>());
        services.AddScoped<InternationalQuestionHandler>();           // Phase 6: International questions
        services.AddScoped<IUpdateHandler, InternationalQuestionHandler>(sp => sp.GetRequiredService<InternationalQuestionHandler>());
        services.AddScoped<SponsorshipHandler>();                    // Phase 7: Sponsorship
        services.AddScoped<IUpdateHandler, SponsorshipHandler>(sp => sp.GetRequiredService<SponsorshipHandler>());
        services.AddScoped<CurrencyPurchaseHandler>();               // Phase 8: Currency purchase
        services.AddScoped<IUpdateHandler, CurrencyPurchaseHandler>(sp => sp.GetRequiredService<CurrencyPurchaseHandler>());
        services.AddScoped<MyMessagesHandler>();                     // Phase 4: My Messages
        services.AddScoped<IUpdateHandler, MyMessagesHandler>(sp => sp.GetRequiredService<MyMessagesHandler>());
        services.AddScoped<MyProposalsHandler>();                    // Phase 4: My Proposals
        services.AddScoped<IUpdateHandler, MyProposalsHandler>(sp => sp.GetRequiredService<MyProposalsHandler>());
        services.AddScoped<IUpdateHandler, DynamicStageHandler>();
        services.AddScoped<IUpdateHandler, UnknownCommandHandler>();
        return services;
    }
}
