using VSHelpDesk.Application.Abstractions.Persistence.Repositories;
using VSHelpDesk.Application.Common.Exceptions;

namespace VSHelpDesk.Application.Features.ScheduledJobs.ResolveInactiveTickets;

/// <summary>
/// Per-candidate automatic resolution: load → eligibility recheck → ResolveAutomatically → one retry.
/// </summary>
public sealed class InactiveTicketResolver(
    ITicketRepository ticketRepository,
    IUnitOfWork unitOfWork)
    : IInactiveTicketResolver
{
    public async Task<InactiveTicketResolutionOutcome> ResolveAsync(
        Guid ticketId,
        DateTime cutoffUtc,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var ticket = await ticketRepository.GetByIdAsync(ticketId, cancellationToken: cancellationToken);
        if (ticket is null || !ResolveInactiveTicketsPolicy.IsEligible(ticket, cutoffUtc))
        {
            return InactiveTicketResolutionOutcome.Skipped;
        }

        try
        {
            ticket.ResolveAutomatically(nowUtc);
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return InactiveTicketResolutionOutcome.Resolved;
        }
        catch (OptimisticConcurrencyException)
        {
            unitOfWork.ClearTrackedChanges();
        }

        var reloaded = await ticketRepository.GetByIdAsync(ticketId, cancellationToken: cancellationToken);
        if (reloaded is null || !ResolveInactiveTicketsPolicy.IsEligible(reloaded, cutoffUtc))
        {
            return InactiveTicketResolutionOutcome.Skipped;
        }

        try
        {
            reloaded.ResolveAutomatically(nowUtc);
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return InactiveTicketResolutionOutcome.Resolved;
        }
        catch (OptimisticConcurrencyException)
        {
            unitOfWork.ClearTrackedChanges();
            return InactiveTicketResolutionOutcome.Conflicted;
        }
    }
}
