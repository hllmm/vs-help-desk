namespace VSHelpDesk.Application.Common.Exceptions;

public sealed class NotFoundException : Exception
{
    public NotFoundException(string message)
        : base(message)
    {
    }

    public NotFoundException(string entityName, object key)
        : base(ApplicationMessages.Common.NotFoundTemplate(entityName, key))
    {
    }
}
