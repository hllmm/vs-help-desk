
using VSHelpDesk.Application.Common.Localization;

namespace VSHelpDesk.Application.Common.Exceptions;

public sealed class UnauthorizedApplicationException : Exception
{
    public UnauthorizedApplicationException(string? message = null)
        : base(message ?? LocalizedApplicationMessages.Unauthorized)
    {
    }
}
