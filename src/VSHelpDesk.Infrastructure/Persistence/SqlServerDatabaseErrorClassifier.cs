using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Application.Common.Exceptions;
using VSHelpDesk.Infrastructure.Persistence.Configurations;

namespace VSHelpDesk.Infrastructure.Persistence;

public sealed class SqlServerDatabaseErrorClassifier : IDatabaseErrorClassifier
{
    public bool IsProcessedEmailIdempotencyConflict(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException sqlEx)
            {
                // Error 2627: Unique constraint violation
                // Error 2601: Duplicate key row in object with unique index
                if (sqlEx.Number is 2627 or 2601 &&
                    (sqlEx.Message.Contains(ProcessedEmailMessageConfiguration.IdempotencyUniqueIndexName, StringComparison.OrdinalIgnoreCase) ||
                     sqlEx.Message.Contains("idempotency_key", StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    public bool IsOptimisticConcurrencyConflict(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is OptimisticConcurrencyException or DbUpdateConcurrencyException)
            {
                return true;
            }
        }

        return false;
    }
}
