namespace VSHelpDesk.Application.Features.Tickets.ReplyToTicket;

/// <summary>UC-006 / UC-009 — customer email reply or reopen on an existing ticket.</summary>
public sealed record AppendCustomerReplyCommand(
    string MessageId,
    string TicketNumber,
    string Content,
    bool IsHtml = false);
