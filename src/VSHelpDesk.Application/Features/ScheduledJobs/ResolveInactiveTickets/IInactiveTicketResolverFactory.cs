namespace VSHelpDesk.Application.Features.ScheduledJobs.ResolveInactiveTickets;

public interface IInactiveTicketResolverFactory
{
    Task<InactiveTicketResolutionOutcome> ResolveAsync(
        Guid ticketId,
        DateTime cutoffUtc,
        DateTime nowUtc,
        CancellationToken cancellationToken);
}
