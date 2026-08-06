using VSHelpDesk.Application.Abstractions.Correlation;

namespace VSHelpDesk.WebAPI.Services;

public sealed class HttpCorrelationIdProvider(IHttpContextAccessor httpContextAccessor) : ICorrelationIdProvider
{
    public string? GetCorrelationId() => httpContextAccessor.HttpContext?.TraceIdentifier;
}
