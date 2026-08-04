using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Application.Abstractions.Persistence.Repositories;
using VSHelpDesk.Application.Features.Parameters.GetParameterAudit;
using VSHelpDesk.Domain.Entities;

namespace VSHelpDesk.Application.UnitTests.Features.Parameters;

public sealed class GetParameterAuditHandlerTests
{
    [Fact]
    public async Task List_FiltersByKey_OrdersDescendingByDate_AppliesDefaultTake_AndResolvesUsername()
    {
        var targetKey = "AutoResolve.InactiveDays";
        var otherKey = "Smtp.Host";
        var user1 = new User("User One", "user.one", "u1@example.test", "hash", Domain.Enums.UserRole.Admin);
        var user2 = new User("User Two", "user.two", "u2@example.test", "hash", Domain.Enums.UserRole.Admin);

        var log1 = new ParameterChangeLog(targetKey, "3", "5", user1.Id, new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc));
        var log2 = new ParameterChangeLog(targetKey, "5", "7", user2.Id, new DateTime(2026, 1, 2, 10, 0, 0, DateTimeKind.Utc));
        var log3 = new ParameterChangeLog(otherKey, "a", "b", user1.Id, new DateTime(2026, 1, 3, 10, 0, 0, DateTimeKind.Utc));

        var db = new FakeDb([log1, log2, log3], [user1, user2]);
        var handler = new GetParameterAuditHandler(db, db);

        var items = await handler.HandleAsync(new GetParameterAuditQuery(targetKey, Take: 0));

        Assert.Equal(2, items.Count);

        Assert.Equal(log2.Id, items[0].Id);
        Assert.Equal(targetKey, items[0].ParameterKey);
        Assert.Equal("5", items[0].OldValue);
        Assert.Equal("7", items[0].NewValue);
        Assert.Equal(user2.Id, items[0].ChangedByUserId);
        Assert.Equal("user.two", items[0].ChangedByUsername);
        Assert.Equal(log2.ChangedAt, items[0].ChangedAt);

        Assert.Equal(log1.Id, items[1].Id);
        Assert.Equal(user1.Id, items[1].ChangedByUserId);
        Assert.Equal("user.one", items[1].ChangedByUsername);
    }

    [Fact]
    public async Task List_NullUsernameWhenUserNotFound()
    {
        var targetKey = "AutoResolve.InactiveDays";
        var missingUserId = Guid.NewGuid();
        var log = new ParameterChangeLog(targetKey, "1", "2", missingUserId, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var db = new FakeDb([log], []);
        var handler = new GetParameterAuditHandler(db, db);

        var items = await handler.HandleAsync(new GetParameterAuditQuery(targetKey, Take: 10));

        var item = Assert.Single(items);
        Assert.Equal(missingUserId, item.ChangedByUserId);
        Assert.Null(item.ChangedByUsername);
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
        var handler = new GetParameterAuditHandler(db, db);

        var items = await handler.HandleAsync(new GetParameterAuditQuery(null, Take: 500));

        Assert.Equal(GetParameterAuditHandler.MaxTake, items.Count);
    }

    private sealed class FakeDb(
        IReadOnlyList<ParameterChangeLog> logs,
        IReadOnlyList<User> users) : IApplicationDbContext, IApplicationParameterRepository, IUserRepository
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
        public IQueryable<SystemLog> SystemLogs => Array.Empty<SystemLog>().AsQueryable();

        public Task<ApplicationParameter?> GetByCodeAsync(string code, CancellationToken cancellationToken) => Task.FromResult<ApplicationParameter?>(null);
        public Task<ApplicationParameter?> GetByKeyAsync(string key, CancellationToken cancellationToken) => Task.FromResult<ApplicationParameter?>(null);
        public Task<IReadOnlyList<ApplicationParameter>> GetAllAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ApplicationParameter>>([]);
        public Task AddChangeLogAsync(ParameterChangeLog changeLog, CancellationToken cancellationToken) => Task.CompletedTask;
        public void Update(ApplicationParameter parameter) { }
        public IQueryable<ParameterChangeLog> GetChangeLogsQueryable() => ParameterChangeLogs;

        public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(users.FirstOrDefault(u => u.Id == id));
        public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken) => Task.FromResult(users.FirstOrDefault(u => u.Email == email));
        public Task<User?> GetByUsernameAsync(string username, CancellationToken cancellationToken) => Task.FromResult(users.FirstOrDefault(u => u.Username == username));
        public IQueryable<User> GetListQueryable() => Users;
        public Task AddAsync(User user, CancellationToken cancellationToken) => Task.CompletedTask;
        public void Update(User user) { }

        public void Add<TEntity>(TEntity entity) where TEntity : class
        {
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken) =>
            Task.FromResult(0);

        public void ClearTrackedChanges()
        {
        }
    }
}
