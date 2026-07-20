using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Application.Features.Tickets.CreateTicket;
using VSHelpDesk.Domain.Entities;
using VSHelpDesk.Domain.Enums;
using VSHelpDesk.Domain.Tickets;

namespace VSHelpDesk.Application.UnitTests.Features.Tickets.CreateTicket;

public sealed class CreateTicketHandlerTests
{
    private static readonly DateTimeOffset CreateTime = new(2026, 7, 28, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task UC002_NewMessageId_CreatesTicketFirstCustomerMessageAndProcessedEmail()
    {
        var context = new FakeApplicationDbContext();
        var numbers = new FakeTicketNumberGenerator("VS-000007");
        var handler = CreateHandler(context, numbers);

        var result = await handler.HandleAsync(
            new CreateTicketCommand(
                IdempotencyKey: "<msg-new-001@example.test>",
                SourceMessageId: "<msg-new-001@example.test>",
                Subject: "Cannot print",
                CustomerName: "Ada",
                CustomerEmail: "ada@example.test",
                Content: "Printer jam"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.WasAlreadyProcessed);
        Assert.Equal("VS-000007", result.Value.TicketNumber);
        Assert.True(TicketNumberFormat.IsCanonical(result.Value.TicketNumber));
        Assert.Equal(1, context.SaveChangesCallCount);
        Assert.Equal(1, numbers.NextCallCount);

        var ticket = Assert.Single(context.TicketsList);
        var message = Assert.Single(context.TicketMessagesList);
        var processed = Assert.Single(context.ProcessedEmailMessagesList);

        Assert.Equal(TicketStatus.New, ticket.Status);
        Assert.Equal("Cannot print", ticket.Subject);
        Assert.Equal(CreateTime.UtcDateTime, ticket.LastActivityAt);
        Assert.Equal(ticket.Id, message.TicketId);
        Assert.Equal(MessageSenderType.Customer, message.SenderType);
        Assert.Equal("Printer jam", message.Content);
        Assert.Equal(CreateTime.UtcDateTime, message.CreatedAt);
        Assert.Equal("<msg-new-001@example.test>", processed.IdempotencyKey);
        Assert.Equal(ticket.Id, processed.TicketId);
        Assert.Equal(result.Value.FirstTicketMessageId, message.Id);
        Assert.Equal(result.Value.ProcessedEmailMessageId, processed.Id);
    }

    [Fact]
    public async Task Create_PersistsCreatedTicketDispositionAndPendingAck()
    {
        var context = new FakeApplicationDbContext();
        var handler = CreateHandler(context, new FakeTicketNumberGenerator("VS-000100"));

        var result = await handler.HandleAsync(
            new CreateTicketCommand(
                IdempotencyKey: "<msg-disp@example.test>",
                SourceMessageId: "<msg-disp@example.test>",
                Subject: "Disposition",
                CustomerName: "Ada",
                CustomerEmail: "ada@example.test",
                Content: "Body"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var processed = Assert.Single(context.ProcessedEmailMessagesList);
        Assert.Equal(ProcessedEmailDisposition.CreatedTicket, processed.Disposition);
        Assert.Equal(AcknowledgementStatus.Pending, processed.AcknowledgementStatus);
        Assert.Equal(CreateTime.UtcDateTime, processed.AcknowledgementNextAttemptAt);
        Assert.Equal(result.Value!.ProcessedEmailMessageId, processed.Id);
    }

    [Fact]
    public async Task Create_PreservesDifferentSourceAndIdempotencyValues()
    {
        var context = new FakeApplicationDbContext();
        var handler = CreateHandler(context, new FakeTicketNumberGenerator("VS-000101"));

        var result = await handler.HandleAsync(
            new CreateTicketCommand(
                IdempotencyKey: "receipt:fake:abc123",
                SourceMessageId: "<original-msg@example.test>",
                Subject: "Subject",
                CustomerName: "Ada",
                CustomerEmail: "ada@example.test",
                Content: "Body"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var processed = Assert.Single(context.ProcessedEmailMessagesList);
        Assert.Equal("receipt:fake:abc123", processed.IdempotencyKey);
        Assert.Equal("<original-msg@example.test>", processed.SourceMessageId);
        Assert.NotEqual(processed.IdempotencyKey, processed.SourceMessageId);
    }

    [Fact]
    public async Task UC002_SameMessageId_SecondHandle_DoesNotCreateAnotherTicketOrMessage()
    {
        var context = new FakeApplicationDbContext();
        var numbers = new FakeTicketNumberGenerator("VS-000008", "VS-000009");
        var handler = CreateHandler(context, numbers);
        var command = new CreateTicketCommand(
            IdempotencyKey: "<msg-dup-001@example.test>",
            SourceMessageId: "<msg-dup-001@example.test>",
            Subject: "Duplicate mail",
            CustomerName: "Ada",
            CustomerEmail: "ada@example.test",
            Content: "First body");

        var first = await handler.HandleAsync(command, CancellationToken.None);
        var second = await handler.HandleAsync(command with { Content = "Should not persist" }, CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.False(first.Value!.WasAlreadyProcessed);
        Assert.True(second.IsSuccess);
        Assert.True(second.Value!.WasAlreadyProcessed);
        Assert.Equal(first.Value.TicketId, second.Value.TicketId);
        Assert.Equal(first.Value.TicketNumber, second.Value.TicketNumber);
        Assert.Equal(first.Value.FirstTicketMessageId, second.Value.FirstTicketMessageId);
        Assert.Equal(first.Value.ProcessedEmailMessageId, second.Value.ProcessedEmailMessageId);
        Assert.NotEqual(Guid.Empty, second.Value.FirstTicketMessageId);

        Assert.Single(context.TicketsList);
        Assert.Single(context.TicketMessagesList);
        Assert.Single(context.ProcessedEmailMessagesList);
        Assert.Equal(1, numbers.NextCallCount);
        Assert.Equal(1, context.SaveChangesCallCount);
        Assert.Equal("First body", Assert.Single(context.TicketMessagesList).Content);
    }

    [Fact]
    public async Task UC002_EmptyMessageId_ReturnsValidationFailure()
    {
        var handler = CreateHandler(
            new FakeApplicationDbContext(),
            new FakeTicketNumberGenerator("VS-000012"));

        var result = await handler.HandleAsync(
            new CreateTicketCommand("  ", null, "S", "Ada", "ada@example.test", "Body"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("IdempotencyKey", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UC002_SaveChangesUniqueRace_ReturnsAlreadyProcessed()
    {
        var context = new FakeApplicationDbContext { SimulateConcurrentIdempotencyWinner = true };
        var numbers = new FakeTicketNumberGenerator("VS-000013", "VS-000014");
        var handler = CreateHandler(
            context,
            numbers,
            new FakeDatabaseErrorClassifier { TreatIdempotencyRaceAsConflict = true });
        var command = new CreateTicketCommand(
            IdempotencyKey: "<msg-race@example.test>",
            SourceMessageId: "<msg-race@example.test>",
            Subject: "Race",
            CustomerName: "Ada",
            CustomerEmail: "ada@example.test",
            Content: "Body");

        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.WasAlreadyProcessed);
        Assert.NotEqual(Guid.Empty, result.Value.TicketId);
        Assert.Equal("VS-WINNER", result.Value.TicketNumber);
        Assert.NotEqual(Guid.Empty, result.Value.FirstTicketMessageId);
        Assert.NotEqual(Guid.Empty, result.Value.ProcessedEmailMessageId);
        Assert.Equal(1, numbers.NextCallCount);
        // Loser pending inserts discarded; only concurrent winner remains.
        Assert.Single(context.TicketsList);
        Assert.Single(context.TicketMessagesList);
        Assert.Single(context.ProcessedEmailMessagesList);
    }

    [Fact]
    public async Task GenericInvalidOperation_IsNotTreatedAsIdempotencyRace()
    {
        var context = new FakeApplicationDbContext { ThrowGenericInvalidOperation = true };
        var handler = CreateHandler(context, new FakeTicketNumberGenerator("VS-000015"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.HandleAsync(
                new CreateTicketCommand(
                    IdempotencyKey: "<msg-generic@example.test>",
                    SourceMessageId: "<msg-generic@example.test>",
                    Subject: "S",
                    CustomerName: "Ada",
                    CustomerEmail: "ada@example.test",
                    Content: "Body"),
                CancellationToken.None));
    }

    [Fact]
    public async Task WrongUniqueConstraint_IsNotTreatedAsIdempotencyRace()
    {
        var context = new FakeApplicationDbContext { ThrowWrongUniqueConstraint = true };
        // Classifier that only accepts the processed-email index name — wrong constraint must propagate.
        var handler = CreateHandler(
            context,
            new FakeTicketNumberGenerator("VS-000016"),
            new FakeDatabaseErrorClassifier { TreatIdempotencyRaceAsConflict = true });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.HandleAsync(
                new CreateTicketCommand(
                    IdempotencyKey: "<msg-wrong-ux@example.test>",
                    SourceMessageId: "<msg-wrong-ux@example.test>",
                    Subject: "S",
                    CustomerName: "Ada",
                    CustomerEmail: "ada@example.test",
                    Content: "Body"),
                CancellationToken.None));

        Assert.Contains("IX_Tickets_TicketNumber", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConcurrentNumberAllocation_ReliesOnUniqueGeneratorValues()
    {
        // Last-line DB unique index is Day 6; generator must still issue distinct VS-###### values.
        var numbers = new FakeTicketNumberGenerator("VS-000010", "VS-000011");
        var first = await numbers.NextAsync();
        var second = await numbers.NextAsync();

        Assert.NotEqual(first, second);
        Assert.True(TicketNumberFormat.IsCanonical(first));
        Assert.True(TicketNumberFormat.IsCanonical(second));
    }

    private static CreateTicketHandler CreateHandler(
        FakeApplicationDbContext context,
        FakeTicketNumberGenerator numbers,
        IDatabaseErrorClassifier? classifier = null) =>
        new(
            context,
            numbers,
            new FixedTimeProvider(CreateTime),
            classifier ?? new FakeDatabaseErrorClassifier());

    private sealed class FakeDatabaseErrorClassifier : IDatabaseErrorClassifier
    {
        public bool TreatIdempotencyRaceAsConflict { get; init; }

        public bool IsProcessedEmailIdempotencyConflict(Exception exception) =>
            TreatIdempotencyRaceAsConflict &&
            exception.Message.Contains(
                "UX_ProcessedEmailMessages_IdempotencyKey",
                StringComparison.Ordinal);

        public bool IsOptimisticConcurrencyConflict(Exception exception) => false;
    }

    private sealed class FakeApplicationDbContext : IApplicationDbContext
    {
        public int SaveChangesCallCount { get; private set; }

        public bool SimulateConcurrentIdempotencyWinner { get; init; }

        public bool ThrowGenericInvalidOperation { get; init; }

        public bool ThrowWrongUniqueConstraint { get; init; }

        public List<User> UsersList { get; } = [];
        public List<Ticket> TicketsList { get; } = [];
        public List<TicketMessage> TicketMessagesList { get; } = [];
        public List<TicketAttachment> TicketAttachmentsList { get; } = [];
        public List<ProcessedEmailMessage> ProcessedEmailMessagesList { get; } = [];

        private readonly List<object> pending = [];

        public IQueryable<User> Users => UsersList.AsQueryable();
        public IQueryable<Ticket> Tickets => TicketsList.AsQueryable();
        public IQueryable<TicketMessage> TicketMessages => TicketMessagesList.AsQueryable();
        public IQueryable<TicketAttachment> TicketAttachments => TicketAttachmentsList.AsQueryable();
        public IQueryable<ProcessedEmailMessage> ProcessedEmailMessages =>
            ProcessedEmailMessagesList.AsQueryable();

        public IQueryable<ApplicationParameter> ApplicationParameters =>
            Array.Empty<ApplicationParameter>().AsQueryable();

        public void Add<TEntity>(TEntity entity) where TEntity : class
        {
            pending.Add(entity!);
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveChangesCallCount++;

            if (ThrowGenericInvalidOperation)
            {
                pending.Clear();
                throw new InvalidOperationException("Simulated non-idempotency failure.");
            }

            if (ThrowWrongUniqueConstraint)
            {
                pending.Clear();
                throw new InvalidOperationException(
                    "Simulated IX_Tickets_TicketNumber unique violation.");
            }

            if (SimulateConcurrentIdempotencyWinner &&
                pending.OfType<ProcessedEmailMessage>().FirstOrDefault() is { } racing)
            {
                var winnerTicket = Ticket.Create(
                    "VS-WINNER",
                    "Race",
                    "Ada",
                    "ada@example.test",
                    CreateTime.UtcDateTime);
                var winnerMessage = new TicketMessage(
                    winnerTicket.Id,
                    MessageSenderType.Customer,
                    "Body",
                    createdAtUtc: CreateTime.UtcDateTime);
                var winnerProcessed = ProcessedEmailMessage.ForCreatedTicket(
                    racing.IdempotencyKey,
                    racing.SourceMessageId,
                    CreateTime.UtcDateTime,
                    winnerTicket.Id);
                TicketsList.Add(winnerTicket);
                TicketMessagesList.Add(winnerMessage);
                ProcessedEmailMessagesList.Add(winnerProcessed);
                pending.Clear();
                throw new InvalidOperationException(
                    "Simulated UX_ProcessedEmailMessages_IdempotencyKey unique violation.");
            }

            foreach (var entity in pending)
            {
                switch (entity)
                {
                    case Ticket ticket:
                        TicketsList.Add(ticket);
                        break;
                    case TicketMessage message:
                        TicketMessagesList.Add(message);
                        break;
                    case ProcessedEmailMessage processed:
                        ProcessedEmailMessagesList.Add(processed);
                        break;
                    case User user:
                        UsersList.Add(user);
                        break;
                    default:
                        throw new InvalidOperationException($"Unexpected entity type {entity.GetType().Name}.");
                }
            }

            pending.Clear();
            return Task.FromResult(1);
        }

        public void ClearTrackedChanges() => pending.Clear();
    }

    private sealed class FakeTicketNumberGenerator(params string[] numbers) : ITicketNumberGenerator
    {
        private int index;

        public int NextCallCount { get; private set; }

        public Task<string> NextAsync(CancellationToken cancellationToken = default)
        {
            NextCallCount++;
            if (index >= numbers.Length)
            {
                throw new InvalidOperationException("No more fake ticket numbers configured.");
            }

            return Task.FromResult(numbers[index++]);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
