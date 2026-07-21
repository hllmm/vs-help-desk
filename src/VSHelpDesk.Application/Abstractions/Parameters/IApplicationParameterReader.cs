namespace VSHelpDesk.Application.Abstractions.Parameters;

public interface IApplicationParameterReader
{
    Task EnsureCatalogAsync(CancellationToken cancellationToken = default);

    Task<int> GetIntAsync(string key, int defaultValue, CancellationToken cancellationToken = default);
}
