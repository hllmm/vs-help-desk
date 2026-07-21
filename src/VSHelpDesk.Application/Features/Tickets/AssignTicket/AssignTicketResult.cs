namespace VSHelpDesk.Application.Features.Tickets.AssignTicket;

public sealed record AssignTicketResult(
    Guid TicketId,
    Guid? AssignedUserId,
    DateTime UpdatedAt,
    bool Changed);
