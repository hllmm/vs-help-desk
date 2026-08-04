using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VSHelpDesk.Domain.Entities;
using VSHelpDesk.Domain.Enums;
using VSHelpDesk.Infrastructure.Persistence;
using VSHelpDesk.Infrastructure.Persistence.Repositories;

namespace VSHelpDesk.Infrastructure.IntegrationTests;

public sealed class AttachmentRepositoryContractTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<ApplicationDbContext> _options;

    public AttachmentRepositoryContractTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var context = new ApplicationDbContext(_options);
        context.Database.EnsureCreated();
    }

    [Fact]
    public async Task AddAsync_And_GetByIdAsync_PersistsAndRetrievesAttachment()
    {
        var messageId = Guid.NewGuid();

        await using (var seedContext = new ApplicationDbContext(_options))
        {
            var ticket = Ticket.Create(
                "TICK-000099",
                "Attachment Test Ticket",
                "John",
                "john@test.com",
                DateTime.UtcNow);

            seedContext.Tickets.Add(ticket);
            await seedContext.SaveChangesAsync();

            var message = new TicketMessage(
                ticket.Id,
                MessageSenderType.Customer,
                "Message with attachment");

            typeof(TicketMessage).GetProperty(nameof(TicketMessage.Id))?.SetValue(message, messageId);

            seedContext.TicketMessages.Add(message);
            await seedContext.SaveChangesAsync();
        }

        await using var writeContext = new ApplicationDbContext(_options);
        var repo = new EfTicketAttachmentRepository(writeContext);

        var attachment = new TicketAttachment(
            messageId,
            "test.pdf",
            "stored_test.pdf",
            "storage/test.pdf",
            "application/pdf",
            1024);

        await repo.AddAsync(attachment);
        await writeContext.SaveChangesAsync();

        await using var readContext = new ApplicationDbContext(_options);
        var readRepo = new EfTicketAttachmentRepository(readContext);

        var loaded = await readRepo.GetByIdAsync(attachment.Id);
        Assert.NotNull(loaded);
        Assert.Equal("test.pdf", loaded.FileName);
        Assert.Equal(1024, loaded.FileSize);
    }

    public void Dispose()
    {
        _connection.Dispose();
    }
}
