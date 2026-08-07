namespace VSHelpDesk.Application.Abstractions.Persistence;

public interface IDatabaseErrorClassifier
{
    bool IsProcessedEmailIdempotencyConflict(Exception exception);

    bool IsPortalTicketRequestIdempotencyConflict(Exception exception) => false;

    bool IsOptimisticConcurrencyConflict(Exception exception);
}
