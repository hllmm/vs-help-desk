namespace VSHelpDesk.Application.Common.Exceptions;

public sealed class ResolvedTicketReplyException()
    : ConflictApplicationException(
        "A resolved ticket cannot receive a support reply.");
