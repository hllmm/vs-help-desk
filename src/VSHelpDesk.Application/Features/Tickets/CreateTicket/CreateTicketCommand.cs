namespace VSHelpDesk.Application.Features.Tickets.CreateTicket;

/// <summary>
/// Creates a new ticket from an inbound customer message (UC-002 entry).
/// </summary>
public sealed record CreateTicketCommand(
    string MessageId,
    string Subject,
    string CustomerName,
    string CustomerEmail,
    string Content,
    bool IsHtml = false);
