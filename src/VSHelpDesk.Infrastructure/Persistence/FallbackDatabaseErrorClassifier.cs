using Microsoft.EntityFrameworkCore;
using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Application.Common.Exceptions;

namespace VSHelpDesk.Infrastructure.Persistence;

/// <summary>
/// Provider-agnostic database error classifier for non-PostgreSQL database providers (InMemory, Sqlite).
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

    public bool IsPortalTicketRequestIdempotencyConflict(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is not DbUpdateException dbUpdate)
            {
                continue;
            }

            var message = $"{dbUpdate.Message}\n{dbUpdate.InnerException?.Message}";
            if (message.Contains(
                    "UX_PortalTicketRequests_UserId_IdempotencyKey",
                    StringComparison.OrdinalIgnoreCase) ||
                message.Contains(
                    "PortalTicketRequests.UserId, PortalTicketRequests.IdempotencyKey",
                    StringComparison.OrdinalIgnoreCase))
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
}
