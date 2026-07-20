using VSHelpDesk.Application.Abstractions.Persistence;
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
                "<reply-1@test>",
                "VS-000080",
                "Still broken",
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
                "<reply-disp@test>",
                "VS-000085",
                "Body",
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
    public async Task BR010_ResolvedTicket_SetsWasReopened()
    {
        var ticket = Ticket.Create("VS-000081", "Subject", "Ada", "ada@example.test", FixedNow.UtcDateTime);
        ticket.Resolve(FixedNow.UtcDateTime.AddHours(-1));
        var db = new FakeDb(ticket);
        var handler = CreateHandler(db);

        var result = await handler.HandleAsync(
            new AppendCustomerReplyCommand("<reopen@test>", "VS-000081", "Back", FromAddress: "ada@example.test"),
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
            "<dup@test>",
            "VS-000082",
            "First",
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
            new AppendCustomerReplyCommand("<x@test>", "VS-000099", "Body"),
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
                "<spoof@test>",
                "VS-000083",
                "Body",
                FromAddress: "other@example.test"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("does not match", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(TicketStatus.New, ticket.Status);
    }

    private static AppendCustomerReplyHandler CreateHandler(FakeDb db) =>
        new(db, new FixedTimeProvider(FixedNow), new NeverConflictClassifier());

    private sealed class NeverConflictClassifier : IDatabaseErrorClassifier
    {
        public bool IsProcessedEmailIdempotencyConflict(Exception exception) => false;

        public bool IsOptimisticConcurrencyConflict(Exception exception) => false;
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class FakeDb : IApplicationDbContext
    {
        private readonly List<Ticket> tickets;
        private readonly List<object> pending = [];

        public List<TicketMessage> Messages { get; } = [];
        public List<ProcessedEmailMessage> Processed { get; } = [];

        public FakeDb(params Ticket[] tickets) => this.tickets = tickets.ToList();

        public IQueryable<User> Users => Array.Empty<User>().AsQueryable();
        public IQueryable<Ticket> Tickets => tickets.AsQueryable();
        public IQueryable<TicketMessage> TicketMessages => Messages.AsQueryable();
        public IQueryable<TicketAttachment> TicketAttachments =>
            Array.Empty<TicketAttachment>().AsQueryable();
        public IQueryable<ProcessedEmailMessage> ProcessedEmailMessages => Processed.AsQueryable();

        public void Add<TEntity>(TEntity entity) where TEntity : class => pending.Add(entity!);

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
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
