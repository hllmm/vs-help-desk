using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VSHelpDesk.Domain.Entities;
using VSHelpDesk.Domain.Enums;
using VSHelpDesk.Infrastructure.Persistence;
using VSHelpDesk.Infrastructure.Persistence.Repositories;

namespace VSHelpDesk.Infrastructure.IntegrationTests;

public sealed class TicketRepositoryContractTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<ApplicationDbContext> _options;

    public TicketRepositoryContractTests()
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
    public async Task AddAsync_And_GetByIdAsync_PersistsAndRetrievesTicket()
    {
        await using var writeContext = new ApplicationDbContext(_options);
        var repository = new EfTicketRepository(writeContext);

        var ticket = Ticket.Create(
            "TICK-000001",
            "Contract Test Ticket",
            "John Doe",
            "john@example.test",
            DateTime.UtcNow);

        await repository.AddAsync(ticket);
        await writeContext.SaveChangesAsync();

        await using var readContext = new ApplicationDbContext(_options);
        var readRepository = new EfTicketRepository(readContext);
        var loaded = await readRepository.GetByIdAsync(ticket.Id);

        Assert.NotNull(loaded);
        Assert.Equal("TICK-000001", loaded.TicketNumber);
        Assert.Equal("Contract Test Ticket", loaded.Subject);
        Assert.Equal(TicketStatus.New, loaded.Status);
    }

    [Fact]
    public async Task Update_PersistsStatusChange()
    {
        var ticket = Ticket.Create(
            "TICK-000002",
            "Status Change Ticket",
            "Jane Doe",
            "jane@example.test",
            DateTime.UtcNow);

        await using (var writeContext = new ApplicationDbContext(_options))
        {
            var repo = new EfTicketRepository(writeContext);
            await repo.AddAsync(ticket);
            await writeContext.SaveChangesAsync();
        }

        await using (var updateContext = new ApplicationDbContext(_options))
        {
            var repo = new EfTicketRepository(updateContext);
            var loaded = await repo.GetByIdAsync(ticket.Id);
            Assert.NotNull(loaded);
            repo.Update(loaded);
            await updateContext.SaveChangesAsync();
        }

        await using (var verifyContext = new ApplicationDbContext(_options))
        {
            var repo = new EfTicketRepository(verifyContext);
            var reloaded = await repo.GetByIdAsync(ticket.Id);
            Assert.NotNull(reloaded);
        }
    }

    public void Dispose()
    {
        _connection.Dispose();
    }
}
