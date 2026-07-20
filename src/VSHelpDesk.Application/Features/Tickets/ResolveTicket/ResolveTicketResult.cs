namespace VSHelpDesk.Application.Features.Tickets.ResolveTicket;

public sealed record ResolveTicketResult(
    Guid TicketId,
    string TicketNumber,
    string Status,
    DateTime ResolvedAt,
    DateTime UpdatedAt,
    DateTime LastActivityAt,
    Guid? ClosedByUserId,
    bool Changed);
