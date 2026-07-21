using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Application.Features.Parameters.GetParameterAudit;
using VSHelpDesk.Domain.Entities;
using VSHelpDesk.Domain.Enums;

namespace VSHelpDesk.Application.UnitTests.Features.Parameters;

public sealed class GetParameterAuditHandlerTests
{
    private static readonly Guid OrphanId = Guid.Parse("99999999-8888-7777-6666-555555555555");

    [Fact]
    public async Task List_ReturnsNewestFirst_WithUsernameJoin()
    {
        var admin = new User(
            "Local Admin",
            "admin",
            "admin@example.test",
            "hash",
            UserRole.Admin);

        var older = new ParameterChangeLog(
            "AutoResolve.InactiveDays",
            "3",
            "4",
            admin.Id,
            new DateTime(2026, 7, 20, 10, 0, 0, DateTimeKind.Utc));
        var newer = new ParameterChangeLog(
            "AutoResolve.InactiveDays",
            "4",
            "5",
            admin.Id,
            new DateTime(2026, 7, 21, 12, 0, 0, DateTimeKind.Utc));
        var orphan = new ParameterChangeLog(
            "Other.Key",
            "a",
            "b",
            OrphanId,
            new DateTime(2026, 7, 21, 11, 0, 0, DateTimeKind.Utc));

        var db = new FakeDb([older, orphan, newer], [admin]);
        var handler = new GetParameterAuditHandler(db);

        var items = await handler.HandleAsync(new GetParameterAuditQuery(null, 50));

        Assert.Equal(3, items.Count);
        Assert.Equal(newer.Id, items[0].Id);
        Assert.Equal("admin", items[0].ChangedByUsername);
        Assert.Equal(orphan.Id, items[1].Id);
        Assert.Null(items[1].ChangedByUsername);
        Assert.Equal(older.Id, items[2].Id);
    }

    [Fact]
    public async Task List_FiltersByKey_AndCapsTake()
    {
        var actorId = Guid.NewGuid();
        var logs = Enumerable.Range(0, 5)
            .Select(i => new ParameterChangeLog(
                i % 2 == 0 ? "AutoResolve.InactiveDays" : "Other.Key",
                "0",
                i.ToString(),
                actorId,
                new DateTime(2026, 7, 21, 0, 0, i, DateTimeKind.Utc)))
            .ToList();

        var db = new FakeDb(logs, []);
        var handler = new GetParameterAuditHandler(db);

        var items = await handler.HandleAsync(
            new GetParameterAuditQuery("AutoResolve.InactiveDays", Take: 2));

        Assert.Equal(2, items.Count);
        Assert.All(items, item => Assert.Equal("AutoResolve.InactiveDays", item.ParameterKey));
        Assert.Equal("4", items[0].NewValue);
        Assert.Equal("2", items[1].NewValue);
    }

    [Fact]
    public async Task List_TakeAboveMax_IsCappedAt100()
    {
        var actorId = Guid.NewGuid();
        var logs = Enumerable.Range(0, 120)
            .Select(i => new ParameterChangeLog(
                "AutoResolve.InactiveDays",
                "0",
                i.ToString(),
                actorId,
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(i)))
            .ToList();

        var db = new FakeDb(logs, []);
        var handler = new GetParameterAuditHandler(db);

        var items = await handler.HandleAsync(new GetParameterAuditQuery(null, Take: 500));

        Assert.Equal(GetParameterAuditHandler.MaxTake, items.Count);
    }

    private sealed class FakeDb(
        IReadOnlyList<ParameterChangeLog> logs,
        IReadOnlyList<User> users) : IApplicationDbContext
    {
        public IQueryable<User> Users => users.AsQueryable();
        public IQueryable<Ticket> Tickets => Array.Empty<Ticket>().AsQueryable();
        public IQueryable<TicketMessage> TicketMessages => Array.Empty<TicketMessage>().AsQueryable();
        public IQueryable<TicketAttachment> TicketAttachments =>
            Array.Empty<TicketAttachment>().AsQueryable();
        public IQueryable<ProcessedEmailMessage> ProcessedEmailMessages =>
            Array.Empty<ProcessedEmailMessage>().AsQueryable();
        public IQueryable<ApplicationParameter> ApplicationParameters =>
            Array.Empty<ApplicationParameter>().AsQueryable();
        public IQueryable<ParameterChangeLog> ParameterChangeLogs => logs.AsQueryable();

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
