using Microsoft.Extensions.Logging.Abstractions;
using VSHelpDesk.Application.Abstractions.Email;
using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Application.Features.MailProcessing.Acknowledgements;
using VSHelpDesk.Domain.Entities;
using VSHelpDesk.Domain.Enums;

namespace VSHelpDesk.Application.UnitTests.Features.MailProcessing;

public sealed class AcknowledgementDispatcherTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 7, 31, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AckFailure_PersistsAttemptsErrorAndNextDue()
    {
        var ticket = Ticket.Create("VS-000300", "Subject", "Ada", "ada@example.test", FixedNow.UtcDateTime);
        var processed = ProcessedEmailMessage.ForCreatedTicket(
            "<ack-fail@test>",
            "<ack-fail@test>",
            FixedNow.UtcDateTime,
            ticket.Id);
        var db = new FakeDb(ticket, processed);
        var sender = new RecordingSender
        {
            ThrowOnSend = true,
            ExceptionMessage = "SMTP unavailable: secret=password"
        };
        var dispatcher = CreateDispatcher(db, sender);

        var result = await dispatcher.AttemptAsync(processed.Id, CancellationToken.None);

        Assert.True(result.Attempted);
        Assert.False(result.Sent);
        Assert.Equal(AcknowledgementStatus.Failed, processed.AcknowledgementStatus);
        Assert.Equal(1, processed.AcknowledgementAttempts);
        Assert.Equal("SMTP acknowledgement failed.", processed.AcknowledgementLastError);
        Assert.Equal(FixedNow.UtcDateTime.AddMinutes(1), processed.AcknowledgementNextAttemptAt);
        Assert.DoesNotContain("password", processed.AcknowledgementLastError, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", processed.AcknowledgementLastError, StringComparison.Ordinal);
        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task LaterRetry_SendsAndMarksSent()
    {
        var ticket = Ticket.Create("VS-000301", "Subject", "Ada", "ada@example.test", FixedNow.UtcDateTime);
        var processed = ProcessedEmailMessage.ForCreatedTicket(
            "<ack-retry@test>",
            "<ack-retry@test>",
            FixedNow.UtcDateTime.AddMinutes(-10),
            ticket.Id);
        // Failed 5 minutes ago → next due in 1 minute → already due at FixedNow.
        processed.RecordAcknowledgementFailure(
            FixedNow.UtcDateTime.AddMinutes(-5),
            "SMTP acknowledgement failed.");
        Assert.Equal(AcknowledgementStatus.Failed, processed.AcknowledgementStatus);
        Assert.True(processed.IsAcknowledgementDue(FixedNow.UtcDateTime));

        var db = new FakeDb(ticket, processed);
        var sender = new RecordingSender();
        var dispatcher = CreateDispatcher(db, sender);

        var summary = await dispatcher.RetryDueAsync(CancellationToken.None);

        Assert.Equal(1, summary.Attempted);
        Assert.Equal(1, summary.Sent);
        Assert.Equal(0, summary.Failed);
        Assert.Equal(AcknowledgementStatus.Sent, processed.AcknowledgementStatus);
        Assert.Equal(FixedNow.UtcDateTime, processed.AcknowledgementSentAt);
        Assert.Null(processed.AcknowledgementNextAttemptAt);
        Assert.Null(processed.AcknowledgementLastError);
        Assert.Single(sender.Sent);
        Assert.Equal("ada@example.test", sender.Sent[0].ToAddress);
        Assert.Contains("VS-000301", sender.Sent[0].Subject, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Attempt_NotRequired_DoesNotSend()
    {
        var ticket = Ticket.Create("VS-000302", "Subject", "Ada", "ada@example.test", FixedNow.UtcDateTime);
        var processed = ProcessedEmailMessage.ForAppendedReply(
            "<ack-not-req@test>",
            "<ack-not-req@test>",
            FixedNow.UtcDateTime,
            ticket.Id);
        var db = new FakeDb(ticket, processed);
        var sender = new RecordingSender();
        var dispatcher = CreateDispatcher(db, sender);

        var result = await dispatcher.AttemptAsync(processed.Id, CancellationToken.None);

        Assert.False(result.Attempted);
        Assert.False(result.Sent);
        Assert.Empty(sender.Sent);
        Assert.Equal(AcknowledgementStatus.NotRequired, processed.AcknowledgementStatus);
    }

    [Fact]
    public async Task Attempt_Success_MarksSent()
    {
        var ticket = Ticket.Create("VS-000303", "Subject", "Ada", "ada@example.test", FixedNow.UtcDateTime);
        var processed = ProcessedEmailMessage.ForCreatedTicket(
            "<ack-ok@test>",
            "<ack-ok@test>",
            FixedNow.UtcDateTime,
            ticket.Id);
        var db = new FakeDb(ticket, processed);
        var sender = new RecordingSender();
        var dispatcher = CreateDispatcher(db, sender);

        var result = await dispatcher.AttemptAsync(processed.Id, CancellationToken.None);

        Assert.True(result.Attempted);
        Assert.True(result.Sent);
        Assert.Equal(AcknowledgementStatus.Sent, processed.AcknowledgementStatus);
        Assert.Single(sender.Sent);
        Assert.Equal("Ada", sender.Sent[0].ToDisplayName);
    }

    [Fact]
    public async Task Attempt_SaveChangesFailureAfterSend_PropagatesAndDoesNotMarkSmtpFailure()
    {
        var ticket = Ticket.Create("VS-000304", "Subject", "Ada", "ada@example.test", FixedNow.UtcDateTime);
        var processed = ProcessedEmailMessage.ForCreatedTicket(
            "<ack-db-fail@test>",
            "<ack-db-fail@test>",
            FixedNow.UtcDateTime,
            ticket.Id);
        var db = new FakeDb(ticket, processed)
        {
            ThrowOnSave = true,
            SaveExceptionMessage = "database write failed"
        };
        var sender = new RecordingSender();
        var dispatcher = CreateDispatcher(db, sender);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => dispatcher.AttemptAsync(processed.Id, CancellationToken.None));

        Assert.Equal("database write failed", ex.Message);
        Assert.Single(sender.Sent);
        // Domain mutation may have run in-memory; must not have been treated as SMTP failure.
        Assert.NotEqual(AcknowledgementStatus.Failed, processed.AcknowledgementStatus);
        Assert.NotEqual("SMTP acknowledgement failed.", processed.AcknowledgementLastError);
    }

    private static AcknowledgementDispatcher CreateDispatcher(FakeDb db, IEmailSender sender) =>
        new(db, sender, new FixedTimeProvider(FixedNow), NullLogger<AcknowledgementDispatcher>.Instance);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
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
        private readonly List<Ticket> tickets;
        private readonly List<ProcessedEmailMessage> processed;
        private readonly List<object> pending = [];

        public FakeDb(Ticket ticket, ProcessedEmailMessage processedRow)
        {
            tickets = [ticket];
            processed = [processedRow];
        }

        public bool ThrowOnSave { get; init; }
        public string SaveExceptionMessage { get; init; } = "database write failed";

        public IQueryable<User> Users => Array.Empty<User>().AsQueryable();
        public IQueryable<Ticket> Tickets => tickets.AsQueryable();
        public IQueryable<TicketMessage> TicketMessages => Array.Empty<TicketMessage>().AsQueryable();
        public IQueryable<TicketAttachment> TicketAttachments =>
            Array.Empty<TicketAttachment>().AsQueryable();
        public IQueryable<ProcessedEmailMessage> ProcessedEmailMessages => processed.AsQueryable();

        public IQueryable<ApplicationParameter> ApplicationParameters =>
            Array.Empty<ApplicationParameter>().AsQueryable();

        public IQueryable<ParameterChangeLog> ParameterChangeLogs =>
            Array.Empty<ParameterChangeLog>().AsQueryable();

        public void Add<TEntity>(TEntity entity) where TEntity : class => pending.Add(entity!);

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (ThrowOnSave)
            {
                throw new InvalidOperationException(SaveExceptionMessage);
            }

            pending.Clear();
            return Task.FromResult(1);
        }

        public void ClearTrackedChanges() => pending.Clear();
    }
}
