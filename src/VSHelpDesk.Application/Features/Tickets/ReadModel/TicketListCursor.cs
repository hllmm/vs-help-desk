namespace VSHelpDesk.Application.Features.Tickets.ReadModel;

public sealed record TicketListCursor(DateTime LastActivityAt, string TicketNumber);
