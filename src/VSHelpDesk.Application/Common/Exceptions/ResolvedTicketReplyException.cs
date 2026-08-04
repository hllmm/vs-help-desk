
using VSHelpDesk.Application.Common.Localization;

namespace VSHelpDesk.Application.Common.Exceptions;

public sealed class ResolvedTicketReplyException : ConflictApplicationException
{
    public ResolvedTicketReplyException(string? message = null)
        : base(message ?? LocalizedApplicationMessages.ResolvedTicketReply)
    {
    }
}
