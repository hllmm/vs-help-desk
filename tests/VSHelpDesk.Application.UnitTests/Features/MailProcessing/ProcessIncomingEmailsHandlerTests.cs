using Microsoft.Extensions.Logging.Abstractions;
using VSHelpDesk.Application.Abstractions.Email;
using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Application.Features.MailProcessing.ProcessIncomingEmails;
using VSHelpDesk.Application.Features.Tickets.CreateTicket;
using VSHelpDesk.Domain.Entities;
using VSHelpDesk.Domain.Enums;

namespace VSHelpDesk.Application.UnitTests.Features.MailProcessing;

public sealed class ProcessIncomingEmailsHandlerTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 7, 30, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task UC002_NewMail_CreatesTicketAndSendsAckAfterCommit()
    {
        var context = new FakeDb();
        var sender = new RecordingSender();
        var receiver = new FakeReceiver(
        [
            Mail("<msg-new@test>", "customer@example.test", "Help please", "My printer is broken.")
        ]);
        var handler = CreateHandler(context, receiver, sender, "VS-000101");

        var result = await handler.HandleAsync(new ProcessIncomingEmailsCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.FetchedCount);
        Assert.Equal(1, result.Value.CreatedTickets);
        Assert.Equal(0, result.Value.AlreadyProcessed);
        Assert.Equal(1, result.Value.AckSent);
        Assert.Equal(0, result.Value.AckFailed);
        Assert.Equal(["VS-000101"], result.Value.CreatedTicketNumbers);
        Assert.Single(context.TicketsList);
        Assert.Single(context.TicketMessagesList);
        Assert.Single(context.ProcessedEmailMessagesList);
        Assert.Single(sender.Sent);
        Assert.Contains("VS-000101", sender.Sent[0].Subject, StringComparison.Ordinal);
        Assert.Equal("customer@example.test", sender.Sent[0].ToAddress);
        Assert.Contains("<msg-new@test>", receiver.Marked);
    }

    [Fact]
    public async Task UC002_SameMessageId_SecondRun_DoesNotCreateOrAckAgain()
    {
        var context = new FakeDb();
        var sender = new RecordingSender();
        var mail = Mail("<msg-dup@test>", "customer@example.test", "Dup", "Body");
        var receiver = new FakeReceiver([mail]);
        var handler = CreateHandler(context, receiver, sender, "VS-000102", "VS-000103");

        var first = await handler.HandleAsync(new ProcessIncomingEmailsCommand(), CancellationToken.None);
        // Second fetch returns same MessageId (receiver not cleared) but CreateTicket short-circuits.
        receiver.ResetMarked();
        var second = await handler.HandleAsync(new ProcessIncomingEmailsCommand(), CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.Equal(1, first.Value!.CreatedTickets);
        Assert.Equal(1, first.Value.AckSent);

        Assert.True(second.IsSuccess);
        Assert.Equal(0, second.Value!.CreatedTickets);
        Assert.Equal(1, second.Value.AlreadyProcessed);
        Assert.Equal(0, second.Value.AckSent);
        Assert.Single(context.TicketsList);
        Assert.Single(context.TicketMessagesList);
        Assert.Single(context.ProcessedEmailMessagesList);
        Assert.Single(sender.Sent);
    }

    [Fact]
    public async Task UC002_AckFailureAfterCommit_KeepsTicketAndCountsAckFailed()
    {
        var context = new FakeDb();
        var sender = new RecordingSender { ThrowOnSend = true };
        var receiver = new FakeReceiver(
        [
            Mail("<msg-ack-fail@test>", "customer@example.test", "Ack fail", "Body")
        ]);
        var handler = CreateHandler(context, receiver, sender, "VS-000104");

        var result = await handler.HandleAsync(new ProcessIncomingEmailsCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.CreatedTickets);
        Assert.Equal(0, result.Value.AckSent);
        Assert.Equal(1, result.Value.AckFailed);
        Assert.Single(context.TicketsList);
        Assert.Single(context.TicketMessagesList);
        Assert.Single(context.ProcessedEmailMessagesList);
    }

    [Fact]
    public async Task UC002_SubjectMatchesExistingTicket_SkipsCreateForDay10()
    {
        var context = new FakeDb();
        var existing = Ticket.Create(
            "VS-000050",
            "Existing",
            "Prior",
            "prior@example.test",
            FixedNow.UtcDateTime);
        context.TicketsList.Add(existing);
        var sender = new RecordingSender();
        var receiver = new FakeReceiver(
        [
            Mail("<msg-match@test>", "customer@example.test", "Re: [VS-000050] Existing", "Follow up")
        ]);
        var handler = CreateHandler(context, receiver, sender, "VS-000105");

        var result = await handler.HandleAsync(new ProcessIncomingEmailsCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.MatchedExistingSkipped);
        Assert.Equal(0, result.Value.CreatedTickets);
        Assert.Equal(0, result.Value.AckSent);
        Assert.Single(context.TicketsList);
        Assert.Empty(sender.Sent);
    }

    private static ProcessIncomingEmailsHandler CreateHandler(
        FakeDb context,
        IEmailReceiver receiver,
        IEmailSender sender,
        params string[] numbers)
    {
        var create = new CreateTicketHandler(
            context,
            new SequenceNumbers(numbers),
            new FixedTimeProvider(FixedNow));
        return new ProcessIncomingEmailsHandler(
            receiver,
            sender,
            new FixedSettings(),
            context,
            create,
            NullLogger<ProcessIncomingEmailsHandler>.Instance);
    }

    private static IncomingEmail Mail(string id, string from, string subject, string body) =>
        new(id, from, "Customer", subject, body, false, FixedNow.UtcDateTime, Array.Empty<IncomingEmailAttachment>());

    private sealed class FixedSettings : IEmailBoundarySettings
    {
        public string ReceiverMode => "Fake";
        public bool SendSmtpProbeOnProcessJob => false;
        public string SupportMailboxAddress => "support@vshelpdesk.local";
        public string SupportMailboxDisplayName => "VS Help Desk";
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class SequenceNumbers(params string[] numbers) : ITicketNumberGenerator
    {
        private int index;

        public Task<string> NextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(numbers[index++]);
    }

    private sealed class FakeReceiver(IReadOnlyList<IncomingEmail> messages) : IEmailReceiver
    {
        public List<string> Marked { get; } = [];

        public Task<IReadOnlyList<IncomingEmail>> FetchUnreadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(messages);

        public Task MarkAsProcessedAsync(string messageId, CancellationToken cancellationToken = default)
        {
            Marked.Add(messageId);
            return Task.CompletedTask;
        }

        public void ResetMarked() => Marked.Clear();
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

    private sealed class FakeDb : IApplicationDbContext
    {
        public List<User> UsersList { get; } = [];
        public List<Ticket> TicketsList { get; } = [];
        public List<TicketMessage> TicketMessagesList { get; } = [];
        public List<ProcessedEmailMessage> ProcessedEmailMessagesList { get; } = [];
        private readonly List<object> pending = [];

        public IQueryable<User> Users => UsersList.AsQueryable();
        public IQueryable<Ticket> Tickets => TicketsList.AsQueryable();
        public IQueryable<TicketMessage> TicketMessages => TicketMessagesList.AsQueryable();
        public IQueryable<ProcessedEmailMessage> ProcessedEmailMessages =>
            ProcessedEmailMessagesList.AsQueryable();

        public void Add<TEntity>(TEntity entity) where TEntity : class => pending.Add(entity!);

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
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
                }
            }

            pending.Clear();
            return Task.FromResult(1);
        }
    }
}
