using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using VSHelpDesk.Application.Abstractions.Authentication;
using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Application.Abstractions.Persistence.Repositories;
using VSHelpDesk.Application.Common.Exceptions;
using VSHelpDesk.Application.Features.Tickets.ResolveTicket;
using VSHelpDesk.Domain.Entities;
using VSHelpDesk.Domain.Enums;

namespace VSHelpDesk.Application.UnitTests.Features.Tickets.ResolveTicket;

public sealed class ResolveTicketHandlerTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 10, 14, 0, 0, TimeSpan.Zero);
    private static readonly Guid SupportUserId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly Guid OriginalCloserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task OpenTicket_ResolvesWithCurrentUserAndExactResult()
    {
        var ticket = Ticket.Create(
            "VS-000501",
            "Open resolve",
            "Ada",
            "ada@example.test",
            FixedNow.UtcDateTime.AddHours(-1));
        var db = new FakeDb(ticket);
        var handler = CreateHandler(db);

        var result = await handler.HandleAsync(
            new ResolveTicketCommand(ticket.Id),
            CancellationToken.None);

        Assert.Equal(ticket.Id, result.TicketId);
        Assert.Equal(ticket.TicketNumber, result.TicketNumber);
        Assert.Equal(nameof(TicketStatus.Resolved), result.Status);
        Assert.Equal(FixedNow.UtcDateTime, result.ResolvedAt);
        Assert.Equal(FixedNow.UtcDateTime, result.UpdatedAt);
        Assert.Equal(FixedNow.UtcDateTime, result.LastActivityAt);
        Assert.Equal(SupportUserId, result.ClosedByUserId);
        Assert.True(result.Changed);
        Assert.Equal(1, db.SaveCallCount);
        Assert.Equal(TicketStatus.Resolved, ticket.Status);
        Assert.Equal(SupportUserId, ticket.ClosedByUserId);
    }

    [Fact]
    public async Task AlreadyResolved_ReturnsChangedFalseWithoutSaveOrOverwrite()
    {
        var ticket = Ticket.Create(
            "VS-000502",
            "Already resolved",
            "Ada",
            "ada@example.test",
            FixedNow.UtcDateTime.AddHours(-2));
        var originalResolvedAt = FixedNow.UtcDateTime.AddHours(-1);
        Assert.True(ticket.ResolveManually(originalResolvedAt, OriginalCloserId));
        var db = new FakeDb(ticket);
        var handler = CreateHandler(db);

        var result = await handler.HandleAsync(
            new ResolveTicketCommand(ticket.Id),
            CancellationToken.None);

        Assert.False(result.Changed);
        Assert.Equal(ticket.Id, result.TicketId);
        Assert.Equal(ticket.TicketNumber, result.TicketNumber);
        Assert.Equal(nameof(TicketStatus.Resolved), result.Status);
        Assert.Equal(originalResolvedAt, result.ResolvedAt);
        Assert.Equal(OriginalCloserId, result.ClosedByUserId);
        Assert.Equal(0, db.SaveCallCount);
        Assert.Equal(OriginalCloserId, ticket.ClosedByUserId);
        Assert.Equal(originalResolvedAt, ticket.ResolvedAt);
    }

    [Fact]
    public async Task UnknownTicket_ThrowsNotFound()
    {
        var db = new FakeDb();
        var handler = CreateHandler(db);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.HandleAsync(new ResolveTicketCommand(Guid.NewGuid()), CancellationToken.None));

        Assert.Equal(0, db.SaveCallCount);
    }

    [Fact]
    public async Task MissingCurrentUser_ThrowsUnauthorizedWithoutSave()
    {
        var ticket = Ticket.Create(
            "VS-000503",
            "No user",
            "Ada",
            "ada@example.test",
            FixedNow.UtcDateTime);
        var db = new FakeDb(ticket);
        var handler = CreateHandler(db, currentUser: new AnonymousUser());

        await Assert.ThrowsAsync<UnauthorizedApplicationException>(() =>
            handler.HandleAsync(new ResolveTicketCommand(ticket.Id), CancellationToken.None));

        Assert.Equal(0, db.SaveCallCount);
        Assert.Equal(TicketStatus.New, ticket.Status);
        Assert.Null(ticket.ClosedByUserId);
    }

    [Fact]
    public async Task SaveConflict_PropagatesWithoutClearReloadOrRetry()
    {
        var ticket = Ticket.Create(
            "VS-000504",
            "Conflict",
            "Ada",
            "ada@example.test",
            FixedNow.UtcDateTime.AddHours(-1));
        var db = new FakeDb(ticket, conflictOnSave: true);
        var handler = CreateHandler(db);

        await Assert.ThrowsAsync<OptimisticConcurrencyException>(() =>
            handler.HandleAsync(new ResolveTicketCommand(ticket.Id), CancellationToken.None));

        Assert.Equal(1, db.SaveCallCount);
        Assert.Equal(0, db.ClearTrackedCallCount);
        Assert.Equal(1, db.TicketQueryCount);
        Assert.Null(db.PersistedClosedByUserId);
        Assert.Equal(TicketStatus.New, db.PersistedStatus);
    }

    [Fact]
    public async Task Cancellation_PropagatesWithoutSuccessLog()
    {
        var ticket = Ticket.Create(
            "VS-000505",
            "Cancel",
            "Ada",
            "ada@example.test",
            FixedNow.UtcDateTime);
        using var cts = new CancellationTokenSource();
        var db = new FakeDb(ticket)
        {
            OnSave = () => cts.Cancel()
        };
        var logger = new RecordingLogger();
        var handler = CreateHandler(db, logger: logger);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            handler.HandleAsync(new ResolveTicketCommand(ticket.Id), cts.Token));

        Assert.Empty(logger.InformationMessages);
    }

    private static ResolveTicketHandler CreateHandler(
        FakeDb db,
        ICurrentUserService? currentUser = null,
        ILogger<ResolveTicketHandler>? logger = null) =>
        new(
            db,
            db,
            currentUser ?? new FixedCurrentUser(),
            new FixedTimeProvider(FixedNow),
            logger ?? NullLogger<ResolveTicketHandler>.Instance);

    private sealed class FixedCurrentUser : ICurrentUserService
    {
        public Guid? UserId => SupportUserId;
        public bool IsAuthenticated => true;
    }

    private sealed class AnonymousUser : ICurrentUserService
    {
        public Guid? UserId => null;
        public bool IsAuthenticated => false;
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class RecordingLogger : ILogger<ResolveTicketHandler>
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

    private sealed class FakeDb : IApplicationDbContext, ITicketRepository, IUnitOfWork
    {
        private readonly Ticket? ticket;
        private readonly bool conflictOnSave;
        private TicketStatus persistedStatus;
        private Guid? persistedClosedByUserId;

        public FakeDb(Ticket? ticket = null, bool conflictOnSave = false)
        {
            this.ticket = ticket;
            this.conflictOnSave = conflictOnSave;
            if (ticket is not null)
            {
                CapturePersisted(ticket);
            }
        }

        public Task<Ticket?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Tickets.FirstOrDefault(t => t.Id == id));

        public Task<Ticket?> GetByNumberAsync(string ticketNumber, CancellationToken cancellationToken) =>
            Task.FromResult(Tickets.FirstOrDefault(t => t.TicketNumber == ticketNumber));

        public IQueryable<Ticket> GetListQueryable() => Tickets;

        public Task AddAsync(Ticket ticket, CancellationToken cancellationToken) => Task.CompletedTask;

        public void Update(Ticket ticket) { }

        public Task AddMessageAsync(TicketMessage message, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<bool> MessageExistsAsync(Guid messageId, CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<TicketMessage?> GetMessageByIdAsync(Guid messageId, CancellationToken cancellationToken) => Task.FromResult<TicketMessage?>(null);

        public Task<Guid> GetFirstMessageIdAsync(Guid ticketId, CancellationToken cancellationToken) => Task.FromResult(Guid.Empty);
        public Action? OnSave { get; init; }
        public int SaveCallCount { get; private set; }
        public int ClearTrackedCallCount { get; private set; }
        public int TicketQueryCount { get; private set; }
        public TicketStatus PersistedStatus => persistedStatus;
        public Guid? PersistedClosedByUserId => persistedClosedByUserId;

        public IQueryable<User> Users => Array.Empty<User>().AsQueryable();

        public IQueryable<Ticket> Tickets
        {
            get
            {
                TicketQueryCount++;
                return ticket is null
                    ? Array.Empty<Ticket>().AsQueryable()
                    : new[] { ticket }.AsQueryable();
            }
        }

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

        public IQueryable<SystemLog> SystemLogs =>
            Array.Empty<SystemLog>().AsQueryable();

        public void Add<TEntity>(TEntity entity) where TEntity : class
        {
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
        {
            SaveCallCount++;
            OnSave?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();

            if (conflictOnSave)
            {
                throw new OptimisticConcurrencyException("Simulated resolve concurrency conflict.");
            }

            if (ticket is not null)
            {
                CapturePersisted(ticket);
            }

            return Task.FromResult(1);
        }

        public void ClearTrackedChanges() => ClearTrackedCallCount++;

        private void CapturePersisted(Ticket source)
        {
            persistedStatus = source.Status;
            persistedClosedByUserId = source.ClosedByUserId;
        }
    }
}
