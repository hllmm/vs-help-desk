using Microsoft.Extensions.DependencyInjection;
using VSHelpDesk.Application.Features.Authentication.Login;
using VSHelpDesk.Application.Features.Tickets.CreateTicket;

namespace VSHelpDesk.Application;

/// <summary>
/// Application layer registration. Handlers/validators are wired in Hafta 1+.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<LoginHandler>();
        services.AddScoped<CreateTicketHandler>();

        return services;
    }
}
