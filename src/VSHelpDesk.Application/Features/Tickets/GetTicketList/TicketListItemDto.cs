namespace VSHelpDesk.Application.Features.Tickets.GetTicketList;

public sealed record TicketListItemDto(
    Guid Id,
    string TicketNumber,
    string Subject,
    string CustomerName,
    string CustomerEmail,
    string Status,
    DateTime LastActivityAt,
    Guid? AssignedUserId);
