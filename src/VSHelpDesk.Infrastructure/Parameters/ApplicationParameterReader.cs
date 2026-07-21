using Microsoft.EntityFrameworkCore;
using VSHelpDesk.Application.Abstractions.Parameters;
using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Application.Features.Parameters;
using VSHelpDesk.Domain.Entities;

namespace VSHelpDesk.Infrastructure.Parameters;

public sealed class ApplicationParameterReader(IApplicationDbContext dbContext) : IApplicationParameterReader
{
    public async Task EnsureCatalogAsync(CancellationToken cancellationToken = default)
    {
        var existingKeys = await dbContext.ApplicationParameters
            .Select(p => p.Key)
            .ToListAsync(cancellationToken);

        var existingKeySet = existingKeys.ToHashSet(StringComparer.Ordinal);
        var added = false;

        foreach (var definition in ApplicationParameterCatalog.All)
        {
            if (existingKeySet.Contains(definition.Key))
            {
                continue;
            }

            dbContext.Add(new ApplicationParameter(
                definition.Key,
                definition.DefaultValue,
                definition.Description));
            added = true;
        }

        if (added)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<int> GetIntAsync(
        string key,
        int defaultValue,
        CancellationToken cancellationToken = default)
    {
        await EnsureCatalogAsync(cancellationToken);

        var value = await dbContext.ApplicationParameters
            .Where(p => p.Key == key)
            .Select(p => p.Value)
            .FirstOrDefaultAsync(cancellationToken);

        if (value is null || !int.TryParse(value.Trim(), out var parsed))
        {
            return defaultValue;
        }

        return parsed;
    }
}
