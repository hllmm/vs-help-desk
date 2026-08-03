using Microsoft.Extensions.Logging;
using VSHelpDesk.Application.Abstractions.Authentication;
using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Application.Abstractions.Persistence.Repositories;
using VSHelpDesk.Application.Common.Exceptions;
using VSHelpDesk.Application.Features.Tickets.AssignTicket;
using VSHelpDesk.Domain.Entities;
using VSHelpDesk.Domain.Enums;
using VSHelpDesk.Domain.Exceptions;

namespace VSHelpDesk.Application.UnitTests.Features.Tickets.AssignTicket;

public sealed class AssignTicketHandlerTests
{
    private static readonly Guid ActorId = Guid.NewGuid();
    private static readonly DateTimeOffset FixedNow = new(2026, 7, 26, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Unauthenticated_ThrowsUnauthorized()
    {
        var handler = CreateHandler(
            new FakeDb([], []),
            currentUser: new FixedCurrentUser(null, false));

        await Assert.ThrowsAsync<UnauthorizedApplicationException>(() =>
            handler.HandleAsync(
                new AssignTicketCommand(Guid.NewGuid(), Guid.NewGuid()),
                CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_TicketNotFound_ThrowsNotFound()
    {
        var handler = CreateHandler(new FakeDb([], []));
        var missingTicketId = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.HandleAsync(
                new AssignTicketCommand(missingTicketId, Guid.NewGuid()),
                CancellationToken.None));

        Assert.Contains(missingTicketId.ToString(), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HandleAsync_AssignToEmptyGuid_ThrowsAssigneeRequired()
    {
        var ticket = CreateTicket();
        var handler = CreateHandler(new FakeDb([ticket], []));

        var exception = await Assert.ThrowsAsync<DomainException>(() =>
            handler.HandleAsync(
                new AssignTicketCommand(ticket.Id, Guid.Empty),
                CancellationToken.None));

        Assert.Equal(AssignTicketCodes.AssigneeRequired, exception.Message);
    }

    [Fact]
    public async Task HandleAsync_AssignToInactiveUser_ThrowsAssigneeNotAvailable()
    {
        var ticket = CreateTicket();
        var inactiveUser = CreateUser("Inactive User", "inactive", isActive: false);
        var handler = CreateHandler(new FakeDb([ticket], [inactiveUser]));

        var exception = await Assert.ThrowsAsync<DomainException>(() =>
            handler.HandleAsync(
                new AssignTicketCommand(ticket.Id, inactiveUser.Id),
                CancellationToken.None));

        Assert.Equal(AssignTicketCodes.AssigneeNotAvailable, exception.Message);
    }

    [Fact]
    public async Task HandleAsync_AssignToMissingUser_ThrowsAssigneeNotAvailable()
    {
        var ticket = CreateTicket();
        var handler = CreateHandler(new FakeDb([ticket], []));

        var exception = await Assert.ThrowsAsync<DomainException>(() =>
            handler.HandleAsync(
                new AssignTicketCommand(ticket.Id, Guid.NewGuid()),
                CancellationToken.None));

        Assert.Equal(AssignTicketCodes.AssigneeNotAvailable, exception.Message);
    }

    [Fact]
    public async Task HandleAsync_ValidAssignment_MutatesStateSavesAndLogs()
    {
        var ticket = CreateTicket();
        var targetUser = CreateUser("Support Agent", "agent1", isActive: true);
        var logger = new RecordingLogger<AssignTicketHandler>();
        var db = new FakeDb([ticket], [targetUser]);
        var handler = CreateHandler(db, logger: logger);

        var result = await handler.HandleAsync(
            new AssignTicketCommand(ticket.Id, targetUser.Id),
            CancellationToken.None);

        Assert.True(result.Changed);
        Assert.Equal(ticket.Id, result.TicketId);
        Assert.Equal(targetUser.Id, result.AssignedUserId);
        Assert.Equal(FixedNow.UtcDateTime, result.UpdatedAt);
        Assert.Equal(targetUser.Id, ticket.AssignedUserId);
        Assert.Equal(FixedNow.UtcDateTime, ticket.UpdatedAt);
        Assert.Equal(1, db.SaveCallCount);
        Assert.Single(logger.InformationMessages);
        Assert.Contains(ticket.Id.ToString(), logger.InformationMessages[0], StringComparison.Ordinal);
        Assert.Contains(targetUser.Id.ToString(), logger.InformationMessages[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task HandleAsync_NoOpReassignment_ReturnsChangedFalseWithoutSaving()
    {
        var ticket = CreateTicket();
        var targetUser = CreateUser("Support Agent", "agent1", isActive: true);
        ticket.Assign(targetUser.Id, FixedNow.UtcDateTime.AddHours(-1));
        var db = new FakeDb([ticket], [targetUser]);
        var handler = CreateHandler(db);

        var result = await handler.HandleAsync(
            new AssignTicketCommand(ticket.Id, targetUser.Id),
            CancellationToken.None);

        Assert.False(result.Changed);
        Assert.Equal(targetUser.Id, result.AssignedUserId);
        Assert.Equal(0, db.SaveCallCount);
    }

    [Fact]
    public async Task HandleAsync_Unassign_ClearsAssigneeAndSaves()
    {
        var ticket = CreateTicket();
        var targetUser = CreateUser("Support Agent", "agent1", isActive: true);
        ticket.Assign(targetUser.Id, FixedNow.UtcDateTime.AddHours(-1));
        var db = new FakeDb([ticket], [targetUser]);
        var handler = CreateHandler(db);

        var result = await handler.HandleAsync(
            new AssignTicketCommand(ticket.Id, null),
            CancellationToken.None);

        Assert.True(result.Changed);
        Assert.Null(result.AssignedUserId);
        Assert.Null(ticket.AssignedUserId);
        Assert.Equal(1, db.SaveCallCount);
    }

    [Fact]
    public async Task HandleAsync_UnassignAlreadyUnassigned_ReturnsChangedFalseWithoutSaving()
    {
        var ticket = CreateTicket();
        var db = new FakeDb([ticket], []);
        var handler = CreateHandler(db);

        var result = await handler.HandleAsync(
            new AssignTicketCommand(ticket.Id, null),
            CancellationToken.None);

        Assert.False(result.Changed);
        Assert.Null(result.AssignedUserId);
        Assert.Equal(0, db.SaveCallCount);
    }

    [Fact]
    public async Task HandleAsync_DbUpdateConcurrencyException_PropagatesOptimisticConcurrencyException()
    {
        var ticket = CreateTicket();
        var targetUser = CreateUser("Support Agent", "agent1", isActive: true);
        var conflictDb = new FakeDb([ticket], [targetUser]) { ThrowConflictOnSave = true };
        var handler = CreateHandler(conflictDb);

        await Assert.ThrowsAsync<OptimisticConcurrencyException>(() =>
            handler.HandleAsync(
                new AssignTicketCommand(ticket.Id, targetUser.Id),
                CancellationToken.None));

        Assert.Equal(1, conflictDb.SaveCallCount);
    }

    private static AssignTicketHandler CreateHandler(
        FakeDb db,
        ICurrentUserService? currentUser = null,
        ILogger<AssignTicketHandler>? logger = null) =>
        new(
            db,
            db,
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
        IReadOnlyList<User> users) : IApplicationDbContext, ITicketRepository, IUserRepository, IUnitOfWork
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
        public IQueryable<SystemLog> SystemLogs => Array.Empty<SystemLog>().AsQueryable();

        public Task<Ticket?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(tickets.FirstOrDefault(t => t.Id == id));

        public Task<Ticket?> GetByNumberAsync(string ticketNumber, CancellationToken cancellationToken = default) =>
            Task.FromResult(tickets.FirstOrDefault(t => t.TicketNumber == ticketNumber));

        public IQueryable<Ticket> GetListQueryable() => Tickets;

        public Task AddAsync(Ticket ticket, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Update(Ticket ticket) { }

        public Task AddMessageAsync(TicketMessage message, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<bool> MessageExistsAsync(Guid messageId, CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task<TicketMessage?> GetMessageByIdAsync(Guid messageId, CancellationToken cancellationToken = default) => Task.FromResult<TicketMessage?>(null);

        public Task<Guid> GetFirstMessageIdAsync(Guid ticketId, CancellationToken cancellationToken = default) => Task.FromResult(Guid.Empty);

        Task<User?> IUserRepository.GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(users.FirstOrDefault(u => u.Id == id));

        public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default) =>
            Task.FromResult(users.FirstOrDefault(u => u.Email == email));

        public Task<User?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default) =>
            Task.FromResult(users.FirstOrDefault(u => u.Username == username));

        IQueryable<User> IUserRepository.GetListQueryable() => Users;

        public Task AddAsync(User user, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Update(User user) { }

        public void Add<TEntity>(TEntity entity) where TEntity : class =>
            throw new NotSupportedException();

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCallCount++;
            if (ThrowConflictOnSave)
            {
                throw new OptimisticConcurrencyException(
                    "Simulated concurrency error during test.",
                    new Exception());
            }

            return Task.FromResult(1);
        }

        public void ClearTrackedChanges()
        {
        }
    }
}
