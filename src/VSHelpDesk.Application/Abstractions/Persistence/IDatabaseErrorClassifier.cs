namespace VSHelpDesk.Application.Abstractions.Persistence;

public interface IDatabaseErrorClassifier
{
    bool IsProcessedEmailIdempotencyConflict(Exception exception);

    bool IsOptimisticConcurrencyConflict(Exception exception);
}
