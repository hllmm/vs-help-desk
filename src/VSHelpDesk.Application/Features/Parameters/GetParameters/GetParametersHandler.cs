using VSHelpDesk.Application.Abstractions.Parameters;
using VSHelpDesk.Application.Abstractions.Persistence;

namespace VSHelpDesk.Application.Features.Parameters.GetParameters;

public sealed class GetParametersHandler(
    IApplicationDbContext applicationDbContext,
    IApplicationParameterReader reader)
{
    public async Task<IReadOnlyList<ParameterDto>> HandleAsync(
        CancellationToken cancellationToken = default)
    {
        await reader.EnsureCatalogAsync(cancellationToken);

        var allowedKeys = ApplicationParameterCatalog.All
            .Select(definition => definition.Key)
            .ToList();

        // Sync materialization matches other Application list handlers (e.g. GetTicketList).
        var rows = applicationDbContext.ApplicationParameters
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
