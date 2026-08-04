using Microsoft.Extensions.Logging;
using VSHelpDesk.Application.Abstractions.Authentication;
using VSHelpDesk.Application.Abstractions.Persistence.Repositories;
using VSHelpDesk.Application.Common;
using VSHelpDesk.Application.Common.Exceptions;
using VSHelpDesk.Domain.Exceptions;

namespace VSHelpDesk.Application.Features.Tickets.AssignTicket;

public sealed class AssignTicketHandler(
    ITicketRepository ticketRepository,
    IUserRepository userRepository,
    IUnitOfWork unitOfWork,
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

        var ticket = await ticketRepository.GetByIdAsync(command.TicketId, cancellationToken: cancellationToken);
        if (ticket is null)
        {
            throw new NotFoundException(ApplicationMessages.Tickets.NotFound(command.TicketId));
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

            var targetUser = await userRepository.GetByIdAsync(targetUserId, cancellationToken);
            var targetIsActive = targetUser is not null && targetUser.IsActive;
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
            await unitOfWork.SaveChangesAsync(cancellationToken);
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
