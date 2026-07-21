using VSHelpDesk.Domain.Enums;

namespace VSHelpDesk.Application.Features.Tickets.ReplyToTicket;

public sealed record AppendCustomerReplyResult(
    Guid TicketId,
    string TicketNumber,
    Guid MessageId,
    TicketStatus StatusBefore,
    TicketStatus StatusAfter,
    bool WasAlreadyProcessed,
    bool WasReopened);
