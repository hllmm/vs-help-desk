using Microsoft.Extensions.Logging;
using VSHelpDesk.Application.Abstractions.Authentication;
using VSHelpDesk.Application.Abstractions.Persistence.Repositories;
using VSHelpDesk.Application.Common.Exceptions;
using VSHelpDesk.Application.Common.Localization;

namespace VSHelpDesk.Application.Features.Tickets.ResolveTicket;

public sealed class ResolveTicketHandler(
    ITicketRepository ticketRepository,
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    TimeProvider timeProvider,
    ILogger<ResolveTicketHandler> logger,
    IMessageProvider? messages = null)
{
    private readonly IMessageProvider _messages = messages ?? FallbackMessageProvider.Instance;

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

        var ticket = await ticketRepository.GetByIdAsync(command.TicketId, cancellationToken: cancellationToken);
        if (ticket is null)
        {
            throw new NotFoundException(_messages.Get(MessageKeys.Tickets.NotFound, command.TicketId));
        }

        var oldStatus = ticket.Status;
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var changed = ticket.ResolveManually(now, userId);

        if (changed)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
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
