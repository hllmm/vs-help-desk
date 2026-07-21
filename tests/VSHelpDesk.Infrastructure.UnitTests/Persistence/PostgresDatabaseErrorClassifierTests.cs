using Microsoft.EntityFrameworkCore;
using Npgsql;
using VSHelpDesk.Application.Common.Exceptions;
using VSHelpDesk.Infrastructure.Persistence;
using VSHelpDesk.Infrastructure.Persistence.Configurations;

namespace VSHelpDesk.Infrastructure.UnitTests.Persistence;

public sealed class PostgresDatabaseErrorClassifierTests
{
    private readonly PostgresDatabaseErrorClassifier classifier = new();

    [Fact]
    public void IsProcessedEmailIdempotencyConflict_RequiresDbUpdateUniqueAndExactConstraint()
    {
        var matching = CreateDbUpdateWithPostgres(
            PostgresErrorCodes.UniqueViolation,
            ProcessedEmailMessageConfiguration.IdempotencyUniqueIndexName);

        Assert.True(classifier.IsProcessedEmailIdempotencyConflict(matching));
    }

    [Fact]
    public void IsProcessedEmailIdempotencyConflict_RejectsWrongConstraint()
    {
        var wrongConstraint = CreateDbUpdateWithPostgres(
            PostgresErrorCodes.UniqueViolation,
            "IX_Tickets_TicketNumber");

        Assert.False(classifier.IsProcessedEmailIdempotencyConflict(wrongConstraint));
    }

    [Fact]
    public void IsProcessedEmailIdempotencyConflict_RejectsForeignKeyViolation()
    {
        var foreignKey = CreateDbUpdateWithPostgres(
            PostgresErrorCodes.ForeignKeyViolation,
            "FK_TicketMessages_Tickets_TicketId");

        Assert.False(classifier.IsProcessedEmailIdempotencyConflict(foreignKey));
    }

    [Fact]
    public void IsProcessedEmailIdempotencyConflict_RejectsGenericInvalidOperation()
    {
        Assert.False(
            classifier.IsProcessedEmailIdempotencyConflict(
                new InvalidOperationException("not a unique conflict")));
    }

    [Fact]
    public void IsOptimisticConcurrencyConflict_RecognizesTranslatedException()
    {
        var translated = new OptimisticConcurrencyException(
            "conflict",
            new DbUpdateConcurrencyException("raw"));

        Assert.True(classifier.IsOptimisticConcurrencyConflict(translated));
        Assert.True(
            classifier.IsOptimisticConcurrencyConflict(new DbUpdateConcurrencyException("raw")));
        Assert.False(
            classifier.IsOptimisticConcurrencyConflict(new InvalidOperationException("nope")));
    }

    private static DbUpdateException CreateDbUpdateWithPostgres(string sqlState, string constraintName)
    {
        var postgres = CreatePostgresException(sqlState, constraintName);
        return new DbUpdateException("database update failed", postgres);
    }

    private static PostgresException CreatePostgresException(string sqlState, string constraintName)
    {
        // Npgsql 10 exposes a public constructor with (message, severity, invariantSeverity, sqlState, innerException).
        var exception = new PostgresException(
            "simulated postgres error",
            severity: "ERROR",
            invariantSeverity: "ERROR",
            sqlState: sqlState);

        // ConstraintName is settable only via reflection on some versions; try property first.
        var property = typeof(PostgresException).GetProperty(nameof(PostgresException.ConstraintName));
        if (property?.CanWrite == true)
        {
            property.SetValue(exception, constraintName);
            return exception;
        }

        // Fallback: use the internal field if present.
        var field = typeof(PostgresException).GetField(
            "<ConstraintName>k__BackingField",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        field?.SetValue(exception, constraintName);
        return exception;
    }
}
