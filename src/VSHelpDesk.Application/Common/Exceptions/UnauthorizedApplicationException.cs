namespace VSHelpDesk.Application.Common.Exceptions;

public sealed class UnauthorizedApplicationException : Exception
{
    public UnauthorizedApplicationException(string message = "Yetkisiz erişim.")
        : base(message)
    {
    }
}
