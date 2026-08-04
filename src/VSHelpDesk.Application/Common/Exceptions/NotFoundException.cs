
using VSHelpDesk.Application.Common.Localization;

namespace VSHelpDesk.Application.Common.Exceptions;

public sealed class NotFoundException : Exception
{
    public NotFoundException(string message)
        : base(message)
    {
    }

    public NotFoundException(string entityName, object key)
        : base(LocalizedApplicationMessages.NotFound(entityName, key))
    {
    }
}
