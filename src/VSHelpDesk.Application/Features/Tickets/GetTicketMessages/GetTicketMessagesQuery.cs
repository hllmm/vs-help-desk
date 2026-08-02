namespace VSHelpDesk.Application.Features.Tickets.GetTicketMessages;

public sealed record GetTicketMessagesQuery(
    Guid TicketId,
    int PageSize = 100,
    string? Cursor = null);
