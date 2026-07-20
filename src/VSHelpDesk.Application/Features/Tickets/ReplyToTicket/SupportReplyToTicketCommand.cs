namespace VSHelpDesk.Application.Features.Tickets.ReplyToTicket;

/// <summary>UC-005 — support portal reply to customer.</summary>
public sealed record SupportReplyToTicketCommand(
    Guid TicketId,
    string Content,
    bool IsHtml = false);
