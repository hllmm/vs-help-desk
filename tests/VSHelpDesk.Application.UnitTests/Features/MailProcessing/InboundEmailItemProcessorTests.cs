using VSHelpDesk.Application.Abstractions.Email;
using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Application.Abstractions.Persistence.Repositories;
using VSHelpDesk.Application.Abstractions.Storage;
using VSHelpDesk.Application.Features.Attachments;
using VSHelpDesk.Application.Features.MailProcessing;
using VSHelpDesk.Application.Features.MailProcessing.Acknowledgements;
using VSHelpDesk.Application.Features.MailProcessing.ProcessIncomingEmails;
using VSHelpDesk.Application.Features.Tickets.CreateTicket;
using VSHelpDesk.Application.Features.Tickets.ReplyToTicket;
using VSHelpDesk.Domain.Entities;
using VSHelpDesk.Domain.Enums;
using Microsoft.Extensions.Logging;
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
        var logger = new RecordingLogger<InboundEmailItemProcessor>();
        var processor = CreateProcessor(
            context,
            new RecordingSender(),
            "VS-000403",
            logger);
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
        var log = Assert.Single(logger.InformationMessages);
        Assert.Contains(existing.Id.ToString(), log, StringComparison.Ordinal);
        Assert.Contains("WaitingCustomerReply", log, StringComparison.Ordinal);
        Assert.Contains("CustomerReplied", log, StringComparison.Ordinal);
        Assert.Contains("reopened=False", log, StringComparison.Ordinal);
        Assert.DoesNotContain("prior@example.test", log, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Still broken", log, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreatedTicket_WritesSafeSuccessLogAfterPersistence()
    {
        var context = new FakeDb();
        var logger = new RecordingLogger<InboundEmailItemProcessor>();
        var processor = CreateProcessor(
            context,
            new RecordingSender(),
            "VS-000412",
            logger);
        var mail = Mail(
            "<log-created@test>",
            "private@example.test",
            "Private subject",
            "Private body");

        var result = await processor.ProcessAsync(mail, CancellationToken.None);

        Assert.Equal(InboundEmailItemOutcome.CreatedTicket, result.Outcome);
        var log = Assert.Single(logger.InformationMessages);
        Assert.Contains(result.TicketNumber!, log, StringComparison.Ordinal);
        Assert.Contains("status=New", log, StringComparison.Ordinal);
        Assert.DoesNotContain("private@example.test", log, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Private subject", log, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Private body", log, StringComparison.OrdinalIgnoreCase);
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
        existing.ResolveManually(
            FixedNow.UtcDateTime.AddHours(-2),
            Guid.Parse("11111111-1111-1111-1111-111111111111"));
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
        var create = new CreateTicketHandler(context, context, context, new SequenceNumbers("VS-000407"), time, classifier);
        var reply = new AppendCustomerReplyHandler(context, context, context, time, classifier);
        var dispatcher = new AcknowledgementDispatcher(
            context,
            context,
            context,
            new RecordingSender(),
            time,
            NullLogger<AcknowledgementDispatcher>.Instance);
        var writer = new TicketAttachmentWriter(
            context,
            context,
            context,
            new RecordingStorage(),
            new FixedPolicy(maxBytes: 1024 * 1024, allowed: ["text/plain"]),
            time,
            NullLogger<TicketAttachmentWriter>.Instance);
        var processor = new InboundEmailItemProcessor(
            context,
            context,
            context,
            create,
            reply,
            dispatcher,
            writer,
            time,
            classifier,
            NullLogger<InboundEmailItemProcessor>.Instance);
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

    [Fact]
    public async Task CreateTicket_WithAllowedAttachment_StoresOnCustomerMessage()
    {
        var context = new FakeDb();
        var processor = CreateProcessor(context, new RecordingSender(), "VS-000409");
        var bytes = "fake-attachment"u8.ToArray();
        var mail = Mail(
            "<msg-attach@test>",
            "customer@example.test",
            "With attach",
            "Body",
            attachments:
            [
                new IncomingEmailAttachment("note.txt", "text/plain", bytes.Length, bytes)
            ]);

        var result = await processor.ProcessAsync(mail, CancellationToken.None);

        Assert.Equal(InboundEmailItemOutcome.CreatedTicket, result.Outcome);
        var message = Assert.Single(context.TicketMessagesList);
        var stored = Assert.Single(context.TicketAttachmentsList);
        Assert.Equal(message.Id, stored.TicketMessageId);
        Assert.Equal("note.txt", stored.FileName);
        Assert.Equal("text/plain", stored.ContentType);
        Assert.Equal(bytes.Length, stored.FileSize);
    }

    [Fact]
    public async Task CreateTicket_WithDisallowedAttachment_SkipsWithoutFailingMailItem()
    {
        var context = new FakeDb();
        var processor = CreateProcessor(context, new RecordingSender(), "VS-000410");
        var bytes = new byte[] { 0x4D, 0x5A, 0x90, 0x00 };
        var mail = Mail(
            "<msg-bad-attach@test>",
            "customer@example.test",
            "Bad attach",
            "Body",
            attachments:
            [
                new IncomingEmailAttachment(
                    "evil.exe",
                    "application/x-msdownload",
                    bytes.Length,
                    bytes)
            ]);

        var result = await processor.ProcessAsync(mail, CancellationToken.None);

        Assert.Equal(InboundEmailItemOutcome.CreatedTicket, result.Outcome);
        Assert.Single(context.TicketsList);
        Assert.Single(context.TicketMessagesList);
        Assert.Empty(context.TicketAttachmentsList);
    }

    [Fact]
    public async Task AppendReply_WithAllowedAttachment_StoresOnCustomerMessage()
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
        var processor = CreateProcessor(context, new RecordingSender(), "VS-000411");
        var bytes = "reply-note"u8.ToArray();
        var mail = Mail(
            "<msg-reply-attach@test>",
            "prior@example.test",
            "Re: [VS-000050] Original",
            "Still broken",
            attachments:
            [
                new IncomingEmailAttachment("reply.txt", "text/plain", bytes.Length, bytes)
            ]);

        var result = await processor.ProcessAsync(mail, CancellationToken.None);

        Assert.Equal(InboundEmailItemOutcome.AppendedReply, result.Outcome);
        var message = Assert.Single(context.TicketMessagesList);
        var stored = Assert.Single(context.TicketAttachmentsList);
        Assert.Equal(message.Id, stored.TicketMessageId);
        Assert.Equal("reply.txt", stored.FileName);
    }

    private static InboundEmailItemProcessor CreateProcessor(
        FakeDb context,
        IEmailSender sender,
        string number,
        ILogger<InboundEmailItemProcessor>? logger = null)
    {
        var time = new FixedTimeProvider(FixedNow);
        var classifier = new NeverConflictClassifier();
        var create = new CreateTicketHandler(context, context, context, new SequenceNumbers(number), time, classifier);
        var reply = new AppendCustomerReplyHandler(context, context, context, time, classifier);
        var dispatcher = new AcknowledgementDispatcher(
            context,
            context,
            context,
            sender,
            time,
            NullLogger<AcknowledgementDispatcher>.Instance);
        var writer = new TicketAttachmentWriter(
            context,
            context,
            context,
            new RecordingStorage(),
            new FixedPolicy(maxBytes: 1024 * 1024, allowed: ["text/plain", "application/pdf"]),
            time,
            NullLogger<TicketAttachmentWriter>.Instance);
        return new InboundEmailItemProcessor(
            context,
            context,
            context,
            create,
            reply,
            dispatcher,
            writer,
            time,
            classifier,
            logger ?? NullLogger<InboundEmailItemProcessor>.Instance);
    }

    private static IncomingEmail Mail(
        string? messageId,
        string? from,
        string subject,
        string body,
        EmailReceiptHandle? receipt = null,
        IReadOnlyList<IncomingEmailAttachment>? attachments = null) =>
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
            Attachments: attachments ?? Array.Empty<IncomingEmailAttachment>());

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

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> InformationMessages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Information)
            {
                InformationMessages.Add(formatter(state, exception));
            }
        }
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

    private class FakeDb : IApplicationDbContext, IProcessedEmailRepository, ITicketRepository, ITicketAttachmentRepository, IUserRepository, IUnitOfWork
    {
        public List<User> UsersList { get; } = [];
        public List<Ticket> TicketsList { get; } = [];
        public List<TicketMessage> TicketMessagesList { get; } = [];
        public List<TicketAttachment> TicketAttachmentsList { get; } = [];
        public List<ProcessedEmailMessage> ProcessedEmailMessagesList { get; } = [];
        protected readonly List<object> pending = [];
        public Task<ProcessedEmailMessage?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(ProcessedEmailMessagesList.FirstOrDefault(p => p.IdempotencyKey == idempotencyKey));

        Task<ProcessedEmailMessage?> IProcessedEmailRepository.GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(ProcessedEmailMessagesList.FirstOrDefault(p => p.Id == id));

        public Task AddAsync(ProcessedEmailMessage message, CancellationToken cancellationToken = default)
        {
            Add(message);
            return Task.CompletedTask;
        }

        IQueryable<ProcessedEmailMessage> IProcessedEmailRepository.GetListQueryable() => ProcessedEmailMessages;

        Task<Ticket?> ITicketRepository.GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(TicketsList.FirstOrDefault(t => t.Id == id));

        public Task<Ticket?> GetByNumberAsync(string ticketNumber, CancellationToken cancellationToken = default) =>
            Task.FromResult(TicketsList.FirstOrDefault(t => t.TicketNumber == ticketNumber));

        public IQueryable<Ticket> GetListQueryable() => Tickets;

        public Task AddAsync(Ticket ticket, CancellationToken cancellationToken = default)
        {
            Add(ticket);
            return Task.CompletedTask;
        }

        public void Update(Ticket ticket) { }

        public Task AddMessageAsync(TicketMessage message, CancellationToken cancellationToken = default)
        {
            Add(message);
            return Task.CompletedTask;
        }

        public Task<bool> MessageExistsAsync(Guid messageId, CancellationToken cancellationToken = default) =>
            Task.FromResult(TicketMessagesList.Any(m => m.Id == messageId));

        public Task<TicketMessage?> GetMessageByIdAsync(Guid messageId, CancellationToken cancellationToken = default) =>
            Task.FromResult(TicketMessagesList.FirstOrDefault(m => m.Id == messageId));

        public Task<Guid> GetFirstMessageIdAsync(Guid ticketId, CancellationToken cancellationToken = default) =>
            Task.FromResult(TicketMessagesList.Where(m => m.TicketId == ticketId).OrderBy(m => m.CreatedAt).Select(m => m.Id).FirstOrDefault());

        Task<TicketAttachment?> ITicketAttachmentRepository.GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(TicketAttachmentsList.FirstOrDefault(a => a.Id == id));

        public Task<TicketAttachment?> GetByStoredFileNameAsync(string storedFileName, CancellationToken cancellationToken = default) =>
            Task.FromResult(TicketAttachmentsList.FirstOrDefault(a => a.StoredFileName == storedFileName));

        public Task AddAsync(TicketAttachment attachment, CancellationToken cancellationToken = default)
        {
            Add(attachment);
            return Task.CompletedTask;
        }

        public void Remove(TicketAttachment attachment) => TicketAttachmentsList.Remove(attachment);

        public IQueryable<TicketAttachment> GetOrphansQueryable() => TicketAttachments.Where(a => !TicketMessagesList.Any(m => m.Id == a.TicketMessageId));

        Task<User?> IUserRepository.GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(UsersList.FirstOrDefault(u => u.Id == id));

        public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default) =>
            Task.FromResult(UsersList.FirstOrDefault(u => u.Email == email));

        public Task<User?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default) =>
            Task.FromResult(UsersList.FirstOrDefault(u => u.Username == username));

        IQueryable<User> IUserRepository.GetListQueryable() => Users;

        public Task AddAsync(User user, CancellationToken cancellationToken = default)
        {
            Add(user);
            return Task.CompletedTask;
        }

        public void Update(User user) { }

        public IQueryable<User> Users => UsersList.AsQueryable();
        public IQueryable<Ticket> Tickets => TicketsList.AsQueryable();
        public IQueryable<TicketMessage> TicketMessages => TicketMessagesList.AsQueryable();
        public IQueryable<TicketAttachment> TicketAttachments => TicketAttachmentsList.AsQueryable();
        public IQueryable<ProcessedEmailMessage> ProcessedEmailMessages =>
            ProcessedEmailMessagesList.AsQueryable();

        public IQueryable<ApplicationParameter> ApplicationParameters =>
            Array.Empty<ApplicationParameter>().AsQueryable();

        public IQueryable<ParameterChangeLog> ParameterChangeLogs =>
            Array.Empty<ParameterChangeLog>().AsQueryable();

        public IQueryable<SystemLog> SystemLogs =>
            Array.Empty<SystemLog>().AsQueryable();

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
                    case TicketAttachment attachment:
                        TicketAttachmentsList.Add(attachment);
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

    private sealed class FixedPolicy(long maxBytes, string[] allowed) : IAttachmentUploadPolicy
    {
        private readonly HashSet<string> set = new(allowed, StringComparer.OrdinalIgnoreCase);

        public long MaxFileSizeBytes => maxBytes;

        public bool IsContentTypeAllowed(string? contentType) =>
            !string.IsNullOrWhiteSpace(contentType) && set.Contains(contentType.Split(';')[0].Trim());

        public string? DetectContentTypeFromContent(ReadOnlySpan<byte> header) => null;

        public bool IsDeclaredTypeConsistentWithContent(
            string? declaredContentType,
            ReadOnlySpan<byte> header) =>
            IsContentTypeAllowed(declaredContentType);
    }

    private sealed class RecordingStorage : IFileStorage
    {
        public async Task<StoredFile> SaveAsync(
            Stream content,
            string originalFileName,
            string contentType,
            CancellationToken cancellationToken = default)
        {
            using var ms = new MemoryStream();
            await content.CopyToAsync(ms, cancellationToken);
            return new StoredFile(
                $"{Guid.NewGuid():N}{Path.GetExtension(originalFileName)}",
                $"/tmp/{originalFileName}",
                contentType,
                ms.Length);
        }

        public Task<Stream> OpenReadAsync(string storedFileName, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(string storedFileName, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<string>> ListStoredFilesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
    }
}
