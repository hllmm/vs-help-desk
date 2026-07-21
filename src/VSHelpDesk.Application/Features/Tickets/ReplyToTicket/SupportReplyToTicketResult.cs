namespace VSHelpDesk.Application.Features.Tickets.ReplyToTicket;

public sealed record SupportReplyToTicketResult(
    Guid TicketId,
    string TicketNumber,
    Guid MessageId,
    string Status,
    bool EmailDelivered,
    bool TicketStateUpdated,
    string? NoticeCode);
