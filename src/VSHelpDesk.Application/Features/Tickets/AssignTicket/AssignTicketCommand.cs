namespace VSHelpDesk.Application.Features.Tickets.AssignTicket;

public sealed record AssignTicketCommand(Guid TicketId, Guid? UserId);
