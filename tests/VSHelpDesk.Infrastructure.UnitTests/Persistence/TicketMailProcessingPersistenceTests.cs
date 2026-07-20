using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;
using VSHelpDesk.Application.Common.Exceptions;
using VSHelpDesk.Domain.Entities;
using VSHelpDesk.Domain.Enums;
using VSHelpDesk.Infrastructure.Persistence;
using VSHelpDesk.Infrastructure.Persistence.Configurations;

namespace VSHelpDesk.Infrastructure.UnitTests.Persistence;

public sealed class TicketMailProcessingPersistenceTests
{
    [Fact]
    public async Task SaveChanges_PersistsTicketMessageAndProcessedEmailInSameTransaction()
    {
        await using var context = CreateInMemoryContext();
        var now = new DateTime(2026, 7, 27, 9, 0, 0, DateTimeKind.Utc);
        var ticket = Ticket.Create(
            "VS-000001",
            "Printer offline",
            "Ada Customer",
            "ada@example.test",
            now);
        var message = new TicketMessage(
            ticket.Id,
            MessageSenderType.Customer,
            "Hello, the office printer is offline.",
            isHtml: false,
            createdAtUtc: now);
        var processed = ProcessedEmailMessage.ForCreatedTicket(
            "<msg-id-unique-001@example.test>",
            "<msg-id-unique-001@example.test>",
            now,
            ticket.Id);

        context.Add(ticket);
        context.Add(message);
        context.Add(processed);
        var written = await context.SaveChangesAsync();

        Assert.Equal(3, written);
        Assert.Equal(1, await context.Tickets.CountAsync());
        Assert.Equal(1, await context.TicketMessages.CountAsync());
        Assert.Equal(1, await context.ProcessedEmailMessages.CountAsync());

        var storedTicket = await context.Tickets.SingleAsync();
        var storedMessage = await context.TicketMessages.SingleAsync();
        var storedProcessed = await context.ProcessedEmailMessages.SingleAsync();

        Assert.Equal(ticket.Id, storedTicket.Id);
        Assert.Equal("VS-000001", storedTicket.TicketNumber);
        Assert.Equal(ticket.Id, storedMessage.TicketId);
        Assert.Equal(MessageSenderType.Customer, storedMessage.SenderType);
        Assert.Equal(ticket.Id, storedProcessed.TicketId);
        Assert.Equal("<msg-id-unique-001@example.test>", storedProcessed.IdempotencyKey);
        Assert.Equal(ProcessedEmailDisposition.CreatedTicket, storedProcessed.Disposition);
    }

    [Fact]
    public void Model_HasUniqueIndex_OnTicketNumber()
    {
        using var metadata = CreateMetadataContext();
        var ticketType = metadata.Model.FindEntityType(typeof(Ticket))!;
        Assert.Contains(
            ticketType.GetIndexes(),
            index => index.IsUnique &&
                index.Properties.Select(property => property.Name)
                    .SequenceEqual([nameof(Ticket.TicketNumber)]));
    }

    [Fact]
    public void Model_HasUniqueIndex_OnProcessedEmailIdempotencyKey()
    {
        using var metadata = CreateMetadataContext();
        var processed = metadata.Model.FindEntityType(typeof(ProcessedEmailMessage))!;
        var unique = Assert.Single(processed.GetIndexes(), index => index.IsUnique);
        Assert.Equal(
            "UX_ProcessedEmailMessages_IdempotencyKey",
            unique.GetDatabaseName());
        Assert.Equal(
            [nameof(ProcessedEmailMessage.IdempotencyKey)],
            unique.Properties.Select(property => property.Name));
        Assert.Equal(
            998,
            processed.FindProperty(nameof(ProcessedEmailMessage.SourceMessageId))!
                .GetMaxLength());
        Assert.Equal(
            500,
            processed.FindProperty(nameof(ProcessedEmailMessage.ProcessingNote))!
                .GetMaxLength());

        var version = metadata.Model.FindEntityType(typeof(Ticket))!
            .FindProperty(nameof(Ticket.Version))!;
        Assert.True(version.IsConcurrencyToken);
        Assert.Equal(ValueGenerated.OnAddOrUpdate, version.ValueGenerated);
    }

