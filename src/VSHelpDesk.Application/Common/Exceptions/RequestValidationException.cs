namespace VSHelpDesk.Application.Common.Exceptions;

public sealed class RequestValidationException(string code) : Exception(code)
{
    public string Code { get; } = code;
}
