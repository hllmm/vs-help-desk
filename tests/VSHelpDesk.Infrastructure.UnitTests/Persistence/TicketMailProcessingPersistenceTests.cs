using Microsoft.EntityFrameworkCore;
using VSHelpDesk.Domain.Entities;
using VSHelpDesk.Domain.Enums;
using VSHelpDesk.Infrastructure.Persistence;

namespace VSHelpDesk.Infrastructure.UnitTests.Persistence;

public sealed class TicketMailProcessingPersistenceTests
{
    [Fact]
    public async Task SaveChanges_PersistsTicketMessageAndProcessedEmailInSameTransaction()
    {
        await using var context = CreateInMemoryContext();
        var now = new DateTime(2026, 7, 27, 9, 0, 0, DateTimeKind.Utc);
        var ticket = new Ticket("VS-000001", "Printer offline", "Ada Customer", "ada@example.test");
        var message = new TicketMessage(
            ticket.Id,
            MessageSenderType.Customer,
            "Hello, the office printer is offline.",
            isHtml: false);
        var processed = new ProcessedEmailMessage(
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
        Assert.Equal("<msg-id-unique-001@example.test>", storedProcessed.MessageId);
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
    public void Model_HasUniqueIndex_OnProcessedEmailMessageId()
    {
        using var metadata = CreateMetadataContext();
        var processedType = metadata.Model.FindEntityType(typeof(ProcessedEmailMessage))!;
        Assert.Contains(
            processedType.GetIndexes(),
            index => index.IsUnique &&
                index.Properties.Select(property => property.Name)
                    .SequenceEqual([nameof(ProcessedEmailMessage.MessageId)]));
    }

    [PostgresFact]
    public async Task PostgreSQL_DuplicateTicketNumber_ThrowsDbUpdateException()
    {
        var ticketNumber = $"TN{Guid.NewGuid():N}"[..18];
        await using var context = PostgresTestConnection.CreateContext();

        try
        {
            context.Add(new Ticket(ticketNumber, "First", "Ada", "ada@example.test"));
            await context.SaveChangesAsync();

            context.Add(new Ticket(ticketNumber, "Second", "Bob", "bob@example.test"));
            var exception = await Assert.ThrowsAsync<DbUpdateException>(
                () => context.SaveChangesAsync());

            Assert.Contains("IX_Tickets_TicketNumber", exception.InnerException?.Message ?? exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            await context.Tickets
                .Where(ticket => ticket.TicketNumber == ticketNumber)
                .ExecuteDeleteAsync();
        }
    }

    [PostgresFact]
    public async Task PostgreSQL_DuplicateMessageId_ThrowsDbUpdateException()
    {
        var messageId = $"<dup-{Guid.NewGuid():N}@example.test>";
        var now = DateTime.UtcNow;
        await using var context = PostgresTestConnection.CreateContext();

        try
        {
            context.Add(new ProcessedEmailMessage(messageId, now));
            await context.SaveChangesAsync();

            context.Add(new ProcessedEmailMessage(messageId, now.AddSeconds(1)));
            var exception = await Assert.ThrowsAsync<DbUpdateException>(
                () => context.SaveChangesAsync());

            Assert.Contains(
                "IX_ProcessedEmailMessages_MessageId",
                exception.InnerException?.Message ?? exception.Message,
                StringComparison.Ordinal);
        }
        finally
        {
            await context.ProcessedEmailMessages
                .Where(processed => processed.MessageId == messageId)
                .ExecuteDeleteAsync();
        }
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
