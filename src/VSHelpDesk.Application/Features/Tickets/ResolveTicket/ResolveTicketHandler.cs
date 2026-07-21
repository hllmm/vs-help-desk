using Microsoft.Extensions.Logging;
using VSHelpDesk.Application.Abstractions.Authentication;
using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Application.Common.Exceptions;

namespace VSHelpDesk.Application.Features.Tickets.ResolveTicket;

public sealed class ResolveTicketHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService,
    TimeProvider timeProvider,
    ILogger<ResolveTicketHandler> logger)
{
    public async Task<ResolveTicketResult> HandleAsync(
        ResolveTicketCommand command,
        CancellationToken cancellationToken)
    {
        if (!currentUserService.IsAuthenticated
            || currentUserService.UserId is not Guid userId
            || userId == Guid.Empty)
        {
            throw new UnauthorizedApplicationException();
        }

        var ticket = applicationDbContext.Tickets
            .FirstOrDefault(candidate => candidate.Id == command.TicketId);
        if (ticket is null)
        {
            throw new NotFoundException($"Ticket '{command.TicketId}' was not found.");
        }

        var oldStatus = ticket.Status;
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var changed = ticket.ResolveManually(now, userId);

        if (changed)
        {
            await applicationDbContext.SaveChangesAsync(cancellationToken);
        }

        logger.LogInformation(
            "Ticket resolved ticketId={TicketId} oldStatus={OldStatus} newStatus={NewStatus} userId={UserId} changed={Changed}",
            ticket.Id,
            oldStatus,
            ticket.Status,
            userId,
            changed);

        return new ResolveTicketResult(
            ticket.Id,
            ticket.TicketNumber,
            ticket.Status.ToString(),
            ticket.ResolvedAt!.Value,
            ticket.UpdatedAt,
            ticket.LastActivityAt,
            ticket.ClosedByUserId,
            changed);
    }
}
