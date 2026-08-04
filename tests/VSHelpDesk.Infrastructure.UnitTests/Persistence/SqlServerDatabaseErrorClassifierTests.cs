using VSHelpDesk.Infrastructure.Persistence;

namespace VSHelpDesk.Infrastructure.UnitTests.Persistence;

public sealed class SqlServerDatabaseErrorClassifierTests
{
    private readonly SqlServerDatabaseErrorClassifier _classifier = new();

    [Fact]
    public void IsProcessedEmailIdempotencyConflict_ReturnsFalse_ForNonSqlExceptions()
    {
        var ex = new InvalidOperationException("Other exception");
        Assert.False(_classifier.IsProcessedEmailIdempotencyConflict(ex));
    }

    [Fact]
    public void IsOptimisticConcurrencyConflict_ReturnsTrue_ForConcurrencyExceptions()
    {
        var ex = new VSHelpDesk.Application.Common.Exceptions.OptimisticConcurrencyException("Concurrency conflict");
        Assert.True(_classifier.IsOptimisticConcurrencyConflict(ex));
    }
}
