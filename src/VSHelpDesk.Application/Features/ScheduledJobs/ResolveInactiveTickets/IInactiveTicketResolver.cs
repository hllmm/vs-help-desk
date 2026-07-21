namespace VSHelpDesk.Application.Features.ScheduledJobs.ResolveInactiveTickets;

public interface IInactiveTicketResolver
{
    Task<InactiveTicketResolutionOutcome> ResolveAsync(
        Guid ticketId,
        DateTime cutoffUtc,
        DateTime nowUtc,
        CancellationToken cancellationToken);
}
