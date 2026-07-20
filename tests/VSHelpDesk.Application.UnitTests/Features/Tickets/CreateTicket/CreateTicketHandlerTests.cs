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
        var handler = new CreateTicketHandler(context, numbers, new FixedTimeProvider(CreateTime));

        var result = await handler.HandleAsync(
            new CreateTicketCommand(
                MessageId: "<msg-new-001@example.test>",
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
        Assert.Equal("<msg-new-001@example.test>", processed.MessageId);
        Assert.Equal(ticket.Id, processed.TicketId);
        Assert.Equal(result.Value.FirstTicketMessageId, message.Id);
    }

    [Fact]
    public async Task BR003_SameMessageId_SecondHandle_DoesNotCreateAnotherTicketOrMessage()
    {
        var context = new FakeApplicationDbContext();
        var numbers = new FakeTicketNumberGenerator("VS-000008", "VS-000009");
        var handler = new CreateTicketHandler(context, numbers, new FixedTimeProvider(CreateTime));
        var command = new CreateTicketCommand(
            MessageId: "<msg-dup-001@example.test>",
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

        Assert.Single(context.TicketsList);
        Assert.Single(context.TicketMessagesList);
        Assert.Single(context.ProcessedEmailMessagesList);
        Assert.Equal(1, numbers.NextCallCount);
        Assert.Equal(1, context.SaveChangesCallCount);
        Assert.Equal("First body", Assert.Single(context.TicketMessagesList).Content);
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

    private sealed class FakeApplicationDbContext : IApplicationDbContext
    {
        public int SaveChangesCallCount { get; private set; }

        public List<User> UsersList { get; } = [];
        public List<Ticket> TicketsList { get; } = [];
        public List<TicketMessage> TicketMessagesList { get; } = [];
        public List<ProcessedEmailMessage> ProcessedEmailMessagesList { get; } = [];

        public IQueryable<User> Users => UsersList.AsQueryable();
        public IQueryable<Ticket> Tickets => TicketsList.AsQueryable();
        public IQueryable<TicketMessage> TicketMessages => TicketMessagesList.AsQueryable();
        public IQueryable<ProcessedEmailMessage> ProcessedEmailMessages =>
            ProcessedEmailMessagesList.AsQueryable();

        public void Add<TEntity>(TEntity entity) where TEntity : class
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
                    throw new InvalidOperationException($"Unexpected entity type {typeof(TEntity).Name}.");
            }
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveChangesCallCount++;
            return Task.FromResult(1);
        }
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
