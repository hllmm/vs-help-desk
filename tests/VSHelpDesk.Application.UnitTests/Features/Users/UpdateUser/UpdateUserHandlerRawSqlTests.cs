using VSHelpDesk.Application.Abstractions.Authentication;
using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Application.Abstractions.Persistence.Repositories;
using VSHelpDesk.Application.Features.Users.UpdateUser;
using VSHelpDesk.Domain.Entities;
using VSHelpDesk.Domain.Enums;

namespace VSHelpDesk.Application.UnitTests.Features.Users.UpdateUser;

public sealed class UpdateUserHandlerRawSqlTests
{
    [Fact]
    public async Task HandleAsync_SkipsPostgreSqlAdvisoryLockWhenProviderCapabilityIsUnavailable()
    {
        var user = new User(
            "Support User",
            "support-user",
            "support-user@example.test",
            "hash",
            UserRole.Support);
        var dbContext = new NonPostgresApplicationDbContext();
        var handler = new UpdateUserHandler(
            new SingleUserRepository(user),
            new NoOpUnitOfWork(),
            dbContext,
            new AnonymousCurrentUser());

        var result = await handler.HandleAsync(
            new UpdateUserCommand(
                user.Id,
                "Updated Support User",
                "updated-support@example.test",
                "Support",
                IsActive: true));

        Assert.Equal(user.Id, result.Id);
        Assert.Equal(0, dbContext.RawSqlCallCount);
    }

    private sealed class SingleUserRepository(User user) : IUserRepository
    {
        public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult<User?>(id == user.Id ? user : null);

        public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default) =>
            Task.FromResult<User?>(null);

        public Task<User?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default) =>
            Task.FromResult<User?>(null);

        public IQueryable<User> GetListQueryable() => new[] { user }.AsQueryable();

        public Task AddAsync(User item, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Update(User item)
        {
        }
    }

    private sealed class NoOpUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(1);

        public void ClearTrackedChanges()
        {
        }
    }

    private sealed class AnonymousCurrentUser : ICurrentUserService
    {
        public Guid? UserId => null;

        public bool IsAuthenticated => false;
    }

    private sealed class NonPostgresApplicationDbContext : IApplicationDbContext
    {
        public int RawSqlCallCount { get; private set; }

        public bool SupportsPostgresRawSql => false;

        public IQueryable<User> Users => Array.Empty<User>().AsQueryable();

        public IQueryable<Ticket> Tickets => Array.Empty<Ticket>().AsQueryable();

        public IQueryable<TicketMessage> TicketMessages => Array.Empty<TicketMessage>().AsQueryable();

        public IQueryable<TicketAttachment> TicketAttachments => Array.Empty<TicketAttachment>().AsQueryable();

        public IQueryable<ProcessedEmailMessage> ProcessedEmailMessages =>
            Array.Empty<ProcessedEmailMessage>().AsQueryable();

        public IQueryable<ApplicationParameter> ApplicationParameters =>
            Array.Empty<ApplicationParameter>().AsQueryable();

        public IQueryable<ParameterChangeLog> ParameterChangeLogs =>
            Array.Empty<ParameterChangeLog>().AsQueryable();

        public IQueryable<SystemLog> SystemLogs => Array.Empty<SystemLog>().AsQueryable();

        public void Add<TEntity>(TEntity entity) where TEntity : class
        {
        }

        public void Remove<TEntity>(TEntity entity) where TEntity : class
        {
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(1);

        public Task ExecuteSqlRawAsync(string sql, CancellationToken cancellationToken = default)
        {
            RawSqlCallCount++;
            throw new NotSupportedException(
                "Raw SQL execution through this abstraction requires PostgreSQL.");
        }

        public void ClearTrackedChanges()
        {
        }
    }
}