    [PostgresFact]
    public async Task DuplicateIdempotencyKey_IsOnlyRecoverableUniqueConflict()
    {
        var idempotencyKey = $"<dup-{Guid.NewGuid():N}@example.test>";
        var now = DateTime.UtcNow;
        var classifier = new PostgresDatabaseErrorClassifier();
        await using var context = PostgresTestConnection.CreateContext();

        try
        {
            context.Add(ProcessedEmailMessage.ForQuarantine(idempotencyKey, idempotencyKey, now));
            await context.SaveChangesAsync();

            context.Add(ProcessedEmailMessage.ForQuarantine(
                idempotencyKey,
                idempotencyKey,
                now.AddSeconds(1)));
            var exception = await Assert.ThrowsAsync<DbUpdateException>(
                () => context.SaveChangesAsync());

            Assert.True(classifier.IsProcessedEmailIdempotencyConflict(exception));
            Assert.Contains(
                ProcessedEmailMessageConfiguration.IdempotencyUniqueIndexName,
                exception.InnerException?.Message ?? exception.Message,
                StringComparison.Ordinal);
        }
        finally
        {
            await context.ProcessedEmailMessages
                .Where(processed => processed.IdempotencyKey == idempotencyKey)
                .ExecuteDeleteAsync();
        }
    }

    [PostgresFact]
    public async Task ForeignKeyViolation_IsNotIdempotencyConflict()
    {
        var classifier = new PostgresDatabaseErrorClassifier();
        await using var context = PostgresTestConnection.CreateContext();
        var unknownTicketId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        context.Add(new TicketMessage(
            unknownTicketId,
            MessageSenderType.Customer,
            "orphan message",
            createdAtUtc: now));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(
            () => context.SaveChangesAsync());

        var postgres = FindPostgresException(exception);
        Assert.NotNull(postgres);
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, postgres.SqlState);
        Assert.False(classifier.IsProcessedEmailIdempotencyConflict(exception));
    }

    [PostgresFact]
    public async Task TicketXmin_SecondWriterBecomesOptimisticConcurrencyException()
    {
        var ticketNumber = $"TX{Guid.NewGuid():N}"[..18];
        var stamp = DateTime.UtcNow;
        Guid ticketId;

        await using (var seed = PostgresTestConnection.CreateContext())
        {
            var ticket = Ticket.Create(
                ticketNumber,
                "Concurrency",
                "Ada",
                "ada@example.test",
                stamp);
            seed.Add(ticket);
            await seed.SaveChangesAsync();
            ticketId = ticket.Id;
        }

        try
        {
            await using var first = PostgresTestConnection.CreateContext();
            await using var second = PostgresTestConnection.CreateContext();

            var left = await first.Tickets.SingleAsync(ticket => ticket.Id == ticketId);
            var right = await second.Tickets.SingleAsync(ticket => ticket.Id == ticketId);

            left.RecordMessageActivity(stamp.AddMinutes(1));
            await first.SaveChangesAsync();

            right.RecordMessageActivity(stamp.AddMinutes(2));
            await Assert.ThrowsAsync<OptimisticConcurrencyException>(
                () => second.SaveChangesAsync());
        }
        finally
        {
            await using var cleanup = PostgresTestConnection.CreateContext();
            await cleanup.Tickets
                .Where(ticket => ticket.Id == ticketId)
                .ExecuteDeleteAsync();
        }
    }

    [PostgresFact]
    public async Task PostgreSQL_DuplicateTicketNumber_ThrowsDbUpdateException()
    {
        var ticketNumber = $"TN{Guid.NewGuid():N}"[..18];
        await using var context = PostgresTestConnection.CreateContext();

        try
        {
            var stamp = DateTime.UtcNow;
            context.Add(Ticket.Create(ticketNumber, "First", "Ada", "ada@example.test", stamp));
            await context.SaveChangesAsync();

            context.Add(Ticket.Create(ticketNumber, "Second", "Bob", "bob@example.test", stamp));
            var exception = await Assert.ThrowsAsync<DbUpdateException>(
                () => context.SaveChangesAsync());

            Assert.Contains("IX_Tickets_TicketNumber", exception.InnerException?.Message ?? exception.Message, StringComparison.Ordinal);
            Assert.False(new PostgresDatabaseErrorClassifier().IsProcessedEmailIdempotencyConflict(exception));
        }
        finally
        {
            await context.Tickets
                .Where(ticket => ticket.TicketNumber == ticketNumber)
                .ExecuteDeleteAsync();
        }
    }

    private static PostgresException? FindPostgresException(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgres)
            {
                return postgres;
            }
        }

        return null;
    }

    private static ApplicationDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new ApplicationDbContext(options);
    }

    private static ApplicationDbContext CreateMetadataContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=metadata_test;Username=test_user")
            .Options;
        return new ApplicationDbContext(options);
    }
}
