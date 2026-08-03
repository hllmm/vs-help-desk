using VSHelpDesk.Application.Abstractions.Parameters;
using VSHelpDesk.Application.Abstractions.Persistence.Repositories;

namespace VSHelpDesk.Application.Features.Parameters.GetParameters;

public sealed class GetParametersHandler(
    IApplicationParameterRepository parameterRepository,
    IApplicationParameterReader reader)
{
    public async Task<IReadOnlyList<ParameterDto>> HandleAsync(
        CancellationToken cancellationToken = default)
    {
        await reader.EnsureCatalogAsync(cancellationToken);

        var allowedKeys = ApplicationParameterCatalog.All
            .Select(definition => definition.Key)
            .ToList();

        var allParameters = await parameterRepository.GetAllAsync(cancellationToken);
        var rows = allParameters
            .Where(parameter => allowedKeys.Contains(parameter.Key))
            .OrderBy(parameter => parameter.Key)
            .ToList();

        return rows
            .Select(parameter => new ParameterDto(
                parameter.Key,
                parameter.Value,
                parameter.Description,
                parameter.UpdatedAt))
            .ToList();
    }
}
