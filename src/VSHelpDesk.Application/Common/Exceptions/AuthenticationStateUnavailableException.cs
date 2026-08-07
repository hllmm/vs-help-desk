namespace VSHelpDesk.Application.Common.Exceptions;

public sealed class AuthenticationStateUnavailableException(Exception? innerException = null)
    : Exception("Authentication state is temporarily unavailable.", innerException)
{
}
