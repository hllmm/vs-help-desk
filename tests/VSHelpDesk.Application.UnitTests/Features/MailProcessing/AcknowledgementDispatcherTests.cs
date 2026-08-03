using Microsoft.Extensions.Logging.Abstractions;
using VSHelpDesk.Application.Abstractions.Email;
using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Application.Abstractions.Persistence.Repositories;
using VSHelpDesk.Application.Features.MailProcessing.Acknowledgements;
using VSHelpDesk.Domain.Entities;
using VSHelpDesk.Domain.Enums;

namespace VSHelpDesk.Application.UnitTests.Features.MailProcessing;

public sealed class AcknowledgementDispatcherTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 7, 28, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AttemptAsync_ValidPendingRow_SendsEmailAndRecordsSentState()
    {
        var ticket = Ticket.Create("VS-000001", "Subj", "Ada", "ada@example.test", FixedNow.UtcDateTime);
        var processed = ProcessedEmailMessage.ForCreatedTicket(
            "<msg1@example.test>",
            "<msg1@example.test>",
            FixedNow.UtcDateTime,
            ticket.Id);

        var db = new FakeDb(ticket, processed);
        var sender = new RecordingSender();
        var dispatcher = CreateDispatcher(db, sender);

        var result = await dispatcher.AttemptAsync(processed.Id, CancellationToken.None);

        Assert.True(result.Attempted);
        Assert.True(result.Sent);
        var mail = Assert.Single(sender.Sent);
        Assert.Equal("ada@example.test", mail.ToAddress);
        Assert.Contains("VS-000001", mail.Subject, StringComparison.Ordinal);

        Assert.Equal(AcknowledgementStatus.Sent, processed.AcknowledgementStatus);
        Assert.Equal(FixedNow.UtcDateTime, processed.AcknowledgementSentAt);
        Assert.Null(processed.AcknowledgementNextAttemptAt);
        Assert.Equal(1, processed.AcknowledgementAttempts);
    }

    [Fact]
    public async Task AttemptAsync_SmtpFailure_RecordsFailedStateAndSchedulesRetry()
    {
        var ticket = Ticket.Create("VS-000001", "Subj", "Ada", "ada@example.test", FixedNow.UtcDateTime);
        var processed = ProcessedEmailMessage.ForCreatedTicket(
            "<msg1@example.test>",
            "<msg1@example.test>",
            FixedNow.UtcDateTime,
            ticket.Id);

        var db = new FakeDb(ticket, processed);
        var sender = new RecordingSender { ThrowOnSend = true };
        var dispatcher = CreateDispatcher(db, sender);

        var result = await dispatcher.AttemptAsync(processed.Id, CancellationToken.None);

        Assert.True(result.Attempted);
        Assert.False(result.Sent);
        Assert.Equal(AcknowledgementStatus.Failed, processed.AcknowledgementStatus);
        Assert.Equal(1, processed.AcknowledgementAttempts);
        Assert.Equal(FixedNow.UtcDateTime.AddMinutes(1), processed.AcknowledgementNextAttemptAt);
        Assert.Equal("SMTP acknowledgement failed.", processed.AcknowledgementLastError);
    }

    [Fact]
    public async Task AttemptAsync_NotDue_ReturnsNotAttemptedWithoutSending()
    {
        var ticket = Ticket.Create("VS-000001", "Subj", "Ada", "ada@example.test", FixedNow.UtcDateTime);
        var processed = ProcessedEmailMessage.ForCreatedTicket(
            "<msg1@example.test>",
            "<msg1@example.test>",
            FixedNow.UtcDateTime,
            ticket.Id);
        processed.RecordAcknowledgementSent(FixedNow.UtcDateTime);

        var db = new FakeDb(ticket, processed);
        var sender = new RecordingSender();
        var dispatcher = CreateDispatcher(db, sender);

        var result = await dispatcher.AttemptAsync(processed.Id, CancellationToken.None);

        Assert.False(result.Attempted);
        Assert.False(result.Sent);
        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task RetryDueAsync_ProcessesOnlyDueRows()
    {
        var ticket = Ticket.Create("VS-000001", "Subj", "Ada", "ada@example.test", FixedNow.UtcDateTime);
        var dueProcessed = ProcessedEmailMessage.ForCreatedTicket(
            "<due@example.test>",
            "<due@example.test>",
            FixedNow.UtcDateTime.AddMinutes(-10),
            ticket.Id);

        var futureProcessed = ProcessedEmailMessage.ForCreatedTicket(
            "<future@example.test>",
            "<future@example.test>",
            FixedNow.UtcDateTime.AddMinutes(10),
            ticket.Id);

        var db = new FakeDb(ticket, dueProcessed)
        {
            processed = [dueProcessed, futureProcessed]
        };

        var sender = new RecordingSender();
        var dispatcher = CreateDispatcher(db, sender);

        var summary = await dispatcher.RetryDueAsync(CancellationToken.None);

        Assert.Equal(1, summary.Attempted);
        Assert.Equal(1, summary.Sent);
        Assert.Equal(0, summary.Failed);
        Assert.Single(sender.Sent);
    }

    [Fact]
    public async Task AttemptAsync_DatabaseSaveFailure_PropagatesExceptionAndDoesNotRecordSmtpFailure()
    {
        var ticket = Ticket.Create("VS-000001", "Subj", "Ada", "ada@example.test", FixedNow.UtcDateTime);
        var processed = ProcessedEmailMessage.ForCreatedTicket(
            "<msg1@example.test>",
            "<msg1@example.test>",
            FixedNow.UtcDateTime,
            ticket.Id);

        var db = new FakeDb(ticket, processed) { ThrowOnSave = true };
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
        new(db, db, db, sender, new FixedTimeProvider(FixedNow), NullLogger<AcknowledgementDispatcher>.Instance);

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

    private sealed class FakeDb : IApplicationDbContext, IProcessedEmailRepository, ITicketRepository, IUnitOfWork
    {
        private readonly List<Ticket> tickets;
        public List<ProcessedEmailMessage> processed;
        private readonly List<object> pending = [];

        public FakeDb(Ticket ticket, ProcessedEmailMessage processedRow)
        {
            tickets = [ticket];
            processed = [processedRow];
        }

        public Task<ProcessedEmailMessage?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(processed.FirstOrDefault(p => p.IdempotencyKey == idempotencyKey));

        Task<ProcessedEmailMessage?> IProcessedEmailRepository.GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(processed.FirstOrDefault(p => p.Id == id));

        public Task AddAsync(ProcessedEmailMessage message, CancellationToken cancellationToken = default) => Task.CompletedTask;

        IQueryable<ProcessedEmailMessage> IProcessedEmailRepository.GetListQueryable() => ProcessedEmailMessages;

        Task<Ticket?> ITicketRepository.GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(tickets.FirstOrDefault(t => t.Id == id));

        public Task<Ticket?> GetByNumberAsync(string ticketNumber, CancellationToken cancellationToken = default) =>
            Task.FromResult(tickets.FirstOrDefault(t => t.TicketNumber == ticketNumber));

        public IQueryable<Ticket> GetListQueryable() => Tickets;

        public Task AddAsync(Ticket ticket, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Update(Ticket ticket) { }

        public Task AddMessageAsync(TicketMessage message, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<bool> MessageExistsAsync(Guid messageId, CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task<TicketMessage?> GetMessageByIdAsync(Guid messageId, CancellationToken cancellationToken = default) => Task.FromResult<TicketMessage?>(null);

        public Task<Guid> GetFirstMessageIdAsync(Guid ticketId, CancellationToken cancellationToken = default) => Task.FromResult(Guid.Empty);

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

        public IQueryable<SystemLog> SystemLogs =>
            Array.Empty<SystemLog>().AsQueryable();

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
