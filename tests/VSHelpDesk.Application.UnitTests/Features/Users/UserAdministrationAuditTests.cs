using VSHelpDesk.Application.Abstractions.Authentication;
using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Application.Common.Exceptions;
using VSHelpDesk.Application.Features.Users.CreateUser;
using VSHelpDesk.Application.Features.Users.SetUserPassword;
using VSHelpDesk.Application.Features.Users.UpdateUser;
using VSHelpDesk.Domain.Entities;
using VSHelpDesk.Domain.Enums;

namespace VSHelpDesk.Application.UnitTests.Features.Users;

public sealed class UserAdministrationAuditTests
{
    private static readonly Guid ActorId =
        Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly DateTimeOffset FixedNow =
        new(2026, 7, 29, 8, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task CreateUser_WritesSecretsFreeAuditInSameSave()
    {
        var db = new FakeDb();
        var handler = new CreateUserHandler(
            db,
            new FakePasswordHasher(),
            new FixedCurrentUser(ActorId),
            new FixedTimeProvider(FixedNow));

        var created = await handler.HandleAsync(
            new CreateUserCommand(
                "Ada Lovelace",
                "ada",
                "ada@example.test",
                "NeverAuditThisPassword!",
                "Support"),
            CancellationToken.None);

        var audit = Assert.Single(db.UserAdministrationAuditLogs);
        Assert.Equal(ActorId, audit.ActorUserId);
        Assert.Equal(created.Id, audit.TargetUserId);
        Assert.Equal("user-created", audit.Action);
        Assert.Null(audit.BeforeValue);
        Assert.Equal(
            "role=Support;active=true;email=ada@example.test;fullName=Ada Lovelace",
            audit.AfterValue);
        Assert.Equal(FixedNow.UtcDateTime, audit.OccurredAt);
        Assert.Equal(1, db.SaveCallCount);
        AssertNoPasswordMaterial(audit);
    }

    [Fact]
    public async Task UpdateUser_WritesBeforeAndAfterAuditInSameSave()
    {
        var target = new User(
            "Ada",
            "ada",
            "old@example.test",
            "existing-password-hash",
            UserRole.Support);
        var db = new FakeDb(target);
        var handler = new UpdateUserHandler(
            db,
            new FixedCurrentUser(ActorId),
            new FixedTimeProvider(FixedNow));

        await handler.HandleAsync(
            new UpdateUserCommand(
                target.Id,
                "Ada Lovelace",
                "ada@example.test",
                "Support",
                false),
            CancellationToken.None);

        var audit = Assert.Single(db.UserAdministrationAuditLogs);
        Assert.Equal(ActorId, audit.ActorUserId);
        Assert.Equal(target.Id, audit.TargetUserId);
        Assert.Equal("user-updated", audit.Action);
        Assert.Equal(
            "role=Support;active=true;email=old@example.test;fullName=Ada",
            audit.BeforeValue);
        Assert.Equal(
            "role=Support;active=false;email=ada@example.test;fullName=Ada Lovelace",
            audit.AfterValue);
        Assert.Equal(FixedNow.UtcDateTime, audit.OccurredAt);
        Assert.Equal(1, db.SaveCallCount);
        AssertNoPasswordMaterial(audit);
    }

    [Fact]
    public async Task SetPassword_WritesEventWithoutStateOrPasswordMaterial()
    {
        var target = new User(
            "Ada",
            "ada",
            "ada@example.test",
            "existing-password-hash",
            UserRole.Support);
        var db = new FakeDb(target);
        var handler = new SetUserPasswordHandler(
            db,
            new FakePasswordHasher(),
            new FixedCurrentUser(ActorId),
            new FixedTimeProvider(FixedNow));

        await handler.HandleAsync(
            new SetUserPasswordCommand(target.Id, "NeverAuditThisPassword!"),
            CancellationToken.None);

        var audit = Assert.Single(db.UserAdministrationAuditLogs);
        Assert.Equal(ActorId, audit.ActorUserId);
        Assert.Equal(target.Id, audit.TargetUserId);
        Assert.Equal("user-password-reset", audit.Action);
        Assert.Null(audit.BeforeValue);
        Assert.Null(audit.AfterValue);
        Assert.Equal(FixedNow.UtcDateTime, audit.OccurredAt);
        Assert.Equal(1, db.SaveCallCount);
        AssertNoPasswordMaterial(audit);
    }

    [Fact]
    public async Task MutationWithoutAuthenticatedActor_ThrowsBeforeSaving()
    {
        var db = new FakeDb();
        var handler = new CreateUserHandler(
            db,
            new FakePasswordHasher(),
            new FixedCurrentUser(null),
            new FixedTimeProvider(FixedNow));

        await Assert.ThrowsAsync<UnauthorizedApplicationException>(
            () => handler.HandleAsync(
                new CreateUserCommand(
                    "Ada",
                    "ada",
                    "ada@example.test",
                    "NeverAuditThisPassword!",
                    "Support"),
                CancellationToken.None));

        Assert.Empty(db.UsersList);
        Assert.Empty(db.UserAdministrationAuditLogs);
        Assert.Equal(0, db.SaveCallCount);
    }

    private static void AssertNoPasswordMaterial(
        UserAdministrationAuditLog audit)
    {
        Assert.DoesNotContain(
            "Password",
            audit.BeforeValue ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "Password",
            audit.AfterValue ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FakeDb : IApplicationDbContext
    {
        public FakeDb(params User[] users)
        {
            UsersList.AddRange(users);
        }

        public List<User> UsersList { get; } = [];
        public List<UserAdministrationAuditLog> UserAdministrationAuditLogs { get; } = [];
        public int SaveCallCount { get; private set; }

        public IQueryable<User> Users => UsersList.AsQueryable();
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
            switch (entity)
            {
                case User user:
                    UsersList.Add(user);
                    break;
                case UserAdministrationAuditLog audit:
                    UserAdministrationAuditLogs.Add(audit);
                    break;
            }
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

    private sealed class FakePasswordHasher : IPasswordHasher
    {
        public string Hash(string password) => $"hashed:{password.Length}";

        public bool Verify(string password, string? passwordHash) => false;
    }

    private sealed class FixedCurrentUser(Guid? userId) : ICurrentUserService
    {
        public Guid? UserId { get; } = userId;

        public bool IsAuthenticated =>
            UserId is Guid value && value != Guid.Empty;
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
