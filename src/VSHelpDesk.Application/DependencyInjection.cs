using Microsoft.Extensions.DependencyInjection;
using VSHelpDesk.Application.Abstractions.Storage;
using VSHelpDesk.Application.Features.Attachments;
using VSHelpDesk.Application.Features.Attachments.GetAttachment;
using VSHelpDesk.Application.Features.Attachments.UploadAttachment;
using VSHelpDesk.Application.Features.Authentication.Login;
using VSHelpDesk.Application.Features.MailProcessing.Acknowledgements;
using VSHelpDesk.Application.Features.MailProcessing.ProcessIncomingEmails;
using VSHelpDesk.Application.Features.Parameters.GetParameters;
using VSHelpDesk.Application.Features.Parameters.UpdateParameter;
using VSHelpDesk.Application.Features.Tickets.CreateTicket;
using VSHelpDesk.Application.Features.Tickets.GetTicketDetails;
using VSHelpDesk.Application.Features.Tickets.GetTicketList;
using VSHelpDesk.Application.Features.Tickets.ReplyToTicket;
using VSHelpDesk.Application.Features.ScheduledJobs.ResolveInactiveTickets;
using VSHelpDesk.Application.Features.Tickets.ResolveTicket;

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
        services.AddScoped<ResolveTicketHandler>();
        services.AddScoped<AcknowledgementDispatcher>();
        services.AddScoped<ITicketAttachmentWriter, TicketAttachmentWriter>();
        services.AddScoped<IInboundEmailItemProcessor, InboundEmailItemProcessor>();
        services.AddScoped<ProcessIncomingEmailsHandler>();
        services.AddScoped<IInactiveTicketResolver, InactiveTicketResolver>();
        services.AddScoped<ResolveInactiveTicketsHandler>();
        services.AddScoped<GetTicketListHandler>();
        services.AddScoped<GetTicketDetailsHandler>();
        services.AddScoped<UploadAttachmentHandler>();
        services.AddScoped<GetAttachmentHandler>();
        services.AddScoped<GetParametersHandler>();
        services.AddScoped<UpdateParameterHandler>();

        return services;
    }
}
