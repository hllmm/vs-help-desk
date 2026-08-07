using Microsoft.EntityFrameworkCore;
using Npgsql;
using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Application.Common.Exceptions;
using VSHelpDesk.Infrastructure.Persistence.Configurations;

namespace VSHelpDesk.Infrastructure.Persistence;

public sealed class PostgresDatabaseErrorClassifier : IDatabaseErrorClassifier
{
    public bool IsProcessedEmailIdempotencyConflict(Exception exception)
    {
        if (exception is not DbUpdateException)
        {
            // Still unwrap: outer wrappers may sit above DbUpdateException.
            for (var current = exception; current is not null; current = current.InnerException)
            {
                if (current is DbUpdateException dbUpdate)
                {
                    return IsIdempotencyUniqueViolation(dbUpdate);
                }
            }

            return false;
        }

        return IsIdempotencyUniqueViolation(exception);
    }

    public bool IsPortalTicketRequestIdempotencyConflict(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is not PostgresException postgres ||
                postgres.SqlState != PostgresErrorCodes.UniqueViolation)
            {
                continue;
            }

            if (string.Equals(
                    postgres.ConstraintName,
                    PortalTicketRequestConfiguration.UserKeyUniqueIndexName,
                    StringComparison.Ordinal))
            {
                return true;
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

    private static bool IsIdempotencyUniqueViolation(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is not PostgresException postgres)
            {
                continue;
            }

            if (postgres.SqlState == PostgresErrorCodes.UniqueViolation &&
                string.Equals(
                    postgres.ConstraintName,
                    ProcessedEmailMessageConfiguration.IdempotencyUniqueIndexName,
                    StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
