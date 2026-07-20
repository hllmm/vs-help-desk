using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Application.Features.Tickets.GetTicketList;
using VSHelpDesk.Domain.Entities;
using VSHelpDesk.Domain.Enums;

namespace VSHelpDesk.Application.UnitTests.Features.Tickets.GetTicketList;

public sealed class GetTicketListHandlerTests
{
    private static readonly DateTime T0 = new(2026, 8, 3, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task UC003_ReturnsTicketsOrderedByLastActivityDescending()
    {
        var older = Ticket.Create("VS-000001", "Older", "A", "a@t.com", T0);
        older.RecordMessageActivity(T0.AddHours(1));
        var newer = Ticket.Create("VS-000002", "Newer", "B", "b@t.com", T0);
        newer.RecordMessageActivity(T0.AddHours(2));
        var handler = new GetTicketListHandler(new FakeDb(older, newer));

        var items = await handler.HandleAsync(new GetTicketListQuery(), CancellationToken.None);

        Assert.Equal(2, items.Count);
        Assert.Equal("VS-000002", items[0].TicketNumber);
        Assert.Equal("VS-000001", items[1].TicketNumber);
        Assert.Equal("Newer", items[0].Subject);
        Assert.Equal("B", items[0].CustomerName);
    }

    [Fact]
    public async Task UC003_StatusFilter_ReturnsOnlyMatching()
    {
        var open = Ticket.Create("VS-000010", "Open", "A", "a@t.com", T0);
        var resolved = Ticket.Create("VS-000011", "Done", "B", "b@t.com", T0);
        resolved.ResolveManually(
            T0.AddHours(1),
            Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var handler = new GetTicketListHandler(new FakeDb(open, resolved));

        var items = await handler.HandleAsync(
            new GetTicketListQuery(TicketStatus.Resolved),
            CancellationToken.None);

        Assert.Single(items);
        Assert.Equal("VS-000011", items[0].TicketNumber);
        Assert.Equal(nameof(TicketStatus.Resolved), items[0].Status);
    }

    private sealed class FakeDb(params Ticket[] tickets) : IApplicationDbContext
    {
        public IQueryable<User> Users => Array.Empty<User>().AsQueryable();
        public IQueryable<Ticket> Tickets { get; } = tickets.AsQueryable();
        public IQueryable<TicketMessage> TicketMessages => Array.Empty<TicketMessage>().AsQueryable();
        public IQueryable<TicketAttachment> TicketAttachments =>
            Array.Empty<TicketAttachment>().AsQueryable();

        public IQueryable<ProcessedEmailMessage> ProcessedEmailMessages =>
            Array.Empty<ProcessedEmailMessage>().AsQueryable();

        public void Add<TEntity>(TEntity entity) where TEntity : class
        {
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(0);

        public void ClearTrackedChanges()
        {
        }

    }
}
