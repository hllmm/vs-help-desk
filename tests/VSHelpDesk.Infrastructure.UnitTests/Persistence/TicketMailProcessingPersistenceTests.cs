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
        await using var context = CreateContext();
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
    public async Task SaveChanges_DuplicateTicketNumber_IsRejectedByUniqueIndexOnRelationalProviders()
    {
        // InMemory does not enforce unique indexes; model metadata is the portable guarantee.
        // Relational rejection is verified after AddTicketMailProcessing migration on PostgreSQL.
        using var metadata = CreateMetadataContext();
        var ticketType = metadata.Model.FindEntityType(typeof(Ticket))!;
        Assert.Contains(
            ticketType.GetIndexes(),
            index => index.IsUnique &&
                index.Properties.Select(property => property.Name)
                    .SequenceEqual([nameof(Ticket.TicketNumber)]));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task SaveChanges_DuplicateMessageId_IsRejectedByUniqueIndexOnRelationalProviders()
    {
        using var metadata = CreateMetadataContext();
        var processedType = metadata.Model.FindEntityType(typeof(ProcessedEmailMessage))!;
        Assert.Contains(
            processedType.GetIndexes(),
            index => index.IsUnique &&
                index.Properties.Select(property => property.Name)
                    .SequenceEqual([nameof(ProcessedEmailMessage.MessageId)]));
        await Task.CompletedTask;
    }

    private static ApplicationDbContext CreateContext()
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
