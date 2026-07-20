using Microsoft.Extensions.Logging.Abstractions;
using VSHelpDesk.Application.Abstractions.Authentication;
using VSHelpDesk.Application.Abstractions.Email;
using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Application.Features.Tickets.ReplyToTicket;
using VSHelpDesk.Domain.Entities;
using VSHelpDesk.Domain.Enums;

namespace VSHelpDesk.Application.UnitTests.Features.Tickets.ReplyToTicket;

public sealed class SupportReplyToTicketHandlerTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 4, 11, 0, 0, TimeSpan.Zero);
    private static readonly Guid SupportUserId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    [Fact]
    public async Task UC005_SuccessfulSend_SavesSupportMessageAndWaitsForCustomer()
    {
        var ticket = Ticket.Create("VS-000301", "Printer", "Ada", "ada@example.test", FixedNow.UtcDateTime);
        ticket.MarkAsCustomerReplied(FixedNow.UtcDateTime);
        var db = new FakeDb(ticket);
        var sender = new RecordingSender();
        var handler = CreateHandler(db, sender);

        var result = await handler.HandleAsync(
            new SupportReplyToTicketCommand(ticket.Id, "Please try restarting.", false),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.EmailDelivered);
        Assert.Null(result.Value.EmailDeliveryError);
        Assert.Equal(nameof(TicketStatus.WaitingCustomerReply), result.Value.Status);
        Assert.Equal(TicketStatus.WaitingCustomerReply, ticket.Status);
        Assert.NotNull(ticket.WaitingCustomerSince);
        Assert.Single(db.Messages);
        Assert.Equal(MessageSenderType.Support, db.Messages[0].SenderType);
        Assert.Equal(SupportUserId, db.Messages[0].UserId);
        Assert.Single(sender.Sent);
        Assert.Contains("VS-000301", sender.Sent[0].Subject, StringComparison.Ordinal);
        Assert.Equal("ada@example.test", sender.Sent[0].ToAddress);
    }

    [Fact]
    public async Task BR022_SmtpFailure_KeepsMessageAndDoesNotChangeToWaiting()
    {
        var ticket = Ticket.Create("VS-000302", "VPN", "Bob", "bob@example.test", FixedNow.UtcDateTime);
        ticket.MarkAsCustomerReplied(FixedNow.UtcDateTime);
        var db = new FakeDb(ticket);
        var sender = new RecordingSender { ThrowOnSend = true };
        var handler = CreateHandler(db, sender);

        var result = await handler.HandleAsync(
            new SupportReplyToTicketCommand(ticket.Id, "We enabled VPN.", false),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.EmailDelivered);
        Assert.Contains("saved", result.Value.EmailDeliveryError, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(TicketStatus.CustomerReplied, ticket.Status);
        Assert.Single(db.Messages);
        Assert.Equal(MessageSenderType.Support, db.Messages[0].SenderType);
    }

    [Fact]
    public async Task UC005_EmptyContent_ReturnsFailure()
    {
        var ticket = Ticket.Create("VS-000303", "X", "C", "c@t.com", FixedNow.UtcDateTime);
        var handler = CreateHandler(new FakeDb(ticket), new RecordingSender());

        var result = await handler.HandleAsync(
            new SupportReplyToTicketCommand(ticket.Id, "  ", false),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("Content", result.Error, StringComparison.Ordinal);
    }

    private static SupportReplyToTicketHandler CreateHandler(FakeDb db, IEmailSender sender) =>
        new(
            db,
            sender,
            new FixedCurrentUser(),
            new FixedTimeProvider(FixedNow),
            NullLogger<SupportReplyToTicketHandler>.Instance);

    private sealed class FixedCurrentUser : ICurrentUserService
    {
        public Guid? UserId => SupportUserId;
        public bool IsAuthenticated => true;
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class RecordingSender : IEmailSender
    {
        public bool ThrowOnSend { get; init; }
        public List<EmailMessage> Sent { get; } = [];

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            if (ThrowOnSend)
            {
                throw new InvalidOperationException("SMTP down");
            }

            Sent.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeDb(Ticket ticket) : IApplicationDbContext
    {
        public List<TicketMessage> Messages { get; } = [];
        private readonly List<object> pending = [];

        public IQueryable<User> Users => Array.Empty<User>().AsQueryable();
        public IQueryable<Ticket> Tickets => new[] { ticket }.AsQueryable();
        public IQueryable<TicketMessage> TicketMessages => Messages.AsQueryable();
        public IQueryable<TicketAttachment> TicketAttachments =>
            Array.Empty<TicketAttachment>().AsQueryable();

        public IQueryable<ProcessedEmailMessage> ProcessedEmailMessages =>
            Array.Empty<ProcessedEmailMessage>().AsQueryable();

        public void Add<TEntity>(TEntity entity) where TEntity : class => pending.Add(entity!);

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            foreach (var entity in pending)
            {
                if (entity is TicketMessage message)
                {
                    Messages.Add(message);
                }
            }

            pending.Clear();
            return Task.FromResult(1);
        }

        public void ClearTrackedChanges() => pending.Clear();

        public bool IsUniqueConstraintViolation(Exception exception) => false;
    }
}
