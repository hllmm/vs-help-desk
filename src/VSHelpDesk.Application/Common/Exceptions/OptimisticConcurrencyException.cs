namespace VSHelpDesk.Application.Common.Exceptions;

public sealed class OptimisticConcurrencyException(
    string message,
    Exception? innerException = null)
    : ConflictApplicationException(message, innerException)
{
}
