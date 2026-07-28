using VSHelpDesk.Application.Abstractions.Authentication;
using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Application.Features.Users;
using VSHelpDesk.Application.Features.Users.UpdateUser;
using VSHelpDesk.Domain.Entities;
using VSHelpDesk.Domain.Enums;
using VSHelpDesk.Domain.Exceptions;

namespace VSHelpDesk.Application.UnitTests.Features.Users;

public sealed class LastAdminGuardTests
{
    private static readonly Guid ActorId =
        Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    [Fact]
    public async Task UpdateUser_ExecutesCompleteMutationThroughTransactionOnce()
    {
        var target = CreateUser(UserRole.Support, isActive: true);
        var db = new FakeDb(target);
        var transaction = new RecordingTransaction();
        var handler = new UpdateUserHandler(
            db,
            new FixedCurrentUser(ActorId),
            new FixedTimeProvider(
                new DateTimeOffset(2026, 7, 29, 9, 0, 0, TimeSpan.Zero)),
            transaction);

        var result = await handler.HandleAsync(
            new UpdateUserCommand(
                target.Id,
                "Updated User",
                "updated@example.test",
                "Support",
                true),
            CancellationToken.None);

        Assert.Equal(1, transaction.CallCount);
        Assert.Equal("Updated User", result.FullName);
        Assert.Equal(1, db.SaveCallCount);
    }

    [Fact]
    public void EnsureCanDemoteOrDeactivate_SoleAdminDemote_Throws()
    {
        var sole = CreateUser(UserRole.Admin, isActive: true);
        var users = new[] { sole }.AsQueryable();

        var ex = Assert.Throws<DomainException>(() =>
            LastAdminGuard.EnsureCanDemoteOrDeactivate(
                users,
                sole.Id,
                UserRole.Support,
                newIsActive: true));

        Assert.Equal(LastAdminGuard.ErrorCode, ex.Message);
    }

    [Fact]
    public void EnsureCanDemoteOrDeactivate_SoleAdminDeactivate_Throws()
    {
        var sole = CreateUser(UserRole.Admin, isActive: true);
        var users = new[] { sole }.AsQueryable();

        var ex = Assert.Throws<DomainException>(() =>
            LastAdminGuard.EnsureCanDemoteOrDeactivate(
                users,
                sole.Id,
                UserRole.Admin,
                newIsActive: false));

        Assert.Equal(LastAdminGuard.ErrorCode, ex.Message);
    }

    [Fact]
    public void EnsureCanDemoteOrDeactivate_TwoAdminsDemoteOne_Ok()
    {
        var adminA = CreateUser(UserRole.Admin, isActive: true);
        var adminB = CreateUser(UserRole.Admin, isActive: true);
        var users = new[] { adminA, adminB }.AsQueryable();

        LastAdminGuard.EnsureCanDemoteOrDeactivate(
            users,
            adminA.Id,
            UserRole.Support,
            newIsActive: true);
    }

    [Fact]
    public void EnsureCanDemoteOrDeactivate_TwoAdminsDeactivateOne_Ok()
    {
        var adminA = CreateUser(UserRole.Admin, isActive: true);
        var adminB = CreateUser(UserRole.Admin, isActive: true);
        var users = new[] { adminA, adminB }.AsQueryable();

        LastAdminGuard.EnsureCanDemoteOrDeactivate(
            users,
            adminA.Id,
            UserRole.Admin,
            newIsActive: false);
    }

    [Fact]
    public void EnsureCanDemoteOrDeactivate_SupportUser_NoThrow()
    {
        var admin = CreateUser(UserRole.Admin, isActive: true);
        var support = CreateUser(UserRole.Support, isActive: true);
        var users = new[] { admin, support }.AsQueryable();

        LastAdminGuard.EnsureCanDemoteOrDeactivate(
            users,
            support.Id,
            UserRole.Support,
            newIsActive: false);
    }

    [Fact]
    public void EnsureCanDemoteOrDeactivate_InactiveAdmin_NoThrow()
    {
        var activeAdmin = CreateUser(UserRole.Admin, isActive: true);
        var inactiveAdmin = CreateUser(UserRole.Admin, isActive: false);
        var users = new[] { activeAdmin, inactiveAdmin }.AsQueryable();

        LastAdminGuard.EnsureCanDemoteOrDeactivate(
            users,
            inactiveAdmin.Id,
            UserRole.Support,
            newIsActive: false);
    }

    private static User CreateUser(UserRole role, bool isActive)
    {
        var user = new User(
            fullName: "Test User",
            username: Guid.NewGuid().ToString("N")[..8],
            email: $"{Guid.NewGuid():N}@test",
            passwordHash: "hash",
            role: role);

        if (!isActive)
        {
            user.Deactivate();
        }

        return user;
    }

    private sealed class RecordingTransaction : IUserAdministrationTransaction
    {
        public int CallCount { get; private set; }

        public async Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return await operation(cancellationToken);
        }
    }

    private sealed class FixedCurrentUser(Guid userId) : ICurrentUserService
    {
        public Guid? UserId { get; } = userId;

        public bool IsAuthenticated => true;
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class FakeDb(User user) : IApplicationDbContext
    {
        private readonly List<User> users = [user];

        public int SaveCallCount { get; private set; }

        public IQueryable<User> Users => users.AsQueryable();
        public IQueryable<Ticket> Tickets => Array.Empty<Ticket>().AsQueryable();
        public IQueryable<TicketMessage> TicketMessages =>
            Array.Empty<TicketMessage>().AsQueryable();
        public IQueryable<TicketAttachment> TicketAttachments =>
            Array.Empty<TicketAttachment>().AsQueryable();
        public IQueryable<ProcessedEmailMessage> ProcessedEmailMessages =>
            Array.Empty<ProcessedEmailMessage>().AsQueryable();
        public IQueryable<ApplicationParameter> ApplicationParameters =>
            Array.Empty<ApplicationParameter>().AsQueryable();
        public IQueryable<ParameterChangeLog> ParameterChangeLogs =>
            Array.Empty<ParameterChangeLog>().AsQueryable();

        public void Add<TEntity>(TEntity entity) where TEntity : class
        {
        }

        public Task<int> SaveChangesAsync(
            CancellationToken cancellationToken = default)
        {
            SaveCallCount++;
            return Task.FromResult(1);
        }

        public void ClearTrackedChanges()
        {
        }
    }
}
