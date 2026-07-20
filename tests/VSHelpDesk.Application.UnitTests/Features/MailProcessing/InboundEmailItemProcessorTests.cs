using VSHelpDesk.Application.Abstractions.Email;
using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Application.Features.MailProcessing;
using VSHelpDesk.Application.Features.MailProcessing.Acknowledgements;
using VSHelpDesk.Application.Features.MailProcessing.ProcessIncomingEmails;
using VSHelpDesk.Application.Features.Tickets.CreateTicket;
using VSHelpDesk.Application.Features.Tickets.ReplyToTicket;
using VSHelpDesk.Domain.Entities;
using VSHelpDesk.Domain.Enums;
using Microsoft.Extensions.Logging.Abstractions;

namespace VSHelpDesk.Application.UnitTests.Features.MailProcessing;

public sealed class InboundEmailItemProcessorTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 7, 31, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task MissingMessageId_UsesReceiptKeyAndProcessesOnce()
    {
        var context = new FakeDb();
        var sender = new RecordingSender();
        var processor = CreateProcessor(context, sender, "VS-000401");
        var receipt = new EmailReceiptHandle(EmailReceiptKind.Fake, "fake\0no-msg-id-1");
        var mail = Mail(messageId: null, "customer@example.test", "Help", "Body", receipt);

        var first = await processor.ProcessAsync(mail, CancellationToken.None);
        var second = await processor.ProcessAsync(mail, CancellationToken.None);

        var expectedKey = InboundEmailIdentityFactory.Create(mail).IdempotencyKey;
        Assert.StartsWith("receipt:fake:", expectedKey, StringComparison.Ordinal);
        Assert.Equal(InboundEmailItemOutcome.CreatedTicket, first.Outcome);
        Assert.Equal(expectedKey, first.IdempotencyKey);
        Assert.Equal("VS-000401", first.TicketNumber);
        Assert.True(first.AcknowledgementSent);
        Assert.Equal(InboundEmailItemOutcome.AlreadyProcessed, second.Outcome);
        Assert.Equal(expectedKey, second.IdempotencyKey);
        Assert.Single(context.TicketsList);
        Assert.Single(context.ProcessedEmailMessagesList);
        Assert.Equal(expectedKey, context.ProcessedEmailMessagesList[0].IdempotencyKey);
        Assert.Null(context.ProcessedEmailMessagesList[0].SourceMessageId);
        Assert.Single(sender.Sent);
    }

    [Fact]
    public async Task InvalidSender_IsQuarantinedAndMarkedByReceipt()
    {
        // Processor quarantines; orchestrator owns mark-seen (covered in handler tests).
        var context = new FakeDb();
        var processor = CreateProcessor(context, new RecordingSender(), "VS-000402");
        var mail = Mail(
            "<bad-from@test>",
            from: "not-an-email",
            subject: "Poison",
            body: "x",
            receipt: new EmailReceiptHandle(EmailReceiptKind.Fake, "fake\0bad-from"));

        var result = await processor.ProcessAsync(mail, CancellationToken.None);

        Assert.Equal(InboundEmailItemOutcome.Quarantined, result.Outcome);
        Assert.Equal(InboundEmailIdentityFactory.Create(mail).IdempotencyKey, result.IdempotencyKey);
        Assert.Null(result.TicketNumber);
        Assert.Empty(context.TicketsList);
        var processed = Assert.Single(context.ProcessedEmailMessagesList);
        Assert.Equal(ProcessedEmailDisposition.Quarantined, processed.Disposition);
        Assert.Equal(AcknowledgementStatus.NotRequired, processed.AcknowledgementStatus);
        Assert.NotNull(processed.ProcessingNote);
    }

    [Fact]
    public async Task MatchingSubjectAndSender_AppendsReply()
    {
        var context = new FakeDb();
        var existing = Ticket.Create(
            "VS-000050",
            "Original",
            "Prior",
            "prior@example.test",
            FixedNow.UtcDateTime);
        existing.MarkAsWaitingCustomerReply(FixedNow.UtcDateTime.AddMinutes(-30));
        context.TicketsList.Add(existing);
        var processor = CreateProcessor(context, new RecordingSender(), "VS-000403");
        var mail = Mail(
            "<msg-reply@test>",
            "prior@example.test",
            "Re: [VS-000050] Original",
            "Still broken");

        var result = await processor.ProcessAsync(mail, CancellationToken.None);

        Assert.Equal(InboundEmailItemOutcome.AppendedReply, result.Outcome);
        Assert.Equal("VS-000050", result.TicketNumber);
        Assert.False(result.WasReopened);
        Assert.False(result.AcknowledgementSent);
        Assert.Single(context.TicketMessagesList);
        Assert.Equal(TicketStatus.CustomerReplied, existing.Status);
    }

    [Fact]
    public async Task ResolvedTicket_ReopensOnCustomerReply()
    {
        var context = new FakeDb();
        var existing = Ticket.Create(
            "VS-000060",
            "Resolved case",
            "Prior",
            "prior@example.test",
            FixedNow.UtcDateTime);
        existing.Resolve(FixedNow.UtcDateTime.AddHours(-2));
        context.TicketsList.Add(existing);
        var processor = CreateProcessor(context, new RecordingSender(), "VS-000404");
        var mail = Mail(
            "<msg-reopen@test>",
            "prior@example.test",
            "[VS-000060] Re: Resolved case",
            "Broke again");

        var result = await processor.ProcessAsync(mail, CancellationToken.None);

        Assert.Equal(InboundEmailItemOutcome.AppendedReply, result.Outcome);
        Assert.True(result.WasReopened);
        Assert.Equal(TicketStatus.CustomerReplied, existing.Status);
    }

    [Fact]
    public async Task FromMismatch_CreatesNewTicketInsteadOfReply()
    {
        var context = new FakeDb();
        context.TicketsList.Add(Ticket.Create(
            "VS-000070",
            "Owned by Ada",
            "Ada",
            "ada@example.test",
            FixedNow.UtcDateTime));
        var processor = CreateProcessor(context, new RecordingSender(), "VS-000405");
        var mail = Mail(
            "<msg-spoof@test>",
            "attacker@evil.test",
            "Re: [VS-000070] Owned by Ada",
            "Inject");

        var result = await processor.ProcessAsync(mail, CancellationToken.None);

        Assert.Equal(InboundEmailItemOutcome.CreatedTicket, result.Outcome);
        Assert.Equal("VS-000405", result.TicketNumber);
        Assert.Equal(2, context.TicketsList.Count);
        Assert.Equal("attacker@evil.test", context.TicketsList[1].CustomerEmail);
    }

    [Fact]
    public async Task SmtpFailure_ReturnsCreatedTicketWithAcknowledgementFailed()
    {
        var context = new FakeDb();
        var sender = new RecordingSender { ThrowOnSend = true };
        var processor = CreateProcessor(context, sender, "VS-000406");
        var mail = Mail("<msg-ack-fail@test>", "customer@example.test", "Help", "Body");

        var result = await processor.ProcessAsync(mail, CancellationToken.None);

        Assert.Equal(InboundEmailItemOutcome.CreatedTicket, result.Outcome);
        Assert.False(result.AcknowledgementSent);
        Assert.True(result.AcknowledgementFailed);
        Assert.Single(context.TicketsList);
        var processed = Assert.Single(context.ProcessedEmailMessagesList);
        Assert.Equal(AcknowledgementStatus.Failed, processed.AcknowledgementStatus);
        Assert.Equal("SMTP acknowledgement failed.", processed.AcknowledgementLastError);
    }

    [Fact]
    public async Task RepeatedOptimisticConflict_ReturnsTicketConcurrencyRetryableFailure()
    {
        var context = new ConcurrencyThrowingDb();
        var existing = Ticket.Create(
            "VS-000080",
            "Busy ticket",
            "Prior",
            "prior@example.test",
            FixedNow.UtcDateTime);
        context.TicketsList.Add(existing);
        var classifier = new AlwaysOptimisticConflictClassifier();
        var time = new FixedTimeProvider(FixedNow);
        var create = new CreateTicketHandler(context, new SequenceNumbers("VS-000407"), time, classifier);
        var reply = new AppendCustomerReplyHandler(context, time, classifier);
        var dispatcher = new AcknowledgementDispatcher(
            context,
            new RecordingSender(),
            time,
            NullLogger<AcknowledgementDispatcher>.Instance);
        var processor = new InboundEmailItemProcessor(
            context,
            create,
            reply,
            dispatcher,
            time,
            classifier);
        var mail = Mail(
            "<msg-concurrency@test>",
            "prior@example.test",
            "Re: [VS-000080] Busy ticket",
            "Retry me");

        var result = await processor.ProcessAsync(mail, CancellationToken.None);

        Assert.Equal(InboundEmailItemOutcome.RetryableFailure, result.Outcome);
        Assert.Equal("ticket-concurrency", result.FailureCode);
        Assert.Empty(context.TicketMessagesList);
        Assert.True(context.SaveAttempts >= 2);
    }

    [Fact]
    public async Task EmptyBody_CreatesTicketWithPlaceholder()
    {
        var context = new FakeDb();
        var processor = CreateProcessor(context, new RecordingSender(), "VS-000408");
        var mail = Mail("<msg-empty@test>", "customer@example.test", "Empty", "   ");

        var result = await processor.ProcessAsync(mail, CancellationToken.None);

        Assert.Equal(InboundEmailItemOutcome.CreatedTicket, result.Outcome);
        Assert.Equal(InboundMailLimits.EmptyBodyPlaceholder, context.TicketMessagesList[0].Content);
    }

    private static InboundEmailItemProcessor CreateProcessor(
        FakeDb context,
        IEmailSender sender,
        params string[] numbers)
    {
        var time = new FixedTimeProvider(FixedNow);
        var classifier = new NeverConflictClassifier();
        var create = new CreateTicketHandler(context, new SequenceNumbers(numbers), time, classifier);
        var reply = new AppendCustomerReplyHandler(context, time, classifier);
        var dispatcher = new AcknowledgementDispatcher(
            context,
            sender,
            time,
            NullLogger<AcknowledgementDispatcher>.Instance);
        return new InboundEmailItemProcessor(
            context,
            create,
            reply,
            dispatcher,
            time,
            classifier);
    }

    private static IncomingEmail Mail(
        string? messageId,
        string? from,
        string subject,
        string body,
        EmailReceiptHandle? receipt = null) =>
        new(
            MessageId: messageId,
            ReceiptHandle: receipt ?? new EmailReceiptHandle(
                EmailReceiptKind.Fake,
                $"fake\0{messageId ?? "null-id"}"),
            FromAddress: from,
            FromDisplayName: "Customer",
            Subject: subject,
            Body: body,
            IsHtml: false,
            ReceivedAt: FixedNow.UtcDateTime,
            Attachments: Array.Empty<IncomingEmailAttachment>());

    private sealed class NeverConflictClassifier : IDatabaseErrorClassifier
    {
        public bool IsProcessedEmailIdempotencyConflict(Exception exception) => false;

        public bool IsOptimisticConcurrencyConflict(Exception exception) => false;
    }

    private sealed class AlwaysOptimisticConflictClassifier : IDatabaseErrorClassifier
    {
        public bool IsProcessedEmailIdempotencyConflict(Exception exception) => false;

        public bool IsOptimisticConcurrencyConflict(Exception exception) => true;
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

    private class FakeDb : IApplicationDbContext
    {
        public List<User> UsersList { get; } = [];
        public List<Ticket> TicketsList { get; } = [];
        public List<TicketMessage> TicketMessagesList { get; } = [];
        public List<TicketAttachment> TicketAttachmentsList { get; } = [];
        public List<ProcessedEmailMessage> ProcessedEmailMessagesList { get; } = [];
        protected readonly List<object> pending = [];

        public IQueryable<User> Users => UsersList.AsQueryable();
        public IQueryable<Ticket> Tickets => TicketsList.AsQueryable();
        public IQueryable<TicketMessage> TicketMessages => TicketMessagesList.AsQueryable();
        public IQueryable<TicketAttachment> TicketAttachments => TicketAttachmentsList.AsQueryable();
        public IQueryable<ProcessedEmailMessage> ProcessedEmailMessages =>
            ProcessedEmailMessagesList.AsQueryable();

        public void Add<TEntity>(TEntity entity) where TEntity : class => pending.Add(entity!);

        public virtual Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
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

    private sealed class ConcurrencyThrowingDb : FakeDb
    {
        public int SaveAttempts { get; private set; }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveAttempts++;
            throw new InvalidOperationException("simulated optimistic concurrency");
        }
    }
}
