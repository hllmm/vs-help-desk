namespace VSHelpDesk.Application.Features.Tickets.CreatePortalTicket;

public sealed record CreatePortalTicketCommand(
    string IdempotencyKey,
    string Subject,
    string CustomerName,
    string CustomerEmail,
    string Content);
