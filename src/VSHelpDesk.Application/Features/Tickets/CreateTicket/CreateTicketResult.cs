namespace VSHelpDesk.Application.Features.Tickets.CreateTicket;

public sealed record CreateTicketResult(
    Guid TicketId,
    string TicketNumber,
    Guid FirstTicketMessageId,
    bool WasAlreadyProcessed);
