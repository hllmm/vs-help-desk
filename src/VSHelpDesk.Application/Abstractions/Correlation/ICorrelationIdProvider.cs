namespace VSHelpDesk.Application.Abstractions.Correlation;

public interface ICorrelationIdProvider
{
    string? GetCorrelationId();
}
