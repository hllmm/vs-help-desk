namespace VSHelpDesk.Application.Common.Exceptions;

public sealed class ResolvedTicketReplyException()
    : ConflictApplicationException(ApplicationMessages.Tickets.ResolvedTicketSupportReply);
