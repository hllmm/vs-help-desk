using Microsoft.Extensions.DependencyInjection;
using VSHelpDesk.Application.Features.Attachments.GetAttachment;
using VSHelpDesk.Application.Features.Attachments.UploadAttachment;
using VSHelpDesk.Application.Features.Authentication.Login;
using VSHelpDesk.Application.Features.MailProcessing.ProcessIncomingEmails;
using VSHelpDesk.Application.Features.Tickets.CreateTicket;
using VSHelpDesk.Application.Features.Tickets.GetTicketDetails;
using VSHelpDesk.Application.Features.Tickets.GetTicketList;
using VSHelpDesk.Application.Features.Tickets.ReplyToTicket;

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
        services.AddScoped<AppendCustomerReplyHandler>();
        services.AddScoped<SupportReplyToTicketHandler>();
        services.AddScoped<ProcessIncomingEmailsHandler>();
        services.AddScoped<GetTicketListHandler>();
        services.AddScoped<GetTicketDetailsHandler>();
        services.AddScoped<UploadAttachmentHandler>();
        services.AddScoped<GetAttachmentHandler>();

        return services;
    }
}
