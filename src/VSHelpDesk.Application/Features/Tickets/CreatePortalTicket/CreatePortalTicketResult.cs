namespace VSHelpDesk.Application.Features.Tickets.CreatePortalTicket;

public sealed record CreatePortalTicketResult(
    Guid TicketId,
    string TicketNumber,
    Guid FirstTicketMessageId,
    bool WasAlreadyProcessed);
