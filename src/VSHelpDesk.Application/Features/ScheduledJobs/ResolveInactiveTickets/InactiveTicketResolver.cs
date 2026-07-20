using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Application.Common.Exceptions;

namespace VSHelpDesk.Application.Features.ScheduledJobs.ResolveInactiveTickets;

/// <summary>
/// Per-candidate automatic resolution: load → eligibility recheck → ResolveAutomatically → one retry.
/// </summary>
public sealed class InactiveTicketResolver(IApplicationDbContext applicationDbContext)
    : IInactiveTicketResolver
{
    public async Task<InactiveTicketResolutionOutcome> ResolveAsync(
        Guid ticketId,
        DateTime cutoffUtc,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var ticket = applicationDbContext.Tickets
            .FirstOrDefault(candidate => candidate.Id == ticketId);
        if (ticket is null || !ResolveInactiveTicketsPolicy.IsEligible(ticket, cutoffUtc))
        {
            return InactiveTicketResolutionOutcome.Skipped;
        }

        try
        {
            ticket.ResolveAutomatically(nowUtc);
            await applicationDbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return InactiveTicketResolutionOutcome.Resolved;
        }
        catch (OptimisticConcurrencyException)
        {
            applicationDbContext.ClearTrackedChanges();
        }

        var reloaded = applicationDbContext.Tickets
            .FirstOrDefault(candidate => candidate.Id == ticketId);
        if (reloaded is null || !ResolveInactiveTicketsPolicy.IsEligible(reloaded, cutoffUtc))
        {
            return InactiveTicketResolutionOutcome.Skipped;
        }

        try
        {
            reloaded.ResolveAutomatically(nowUtc);
            await applicationDbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return InactiveTicketResolutionOutcome.Resolved;
        }
        catch (OptimisticConcurrencyException)
        {
            applicationDbContext.ClearTrackedChanges();
            return InactiveTicketResolutionOutcome.Conflicted;
        }
    }
}
