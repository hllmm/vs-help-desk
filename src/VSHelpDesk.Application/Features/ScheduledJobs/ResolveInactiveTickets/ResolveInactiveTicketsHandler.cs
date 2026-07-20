using Microsoft.Extensions.Logging;
using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Application.Common.Exceptions;
using VSHelpDesk.Domain.Enums;

namespace VSHelpDesk.Application.Features.ScheduledJobs.ResolveInactiveTickets;

/// <summary>
/// UC-008 / BR-008 job orchestrator: lease → select inclusive due candidates → per-ticket scope.
/// </summary>
public sealed class ResolveInactiveTicketsHandler(
    IApplicationDbContext applicationDbContext,
    IInactiveTicketResolverFactory resolverFactory,
    IResolveInactiveTicketsGate gate,
    TimeProvider timeProvider,
    ILogger<ResolveInactiveTicketsHandler> logger)
{
    public async Task<ResolveInactiveTicketsResult> HandleAsync(
        ResolveInactiveTicketsCommand command,
        CancellationToken cancellationToken)
    {
        _ = command;

        await using var lease =
            await gate.TryAcquireAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new JobAlreadyRunningException("resolve-inactive-tickets");

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var cutoffUtc = nowUtc - ResolveInactiveTicketsPolicy.InactivityThreshold;

        logger.LogInformation(
            "ResolveInactiveTickets started cutoffUtc={CutoffUtc}",
            cutoffUtc);

        var candidateIds = applicationDbContext.Tickets
            .Where(ticket =>
                ticket.Status == TicketStatus.WaitingCustomerReply
                && ticket.WaitingCustomerSince != null
                && ticket.WaitingCustomerSince <= cutoffUtc)
            .OrderBy(ticket => ticket.WaitingCustomerSince)
            .ThenBy(ticket => ticket.Id)
            .Select(ticket => ticket.Id)
            .ToList();

        var resolved = 0;
        var skipped = 0;
        var conflicted = 0;
        var failed = 0;

        foreach (var ticketId in candidateIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var outcome = await resolverFactory.ResolveAsync(
                    ticketId,
                    cutoffUtc,
                    nowUtc,
                    cancellationToken).ConfigureAwait(false);

                switch (outcome)
                {
                    case InactiveTicketResolutionOutcome.Resolved:
                        resolved++;
                        break;
                    case InactiveTicketResolutionOutcome.Skipped:
                        skipped++;
                        break;
                    case InactiveTicketResolutionOutcome.Conflicted:
                        conflicted++;
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                failed++;
                logger.LogError(
                    ex,
                    "ResolveInactiveTickets candidate failed ticketId={TicketId}",
                    ticketId);
            }
        }

        logger.LogInformation(
            "ResolveInactiveTickets finished cutoffUtc={CutoffUtc} candidates={Candidates} resolved={Resolved} skipped={Skipped} conflicted={Conflicted} failed={Failed}",
            cutoffUtc,
            candidateIds.Count,
            resolved,
            skipped,
            conflicted,
            failed);

        return new ResolveInactiveTicketsResult(
            cutoffUtc,
            candidateIds.Count,
            resolved,
            skipped,
            conflicted,
            failed);
    }
}
