using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Application.Abstractions.Persistence.Repositories;
using VSHelpDesk.Application.Features.Tickets.GetAssignableUsers;
using VSHelpDesk.Domain.Entities;
using VSHelpDesk.Domain.Enums;

namespace VSHelpDesk.Application.UnitTests.Features.Tickets.GetAssignableUsers;

public sealed class GetAssignableUsersHandlerTests
{
    [Fact]
    public async Task ReturnsOnlyActiveUsersOrderedByFullNameThenUsername()
    {
        var zeynep = CreateUser("Zeynep Destek", "zeynep", UserRole.Support, true);
        var ayseAdmin = CreateUser("Ayşe Kaya", "ayse.admin", UserRole.Admin, true);
        var ayseSupport = CreateUser("Ayşe Kaya", "ayse.support", UserRole.Support, true);
        var inactive = CreateUser("Aaa Pasif", "pasif", UserRole.Support, false);
        var db = new FakeDb([zeynep, inactive, ayseSupport, ayseAdmin]);
        var handler = new GetAssignableUsersHandler(db);

        var result = await handler.HandleAsync(CancellationToken.None);

        Assert.Equal(3, result.Count);
        Assert.Equal(
            ["ayse.admin", "ayse.support", "zeynep"],
            result.Select(item => item.Username));
        Assert.All(result, item => Assert.NotEqual(inactive.Id, item.Id));
        Assert.Equal("Ayşe Kaya", result[0].FullName);
    }

    private static User CreateUser(
        string fullName,
        string username,
        UserRole role,
        bool active)
    {
        var user = new User(
            fullName,
            username,
            $"{username}@example.test",
            "hash",
            role);
        if (!active)
        {
            user.Deactivate();
        }
        return user;
    }

    private sealed class FakeDb(IReadOnlyList<User> users) : IApplicationDbContext, IUserRepository
    {
        public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(users.FirstOrDefault(u => u.Id == id));

        public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken) =>
            Task.FromResult(users.FirstOrDefault(u => u.Email == email));

        public Task<User?> GetByUsernameAsync(string username, CancellationToken cancellationToken) =>
            Task.FromResult(users.FirstOrDefault(u => u.Username == username));

        public IQueryable<User> GetListQueryable() => Users;

        public Task AddAsync(User user, CancellationToken cancellationToken) => Task.CompletedTask;

        public void Update(User user) { }

        public IQueryable<User> Users => users.AsQueryable();
        public IQueryable<Ticket> Tickets => Array.Empty<Ticket>().AsQueryable();
        public IQueryable<TicketMessage> TicketMessages => Array.Empty<TicketMessage>().AsQueryable();
        public IQueryable<TicketAttachment> TicketAttachments => Array.Empty<TicketAttachment>().AsQueryable();
        public IQueryable<ProcessedEmailMessage> ProcessedEmailMessages => Array.Empty<ProcessedEmailMessage>().AsQueryable();
        public IQueryable<ApplicationParameter> ApplicationParameters => Array.Empty<ApplicationParameter>().AsQueryable();
        public IQueryable<ParameterChangeLog> ParameterChangeLogs => Array.Empty<ParameterChangeLog>().AsQueryable();
        public IQueryable<SystemLog> SystemLogs => Array.Empty<SystemLog>().AsQueryable();

        public void Add<TEntity>(TEntity entity) where TEntity : class =>
            throw new NotSupportedException();

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken) =>
            Task.FromResult(0);

        public void ClearTrackedChanges()
        {
        }
    }
}
