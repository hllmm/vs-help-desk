using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Application.Abstractions.Persistence.Repositories;
using VSHelpDesk.Application.Common.Exceptions;
using VSHelpDesk.Application.Features.Tickets.ReplyToTicket;
using VSHelpDesk.Domain.Entities;
using VSHelpDesk.Domain.Enums;

namespace VSHelpDesk.Application.UnitTests.Features.Tickets.ReplyToTicket;

public sealed class AppendCustomerReplyHandlerTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 7, 31, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task UC006_WaitingTicket_AppendsMessageAndSetsCustomerReplied()
    {
        var ticket = Ticket.Create("VS-000080", "Subject", "Ada", "ada@example.test", FixedNow.UtcDateTime);
        ticket.MarkAsWaitingCustomerReply(FixedNow.UtcDateTime.AddMinutes(-10));
        var db = new FakeDb(ticket);
        var handler = CreateHandler(db);

        var result = await handler.HandleAsync(
            new AppendCustomerReplyCommand(
                IdempotencyKey: "<reply-1@test>",
                SourceMessageId: "<reply-1@test>",
                TicketNumber: "VS-000080",
                Content: "Still broken",
                FromAddress: "ada@example.test"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.WasAlreadyProcessed);
        Assert.False(result.Value.WasReopened);
        Assert.Equal(TicketStatus.CustomerReplied, ticket.Status);
        Assert.Single(db.Messages);
        Assert.Equal("Still broken", db.Messages[0].Content);
        Assert.False(db.Messages[0].IsHtml);
    }

    [Fact]
    public async Task Reply_PersistsAppendedReplyAndNotRequiredAck()
    {
        var ticket = Ticket.Create("VS-000085", "Subject", "Ada", "ada@example.test", FixedNow.UtcDateTime);
        var db = new FakeDb(ticket);
        var handler = CreateHandler(db);

        var result = await handler.HandleAsync(
            new AppendCustomerReplyCommand(
                IdempotencyKey: "<reply-disp@test>",
                SourceMessageId: "<reply-disp@test>",
                TicketNumber: "VS-000085",
                Content: "Body",
                FromAddress: "ada@example.test"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var processed = Assert.Single(db.Processed);
        Assert.Equal(ProcessedEmailDisposition.AppendedReply, processed.Disposition);
        Assert.Equal(AcknowledgementStatus.NotRequired, processed.AcknowledgementStatus);
        Assert.Null(processed.AcknowledgementNextAttemptAt);
        Assert.Equal("<reply-disp@test>", processed.IdempotencyKey);
    }

    [Fact]
    public async Task Reply_PreservesDifferentSourceAndIdempotencyValues()
    {
        var ticket = Ticket.Create("VS-000086", "Subject", "Ada", "ada@example.test", FixedNow.UtcDateTime);
        var db = new FakeDb(ticket);
        var handler = CreateHandler(db);

        var result = await handler.HandleAsync(
            new AppendCustomerReplyCommand(
                IdempotencyKey: "receipt:fake:reply-hash",
                SourceMessageId: "<reply-source@test>",
                TicketNumber: "VS-000086",
                Content: "Body",
                FromAddress: "ada@example.test"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var processed = Assert.Single(db.Processed);
        Assert.Equal("receipt:fake:reply-hash", processed.IdempotencyKey);
        Assert.Equal("<reply-source@test>", processed.SourceMessageId);
        Assert.NotEqual(processed.IdempotencyKey, processed.SourceMessageId);
    }

    [Fact]
    public async Task Reply_OneOptimisticConflict_ReloadsAndRetriesOnce()
    {
        var ticket = Ticket.Create("VS-000087", "Subject", "Ada", "ada@example.test", FixedNow.UtcDateTime);
        ticket.MarkAsWaitingCustomerReply(FixedNow.UtcDateTime.AddMinutes(-5));
        var db = new FakeDb(ticket) { FailOptimisticConcurrencyTimes = 1 };
        var handler = CreateHandler(db, new ConcurrencyAwareClassifier());

        var result = await handler.HandleAsync(
            new AppendCustomerReplyCommand(
                IdempotencyKey: "<reply-concurrency@test>",
                SourceMessageId: "<reply-concurrency@test>",
                TicketNumber: "VS-000087",
                Content: "After conflict",
                FromAddress: "ada@example.test"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.WasAlreadyProcessed);
        Assert.Equal(2, db.SaveChangesCallCount);
        Assert.Single(db.Messages);
        Assert.Equal("After conflict", db.Messages[0].Content);
        Assert.Equal(TicketStatus.CustomerReplied, ticket.Status);
        Assert.Single(db.Processed);
    }

    [Fact]
    public async Task Reply_TwoOptimisticConflicts_Throws()
    {
        var ticket = Ticket.Create("VS-000088", "Subject", "Ada", "ada@example.test", FixedNow.UtcDateTime);
        var db = new FakeDb(ticket) { FailOptimisticConcurrencyTimes = 2 };
        var handler = CreateHandler(db, new ConcurrencyAwareClassifier());

        await Assert.ThrowsAsync<OptimisticConcurrencyException>(() =>
            handler.HandleAsync(
                new AppendCustomerReplyCommand(
                    IdempotencyKey: "<reply-double-conflict@test>",
                    SourceMessageId: "<reply-double-conflict@test>",
                    TicketNumber: "VS-000088",
                    Content: "Body",
                    FromAddress: "ada@example.test"),
                CancellationToken.None));
    }

    [Fact]
    public async Task BR010_ResolvedTicket_SetsWasReopened()
    {
        var ticket = Ticket.Create("VS-000081", "Subject", "Ada", "ada@example.test", FixedNow.UtcDateTime);
        ticket.ResolveManually(
            FixedNow.UtcDateTime.AddHours(-1),
            Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var db = new FakeDb(ticket);
        var handler = CreateHandler(db);

        var result = await handler.HandleAsync(
            new AppendCustomerReplyCommand(
                IdempotencyKey: "<reopen@test>",
                SourceMessageId: "<reopen@test>",
                TicketNumber: "VS-000081",
                Content: "Back",
                FromAddress: "ada@example.test"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.WasReopened);
        Assert.Equal(TicketStatus.CustomerReplied, ticket.Status);
        Assert.Null(ticket.ResolvedAt);
    }

    [Fact]
    public async Task SameMessageId_IsIdempotent_NoSecondMessage()
    {
        var ticket = Ticket.Create("VS-000082", "Subject", "Ada", "ada@example.test", FixedNow.UtcDateTime);
        var db = new FakeDb(ticket);
        var handler = CreateHandler(db);
        var command = new AppendCustomerReplyCommand(
            IdempotencyKey: "<dup@test>",
            SourceMessageId: "<dup@test>",
            TicketNumber: "VS-000082",
            Content: "First",
            FromAddress: "ada@example.test");

        var first = await handler.HandleAsync(command, CancellationToken.None);
        var second = await handler.HandleAsync(command with { Content = "Second" }, CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.True(second.Value!.WasAlreadyProcessed);
        Assert.Single(db.Messages);
        Assert.Equal("First", db.Messages[0].Content);
    }

    [Fact]
    public async Task UnknownTicketNumber_ReturnsFailure()
    {
        var handler = CreateHandler(new FakeDb());

        var result = await handler.HandleAsync(
            new AppendCustomerReplyCommand(
                IdempotencyKey: "<x@test>",
                SourceMessageId: "<x@test>",
                TicketNumber: "VS-000099",
                Content: "Body"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("not found", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FromMismatch_ReturnsFailure()
    {
        var ticket = Ticket.Create("VS-000083", "Subject", "Ada", "ada@example.test", FixedNow.UtcDateTime);
        var handler = CreateHandler(new FakeDb(ticket));

        var result = await handler.HandleAsync(
            new AppendCustomerReplyCommand(
                IdempotencyKey: "<spoof@test>",
                SourceMessageId: "<spoof@test>",
                TicketNumber: "VS-000083",
                Content: "Body",
                FromAddress: "other@example.test"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("does not match", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(TicketStatus.New, ticket.Status);
    }

    private static AppendCustomerReplyHandler CreateHandler(
        FakeDb db,
        IDatabaseErrorClassifier? classifier = null) =>
        new(db, db, db, new FixedTimeProvider(FixedNow), classifier ?? new NeverConflictClassifier());

    private sealed class NeverConflictClassifier : IDatabaseErrorClassifier
    {
        public bool IsProcessedEmailIdempotencyConflict(Exception exception) => false;

        public bool IsOptimisticConcurrencyConflict(Exception exception) => false;
    }

    private sealed class ConcurrencyAwareClassifier : IDatabaseErrorClassifier
    {
        public bool IsProcessedEmailIdempotencyConflict(Exception exception) => false;

        public bool IsOptimisticConcurrencyConflict(Exception exception) =>
            exception is OptimisticConcurrencyException ||
            exception.Message.Contains("optimistic concurrency", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class FakeDb : IApplicationDbContext, ITicketRepository, IProcessedEmailRepository, IUnitOfWork
    {
        private readonly List<Ticket> tickets;
        private readonly List<object> pending = [];

        public int FailOptimisticConcurrencyTimes { get; init; }

        public int SaveChangesCallCount { get; private set; }

        public List<TicketMessage> Messages { get; } = [];
        public List<ProcessedEmailMessage> Processed { get; } = [];

        public FakeDb(params Ticket[] tickets) => this.tickets = tickets.ToList();

        public Task<Ticket?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(tickets.FirstOrDefault(t => t.Id == id));

        public Task<Ticket?> GetByNumberAsync(string ticketNumber, CancellationToken cancellationToken = default) =>
            Task.FromResult(tickets.FirstOrDefault(t => t.TicketNumber == ticketNumber));

        public IQueryable<Ticket> GetListQueryable() => Tickets;

        public Task AddAsync(Ticket ticket, CancellationToken cancellationToken = default)
        {
            Add(ticket);
            return Task.CompletedTask;
        }

        public void Update(Ticket ticket) { }

        public Task AddMessageAsync(TicketMessage message, CancellationToken cancellationToken = default)
        {
            Add(message);
            return Task.CompletedTask;
        }

        public Task<bool> MessageExistsAsync(Guid messageId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Messages.Any(m => m.Id == messageId));

        public Task<TicketMessage?> GetMessageByIdAsync(Guid messageId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Messages.FirstOrDefault(m => m.Id == messageId));

        public Task<Guid> GetFirstMessageIdAsync(Guid ticketId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Messages.Where(m => m.TicketId == ticketId).OrderBy(m => m.CreatedAt).Select(m => m.Id).FirstOrDefault());

        public Task<ProcessedEmailMessage?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(Processed.FirstOrDefault(p => p.IdempotencyKey == idempotencyKey));

        Task<ProcessedEmailMessage?> IProcessedEmailRepository.GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Processed.FirstOrDefault(p => p.Id == id));

        public Task AddAsync(ProcessedEmailMessage message, CancellationToken cancellationToken = default)
        {
            Add(message);
            return Task.CompletedTask;
        }

        IQueryable<ProcessedEmailMessage> IProcessedEmailRepository.GetListQueryable() => ProcessedEmailMessages;

        public IQueryable<User> Users => Array.Empty<User>().AsQueryable();
        public IQueryable<Ticket> Tickets => tickets.AsQueryable();
        public IQueryable<TicketMessage> TicketMessages => Messages.AsQueryable();
        public IQueryable<TicketAttachment> TicketAttachments =>
            Array.Empty<TicketAttachment>().AsQueryable();
        public IQueryable<ProcessedEmailMessage> ProcessedEmailMessages => Processed.AsQueryable();

        public IQueryable<ApplicationParameter> ApplicationParameters =>
            Array.Empty<ApplicationParameter>().AsQueryable();

        public IQueryable<ParameterChangeLog> ParameterChangeLogs =>
            Array.Empty<ParameterChangeLog>().AsQueryable();

        public IQueryable<SystemLog> SystemLogs =>
            Array.Empty<SystemLog>().AsQueryable();

        public void Add<TEntity>(TEntity entity) where TEntity : class => pending.Add(entity!);

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveChangesCallCount++;

            if (SaveChangesCallCount <= FailOptimisticConcurrencyTimes)
            {
                pending.Clear();
                throw new OptimisticConcurrencyException(
                    "Simulated optimistic concurrency conflict.");
            }

            foreach (var entity in pending)
            {
                switch (entity)
                {
                    case TicketMessage message:
                        Messages.Add(message);
                        break;
                    case ProcessedEmailMessage processed:
                        Processed.Add(processed);
                        break;
                }
            }

            pending.Clear();
            return Task.FromResult(1);
        }

        public void ClearTrackedChanges() => pending.Clear();
    }
}
