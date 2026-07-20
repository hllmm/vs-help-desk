using Microsoft.Extensions.Logging.Abstractions;
using VSHelpDesk.Application.Abstractions.Email;
using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Application.Features.MailProcessing;
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
    public async Task ProcessJob_AckSuccess_RecordsSent()
    {
        var context = new FakeDb();
        var sender = new RecordingSender();
        var receiver = new FakeReceiver(
        [
            Mail("<msg-ack-ok@test>", "customer@example.test", "Help", "Body")
        ]);
        var handler = CreateHandler(context, receiver, sender, "VS-000220");

        var result = await handler.HandleAsync(new ProcessIncomingEmailsCommand(), CancellationToken.None);

        Assert.Equal(1, result.Value!.AckSent);
        var processed = Assert.Single(context.ProcessedEmailMessagesList);
        Assert.Equal(AcknowledgementStatus.Sent, processed.AcknowledgementStatus);
        Assert.Equal(FixedNow.UtcDateTime, processed.AcknowledgementSentAt);
        Assert.Null(processed.AcknowledgementNextAttemptAt);
        Assert.Null(processed.AcknowledgementLastError);
    }

    [Fact]
    public async Task ProcessJob_AckFailure_RecordsSafeErrorAndNextDue()
    {
        var context = new FakeDb();
        var sender = new RecordingSender
        {
            ThrowOnSend = true,
            ExceptionMessage = "SMTP unavailable: secret=password"
        };
        var receiver = new FakeReceiver(
        [
            Mail("<msg-ack-fail-safe@test>", "customer@example.test", "Help", "Body")
        ]);
        var handler = CreateHandler(context, receiver, sender, "VS-000221");

        var result = await handler.HandleAsync(new ProcessIncomingEmailsCommand(), CancellationToken.None);

        Assert.Equal(1, result.Value!.AckFailed);
        var processed = Assert.Single(context.ProcessedEmailMessagesList);
        Assert.Equal(AcknowledgementStatus.Failed, processed.AcknowledgementStatus);
        Assert.Equal("SMTP acknowledgement failed.", processed.AcknowledgementLastError);
        Assert.Equal(FixedNow.UtcDateTime.AddMinutes(1), processed.AcknowledgementNextAttemptAt);
        Assert.DoesNotContain("password", processed.AcknowledgementLastError, StringComparison.Ordinal);
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

    [Fact]
    public async Task T1_BR002_AckSmtpFailure_AfterCreate_KeepsTicketAndIncrementsAckFailed()
    {
        var context = new FakeDb();
        var sender = new RecordingSender { ThrowOnSend = true };
        var receiver = new FakeReceiver(
        [
            Mail("<msg-ack-fail@test>", "customer@example.test", "Help", "Body")
        ]);
        var handler = CreateHandler(context, receiver, sender, "VS-000210");

        var result = await handler.HandleAsync(new ProcessIncomingEmailsCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.CreatedTickets);
        Assert.Equal(0, result.Value.AckSent);
        Assert.Equal(1, result.Value.AckFailed);
        Assert.Single(context.TicketsList);
        Assert.Single(context.TicketMessagesList);
        Assert.Single(context.ProcessedEmailMessagesList);
        Assert.Contains(receiver.Marked, handle => handle.Value == "fake\0<msg-ack-fail@test>");
    }

    [Fact]
    public async Task T1_NewMail_EmptyBody_CreatesTicketWithPlaceholder()
    {
        var context = new FakeDb();
        var sender = new RecordingSender();
        var receiver = new FakeReceiver(
        [
            Mail("<msg-empty@test>", "customer@example.test", "Empty body mail", "   ")
        ]);
        var handler = CreateHandler(context, receiver, sender, "VS-000211");

        var result = await handler.HandleAsync(new ProcessIncomingEmailsCommand(), CancellationToken.None);

        Assert.Equal(1, result.Value!.CreatedTickets);
        Assert.Equal(0, result.Value.SkippedInvalid);
        Assert.Equal(InboundMailLimits.EmptyBodyPlaceholder, context.TicketMessagesList[0].Content);
        Assert.False(context.TicketMessagesList[0].IsHtml);
    }

    [Fact]
    public async Task FromMismatch_OnSubjectMatch_CreatesNewTicketInsteadOfReply()
    {
        var context = new FakeDb();
        var existing = Ticket.Create(
            "VS-000070",
            "Owned by Ada",
            "Ada",
            "ada@example.test",
            FixedNow.UtcDateTime);
        context.TicketsList.Add(existing);
        var sender = new RecordingSender();
        var receiver = new FakeReceiver(
        [
            Mail("<msg-spoof@test>", "attacker@evil.test", "Re: [VS-000070] Owned by Ada", "Inject")
        ]);
        var handler = CreateHandler(context, receiver, sender, "VS-000212");

        var result = await handler.HandleAsync(new ProcessIncomingEmailsCommand(), CancellationToken.None);

        Assert.Equal(1, result.Value!.CreatedTickets);
        Assert.Equal(0, result.Value.CustomerReplies);
        Assert.Equal(2, context.TicketsList.Count);
        Assert.Equal("attacker@evil.test", context.TicketsList[1].CustomerEmail);
    }

    [Fact]
    public async Task T1_NewMail_AckSubjectContainsCanonicalTicketNumber_AndToIsCustomer()
    {
        var context = new FakeDb();
        var sender = new RecordingSender();
        var receiver = new FakeReceiver(
        [
            Mail("<msg-ack-subject@test>", "customer@example.test", "Help", "Body")
        ]);
        var handler = CreateHandler(context, receiver, sender, "VS-000213");

        var result = await handler.HandleAsync(new ProcessIncomingEmailsCommand(), CancellationToken.None);

        Assert.Equal(1, result.Value!.AckSent);
        Assert.Single(sender.Sent);
        Assert.Contains("VS-000213", sender.Sent[0].Subject, StringComparison.Ordinal);
        Assert.Equal("customer@example.test", sender.Sent[0].ToAddress);
        Assert.Equal(TicketStatus.New, context.TicketsList[0].Status);
    }

    private static ProcessIncomingEmailsHandler CreateHandler(
        FakeDb context,
        IEmailReceiver receiver,
        IEmailSender sender,
        params string[] numbers)
    {
        var time = new FixedTimeProvider(FixedNow);
        var classifier = new NeverConflictClassifier();
        var create = new CreateTicketHandler(context, new SequenceNumbers(numbers), time, classifier);
        var reply = new AppendCustomerReplyHandler(context, time, classifier);
        return new ProcessIncomingEmailsHandler(
            receiver,
            sender,
            new FixedSettings(),
            context,
            create,
            reply,
            time,
            new AlwaysEnterGate(),
            NullLogger<ProcessIncomingEmailsHandler>.Instance);
    }

    private sealed class NeverConflictClassifier : IDatabaseErrorClassifier
    {
        public bool IsProcessedEmailIdempotencyConflict(Exception exception) => false;

        public bool IsOptimisticConcurrencyConflict(Exception exception) => false;
    }

    private sealed class AlwaysEnterGate : IProcessIncomingEmailsGate
    {
        public Task<bool> TryEnterAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public void Exit()
        {
        }
    }

    private static IncomingEmail Mail(string? id, string from, string subject, string body) =>
        new(
            MessageId: id,
            ReceiptHandle: new EmailReceiptHandle(EmailReceiptKind.Fake, $"fake\0{id ?? "null-id"}"),
            FromAddress: from,
            FromDisplayName: "Customer",
            Subject: subject,
            Body: body,
            IsHtml: false,
            ReceivedAt: FixedNow.UtcDateTime,
            Attachments: Array.Empty<IncomingEmailAttachment>());

    private sealed class FixedSettings : IEmailBoundarySettings
    {
        public string ReceiverMode => "Fake";
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
        public List<EmailReceiptHandle> Marked { get; } = [];

        public Task<IReadOnlyList<IncomingEmail>> FetchUnreadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(messages);

        public Task MarkAsProcessedAsync(
            EmailReceiptHandle receiptHandle,
            CancellationToken cancellationToken = default)
        {
            Marked.Add(receiptHandle);
            return Task.CompletedTask;
        }

        public void ResetMarked() => Marked.Clear();
    }

    private sealed class RecordingSender : IEmailSender
    {
        public bool ThrowOnSend { get; init; }
        public string ExceptionMessage { get; init; } = "SMTP down";
        public List<EmailMessage> Sent { get; } = [];

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            if (ThrowOnSend)
            {
                throw new InvalidOperationException(ExceptionMessage);
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

        public void ClearTrackedChanges() => pending.Clear();
    }
}
