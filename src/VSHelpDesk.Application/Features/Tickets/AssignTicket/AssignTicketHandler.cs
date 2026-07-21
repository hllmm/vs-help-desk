using Microsoft.Extensions.Logging;
using VSHelpDesk.Application.Abstractions.Authentication;
using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Application.Common.Exceptions;
using VSHelpDesk.Domain.Exceptions;

namespace VSHelpDesk.Application.Features.Tickets.AssignTicket;

public sealed class AssignTicketHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService,
    TimeProvider timeProvider,
    ILogger<AssignTicketHandler> logger)
{
    public async Task<AssignTicketResult> HandleAsync(
        AssignTicketCommand command,
        CancellationToken cancellationToken)
    {
        if (!currentUserService.IsAuthenticated
            || currentUserService.UserId is not Guid actorUserId
            || actorUserId == Guid.Empty)
        {
            throw new UnauthorizedApplicationException();
        }

        var ticket = applicationDbContext.Tickets
            .FirstOrDefault(candidate => candidate.Id == command.TicketId);
        if (ticket is null)
        {
            throw new NotFoundException($"Ticket '{command.TicketId}' was not found.");
        }

        var oldAssigneeUserId = ticket.AssignedUserId;
        var now = timeProvider.GetUtcNow().UtcDateTime;
        bool changed;

        if (command.UserId is Guid targetUserId)
        {
            if (targetUserId == Guid.Empty)
            {
                throw new DomainException(AssignTicketCodes.AssigneeRequired);
            }

            var targetIsActive = applicationDbContext.Users
                .Any(user => user.Id == targetUserId && user.IsActive);
            if (!targetIsActive)
            {
                throw new DomainException(AssignTicketCodes.AssigneeNotAvailable);
            }

            changed = ticket.Assign(targetUserId, now);
        }
        else
        {
            changed = ticket.Unassign(now);
        }

        if (changed)
        {
            await applicationDbContext.SaveChangesAsync(cancellationToken);
        }

        logger.LogInformation(
            "Ticket assignment updated ticketId={TicketId} oldAssigneeUserId={OldAssigneeUserId} newAssigneeUserId={NewAssigneeUserId} actorUserId={ActorUserId} changed={Changed}",
            ticket.Id,
            oldAssigneeUserId,
            ticket.AssignedUserId,
            actorUserId,
            changed);

        return new AssignTicketResult(
            ticket.Id,
            ticket.AssignedUserId,
            ticket.UpdatedAt,
            changed);
    }
}
