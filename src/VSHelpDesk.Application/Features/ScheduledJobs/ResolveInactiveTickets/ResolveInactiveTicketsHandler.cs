using Microsoft.Extensions.Logging;
using VSHelpDesk.Application.Abstractions.Parameters;
using VSHelpDesk.Application.Abstractions.Persistence.Repositories;
using VSHelpDesk.Application.Common.Exceptions;
using VSHelpDesk.Application.Features.Parameters;
using VSHelpDesk.Domain.Enums;

namespace VSHelpDesk.Application.Features.ScheduledJobs.ResolveInactiveTickets;

/// <summary>
/// UC-008 / BR-008 job orchestrator: lease → select inclusive due candidates → per-ticket scope.
/// Cutoff days from <c>AutoResolve.InactiveDays</c> (default 3).
/// </summary>
public sealed class ResolveInactiveTicketsHandler(
    ITicketRepository ticketRepository,
    IInactiveTicketResolverFactory resolverFactory,
    IResolveInactiveTicketsGate gate,
    IApplicationParameterReader parameterReader,
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
        var days = await parameterReader
            .GetIntAsync(
                ApplicationParameterCatalog.AutoResolveInactiveDaysKey,
                ResolveInactiveTicketsPolicy.DefaultInactivityDays,
                cancellationToken)
            .ConfigureAwait(false);
        if (days < 1)
        {
            days = ResolveInactiveTicketsPolicy.DefaultInactivityDays;
        }

        var cutoffUtc = nowUtc - TimeSpan.FromDays(days);

        logger.LogInformation(
            "ResolveInactiveTickets started cutoffUtc={CutoffUtc}",
            cutoffUtc);

        var candidateIds = ticketRepository.GetListQueryable()
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
