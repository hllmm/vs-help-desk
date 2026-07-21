namespace VSHelpDesk.Application.Features.Tickets.ReplyToTicket;

/// <summary>UC-006 / UC-009 — customer email reply or reopen on an existing ticket.</summary>
public sealed record AppendCustomerReplyCommand(
    string IdempotencyKey,
    string? SourceMessageId,
    string TicketNumber,
    string Content,
    string? FromAddress = null);
