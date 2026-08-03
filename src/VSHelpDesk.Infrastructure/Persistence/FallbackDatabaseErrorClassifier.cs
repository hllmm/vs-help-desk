using Microsoft.EntityFrameworkCore;
using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Application.Common.Exceptions;

namespace VSHelpDesk.Infrastructure.Persistence;

/// <summary>
/// Provider-agnostic database error classifier for non-PostgreSQL database providers (InMemory, Sqlite, SqlServer).
/// </summary>
public sealed class FallbackDatabaseErrorClassifier : IDatabaseErrorClassifier
{
    public bool IsProcessedEmailIdempotencyConflict(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is DbUpdateException dbUpdate)
            {
                var msg = dbUpdate.Message ?? string.Empty;
                var innerMsg = dbUpdate.InnerException?.Message ?? string.Empty;

                if (msg.Contains("IdempotencyKey", StringComparison.OrdinalIgnoreCase) ||
                    msg.Contains("IX_ProcessedEmailMessages", StringComparison.OrdinalIgnoreCase) ||
                    msg.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase) ||
                    msg.Contains("duplicate key", StringComparison.OrdinalIgnoreCase) ||
                    msg.Contains("2601", StringComparison.OrdinalIgnoreCase) ||
                    msg.Contains("2627", StringComparison.OrdinalIgnoreCase) ||
                    innerMsg.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase) ||
                    innerMsg.Contains("IdempotencyKey", StringComparison.OrdinalIgnoreCase) ||
                    innerMsg.Contains("duplicate key", StringComparison.OrdinalIgnoreCase) ||
                    innerMsg.Contains("2601", StringComparison.OrdinalIgnoreCase) ||
                    innerMsg.Contains("2627", StringComparison.OrdinalIgnoreCase))
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
