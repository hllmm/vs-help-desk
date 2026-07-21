using Microsoft.Extensions.Logging;
using VSHelpDesk.Application.Abstractions.Authentication;
using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Application.Common.Exceptions;
using VSHelpDesk.Application.Features.Tickets.AssignTicket;
using VSHelpDesk.Domain.Entities;
using VSHelpDesk.Domain.Enums;
using VSHelpDesk.Domain.Exceptions;

namespace VSHelpDesk.Application.UnitTests.Features.Tickets.AssignTicket;

public sealed class AssignTicketHandlerTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 8, 14, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid ActorId =
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Fact]
    public async Task ActiveUser_AssignsAndReturnsPersistedResultWithAuditLog()
    {
        var ticket = CreateTicket();
        var assignee = CreateUser("Ayşe Destek", "ayse", isActive: true);
        var db = new FakeDb([ticket], [assignee]);
        var logger = new RecordingLogger<AssignTicketHandler>();
        var handler = CreateHandler(db, logger: logger);

        var result = await handler.HandleAsync(
            new AssignTicketCommand(ticket.Id, assignee.Id),
            CancellationToken.None);

        Assert.True(result.Changed);
        Assert.Equal(ticket.Id, result.TicketId);
        Assert.Equal(assignee.Id, result.AssignedUserId);
        Assert.Equal(FixedNow.UtcDateTime, result.UpdatedAt);
        Assert.Equal(assignee.Id, ticket.AssignedUserId);
        Assert.Equal(1, db.SaveCallCount);
        Assert.Contains(logger.InformationMessages, message =>
            message.Contains(ticket.Id.ToString(), StringComparison.Ordinal)
            && message.Contains(ActorId.ToString(), StringComparison.Ordinal)
            && message.Contains(assignee.Id.ToString(), StringComparison.Ordinal));
    }

    [Fact]
    public async Task NullTarget_UnassignsAndSameStateDoesNotSaveAgain()
    {
        var ticket = CreateTicket();
        var assignee = CreateUser("Ayşe Destek", "ayse", isActive: true);
        Assert.True(ticket.Assign(assignee.Id, FixedNow.UtcDateTime.AddMinutes(-5)));
        var db = new FakeDb([ticket], [assignee]);
        var handler = CreateHandler(db);

        var first = await handler.HandleAsync(
            new AssignTicketCommand(ticket.Id, UserId: null),
            CancellationToken.None);
        var second = await handler.HandleAsync(
            new AssignTicketCommand(ticket.Id, UserId: null),
            CancellationToken.None);

        Assert.True(first.Changed);
        Assert.False(second.Changed);
        Assert.Null(first.AssignedUserId);
        Assert.Null(ticket.AssignedUserId);
        Assert.Equal(1, db.SaveCallCount);
        Assert.Equal(FixedNow.UtcDateTime, ticket.UpdatedAt);
        Assert.Equal(FixedNow.UtcDateTime.AddHours(-1), ticket.LastActivityAt);
    }

    [Fact]
    public async Task SameTarget_IsIdempotentWithoutSave()
    {
        var ticket = CreateTicket();
        var assignee = CreateUser("Ayşe Destek", "ayse", isActive: true);
        var originalUpdatedAt = FixedNow.UtcDateTime.AddMinutes(-5);
        Assert.True(ticket.Assign(assignee.Id, originalUpdatedAt));
        var db = new FakeDb([ticket], [assignee]);
        var handler = CreateHandler(db);

        var result = await handler.HandleAsync(
            new AssignTicketCommand(ticket.Id, assignee.Id),
            CancellationToken.None);

        Assert.False(result.Changed);
        Assert.Equal(originalUpdatedAt, result.UpdatedAt);
        Assert.Equal(0, db.SaveCallCount);
    }

    [Fact]
    public async Task MissingInactiveAndEmptyTargetsUseStableCodes()
    {
        var ticket = CreateTicket();
        var inactive = CreateUser("Pasif Kullanıcı", "pasif", isActive: false);
        var db = new FakeDb([ticket], [inactive]);
        var handler = CreateHandler(db);

        var missing = await Assert.ThrowsAsync<DomainException>(() =>
            handler.HandleAsync(
                new AssignTicketCommand(ticket.Id, Guid.NewGuid()),
                CancellationToken.None));
        var inactiveError = await Assert.ThrowsAsync<DomainException>(() =>
            handler.HandleAsync(
                new AssignTicketCommand(ticket.Id, inactive.Id),
                CancellationToken.None));
        var empty = await Assert.ThrowsAsync<DomainException>(() =>
            handler.HandleAsync(
                new AssignTicketCommand(ticket.Id, Guid.Empty),
                CancellationToken.None));

        Assert.Equal(AssignTicketCodes.AssigneeNotAvailable, missing.Message);
        Assert.Equal(AssignTicketCodes.AssigneeNotAvailable, inactiveError.Message);
        Assert.Equal(AssignTicketCodes.AssigneeRequired, empty.Message);
        Assert.Equal(0, db.SaveCallCount);
    }

    [Fact]
    public async Task AnonymousActorAndUnknownTicketAreRejectedBeforeSave()
    {
        var ticket = CreateTicket();
        var db = new FakeDb([ticket], []);
        var anonymous = CreateHandler(db, currentUser: new FixedCurrentUser(null, false));

        await Assert.ThrowsAsync<UnauthorizedApplicationException>(() =>
            anonymous.HandleAsync(
                new AssignTicketCommand(ticket.Id, UserId: null),
                CancellationToken.None));

        var authenticated = CreateHandler(db);
        await Assert.ThrowsAsync<NotFoundException>(() =>
            authenticated.HandleAsync(
                new AssignTicketCommand(Guid.NewGuid(), UserId: null),
                CancellationToken.None));
        Assert.Equal(0, db.SaveCallCount);
    }

    [Fact]
    public async Task ResolvedTicketAndSaveConflictAreNotRetried()
    {
        var resolved = CreateTicket("VS-000802");
        Assert.True(resolved.ResolveManually(
            FixedNow.UtcDateTime.AddMinutes(-2),
            ActorId));
        var resolvedDb = new FakeDb([resolved], []);
        var resolvedHandler = CreateHandler(resolvedDb);

        var resolvedError = await Assert.ThrowsAsync<DomainException>(() =>
            resolvedHandler.HandleAsync(
                new AssignTicketCommand(resolved.Id, UserId: null),
                CancellationToken.None));
        Assert.Equal(AssignTicketCodes.TicketResolved, resolvedError.Message);

        var open = CreateTicket("VS-000803");
        var target = CreateUser("Ayşe Destek", "ayse", isActive: true);
        var conflictDb = new FakeDb([open], [target]) { ThrowConflictOnSave = true };
        var conflictHandler = CreateHandler(conflictDb);

        await Assert.ThrowsAsync<OptimisticConcurrencyException>(() =>
            conflictHandler.HandleAsync(
                new AssignTicketCommand(open.Id, target.Id),
                CancellationToken.None));
        Assert.Equal(1, conflictDb.SaveCallCount);
    }

    private static AssignTicketHandler CreateHandler(
        FakeDb db,
        ICurrentUserService? currentUser = null,
        ILogger<AssignTicketHandler>? logger = null) =>
        new(
            db,
            currentUser ?? new FixedCurrentUser(ActorId, true),
            new FixedTimeProvider(FixedNow),
            logger ?? new RecordingLogger<AssignTicketHandler>());

    private static Ticket CreateTicket(string number = "VS-000801") =>
        Ticket.Create(
            number,
            "Assignment",
            "Ada",
            "ada@example.test",
            FixedNow.UtcDateTime.AddHours(-1));

    private static User CreateUser(string fullName, string username, bool isActive)
    {
        var user = new User(
            fullName,
            username,
            $"{username}@example.test",
            "hash",
            UserRole.Support);
        if (!isActive)
        {
            user.Deactivate();
        }
        return user;
    }

    private sealed class FixedCurrentUser(Guid? userId, bool authenticated)
        : ICurrentUserService
    {
        public Guid? UserId => userId;
        public bool IsAuthenticated => authenticated;
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> InformationMessages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Information)
            {
                InformationMessages.Add(formatter(state, exception));
            }
        }
    }

    private sealed class FakeDb(
        IReadOnlyList<Ticket> tickets,
        IReadOnlyList<User> users) : IApplicationDbContext
    {
        public bool ThrowConflictOnSave { get; init; }
        public int SaveCallCount { get; private set; }

        public IQueryable<User> Users => users.AsQueryable();
        public IQueryable<Ticket> Tickets => tickets.AsQueryable();
        public IQueryable<TicketMessage> TicketMessages => Array.Empty<TicketMessage>().AsQueryable();
        public IQueryable<TicketAttachment> TicketAttachments => Array.Empty<TicketAttachment>().AsQueryable();
        public IQueryable<ProcessedEmailMessage> ProcessedEmailMessages => Array.Empty<ProcessedEmailMessage>().AsQueryable();
        public IQueryable<ApplicationParameter> ApplicationParameters => Array.Empty<ApplicationParameter>().AsQueryable();
        public IQueryable<ParameterChangeLog> ParameterChangeLogs => Array.Empty<ParameterChangeLog>().AsQueryable();

        public void Add<TEntity>(TEntity entity) where TEntity : class =>
            throw new NotSupportedException();

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCallCount++;
            if (ThrowConflictOnSave)
            {
                throw new OptimisticConcurrencyException("assignment conflict");
            }
            return Task.FromResult(1);
        }

        public void ClearTrackedChanges() => throw new NotSupportedException();
    }
}
