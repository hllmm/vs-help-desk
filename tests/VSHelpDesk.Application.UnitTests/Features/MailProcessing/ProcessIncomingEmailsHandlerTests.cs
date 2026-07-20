using Microsoft.Extensions.Logging.Abstractions;
using VSHelpDesk.Application.Abstractions.Email;
using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Application.Features.MailProcessing.ProcessIncomingEmails;
using VSHelpDesk.Application.Features.Tickets.CreateTicket;
using VSHelpDesk.Application.Features.Tickets.ReplyToTicket;
using VSHelpDesk.Domain.Entities;
using VSHelpDesk.Domain.Enums;

namespace VSHelpDesk.Application.UnitTests.Features.MailProcessing;

public sealed class ProcessIncomingEmailsHandlerTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 7, 31, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task T1_NewMail_CreatesTicketAndSendsAck()
    {
        var context = new FakeDb();
        var sender = new RecordingSender();
        var receiver = new FakeReceiver(
        [
            Mail("<msg-new@test>", "customer@example.test", "Help please", "My printer is broken.")
        ]);
        var handler = CreateHandler(context, receiver, sender, "VS-000201");

        var result = await handler.HandleAsync(new ProcessIncomingEmailsCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.CreatedTickets);
        Assert.Equal(0, result.Value.CustomerReplies);
        Assert.Equal(1, result.Value.AckSent);
        Assert.Equal(["VS-000201"], result.Value.CreatedTicketNumbers);
        Assert.Single(context.TicketsList);
        Assert.Single(sender.Sent);
    }

    [Fact]
    public async Task T2_BR007_MatchingSubject_AppendsCustomerReply()
    {
        var context = new FakeDb();
        var existing = Ticket.Create("VS-000050", "Original subject", "Prior", "prior@example.test", FixedNow.UtcDateTime);
        existing.MarkAsWaitingCustomerReply(FixedNow.UtcDateTime.AddMinutes(-30));
        context.TicketsList.Add(existing);
        var sender = new RecordingSender();
        var receiver = new FakeReceiver(
        [
            Mail("<msg-reply@test>", "prior@example.test", "Re: [VS-000050] Original subject", "Still broken")
        ]);
        var handler = CreateHandler(context, receiver, sender, "VS-000202");

        var result = await handler.HandleAsync(new ProcessIncomingEmailsCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value!.CreatedTickets);
        Assert.Equal(1, result.Value.CustomerReplies);
        Assert.Equal(0, result.Value.ReopenedTickets);
        Assert.Equal(0, result.Value.AckSent);
        Assert.Single(context.TicketsList);
        Assert.Single(context.TicketMessagesList);
        Assert.Equal(TicketStatus.CustomerReplied, existing.Status);
        Assert.Equal("Original subject", existing.Subject);
        Assert.Equal("Still broken", context.TicketMessagesList[0].Content);
        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task T3_BR010_ResolvedTicket_ReopensOnCustomerReply()
    {
        var context = new FakeDb();
        var existing = Ticket.Create("VS-000060", "Resolved case", "Prior", "prior@example.test", FixedNow.UtcDateTime);
        existing.Resolve(FixedNow.UtcDateTime.AddHours(-2));
        context.TicketsList.Add(existing);
        var sender = new RecordingSender();
        var receiver = new FakeReceiver(
        [
            Mail("<msg-reopen@test>", "prior@example.test", "[VS-000060] Re: Resolved case", "It broke again")
        ]);
        var handler = CreateHandler(context, receiver, sender, "VS-000203");

        var result = await handler.HandleAsync(new ProcessIncomingEmailsCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.CustomerReplies);
        Assert.Equal(1, result.Value.ReopenedTickets);
        Assert.Equal(TicketStatus.CustomerReplied, existing.Status);
        Assert.Null(existing.ResolvedAt);
        Assert.Single(context.TicketMessagesList);
        Assert.Equal("Resolved case", existing.Subject);
    }

    [Fact]
    public async Task T4_DuplicateMessageId_DoesNotAddSecondMessage()
    {
        var context = new FakeDb();
        var sender = new RecordingSender();
        var mail = Mail("<msg-dup@test>", "customer@example.test", "Dup", "Body");
        var receiver = new FakeReceiver([mail]);
        var handler = CreateHandler(context, receiver, sender, "VS-000204", "VS-000205");

        var first = await handler.HandleAsync(new ProcessIncomingEmailsCommand(), CancellationToken.None);
        receiver.ResetMarked();
        var second = await handler.HandleAsync(new ProcessIncomingEmailsCommand(), CancellationToken.None);

        Assert.Equal(1, first.Value!.CreatedTickets);
        Assert.Equal(1, first.Value.AckSent);
        Assert.Equal(0, second.Value!.CreatedTickets);
        Assert.Equal(1, second.Value.AlreadyProcessed);
        Assert.Equal(0, second.Value.AckSent);
        Assert.Single(context.TicketsList);
        Assert.Single(context.TicketMessagesList);
        Assert.Single(sender.Sent);
    }

    [Fact]
    public async Task InvalidTicketNumberInSubject_CreatesNewTicket()
    {
        var context = new FakeDb();
        var sender = new RecordingSender();
        var receiver = new FakeReceiver(
        [
            Mail("<msg-orphan@test>", "x@example.test", "Re: [VS-999999] ghost", "No such ticket")
        ]);
        var handler = CreateHandler(context, receiver, sender, "VS-000206");

        var result = await handler.HandleAsync(new ProcessIncomingEmailsCommand(), CancellationToken.None);

        Assert.Equal(1, result.Value!.CreatedTickets);
        Assert.Equal(0, result.Value.CustomerReplies);
        Assert.Equal("VS-000206", context.TicketsList[0].TicketNumber);
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
        var reply = new AppendCustomerReplyHandler(context, new FixedTimeProvider(FixedNow));
        return new ProcessIncomingEmailsHandler(
            receiver,
            sender,
            new FixedSettings(),
            context,
            create,
            reply,
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
        public List<EmailMessage> Sent { get; } = [];

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            Sent.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeDb : IApplicationDbContext
    {
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
