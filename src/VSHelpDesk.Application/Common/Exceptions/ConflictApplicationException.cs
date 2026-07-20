namespace VSHelpDesk.Application.Common.Exceptions;

public abstract class ConflictApplicationException(
    string message,
    Exception? innerException = null)
    : Exception(message, innerException)
{
}
