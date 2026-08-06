using Microsoft.Extensions.Logging.Abstractions;
using VSHelpDesk.Application.Abstractions.Email;
using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Application.Abstractions.Persistence.Repositories;
using VSHelpDesk.Application.Common.Localization;
using VSHelpDesk.Application.Features.MailProcessing;
using VSHelpDesk.Application.Features.MailProcessing.Acknowledgements;
using VSHelpDesk.Application.Features.MailProcessing.ProcessIncomingEmails;
using VSHelpDesk.Domain.Entities;

namespace VSHelpDesk.Application.UnitTests.Features.MailProcessing;

public sealed class ProcessIncomingEmailsHandlerQuotaTests
{
    [Fact]
    public async Task Handler_quarantines_remaining_when_aggregate_exceeds_50MB()
    {
        // Streaming disposition: third mail is AggregateBudgetExceeded from IMAP client
        var mail1 = MailWithAttachments("fake\\m1", 1024);
        var mail2 = MailWithAttachments("fake\\m2", 1024);
        var mail3 = MailWithDisposition("fake\\m3", ImapItemDisposition.AggregateBudgetExceeded);

        var receiver = new FakeReceiver([mail1, mail2, mail3]);
        var factory = new ScriptedFactory([
            new InboundEmailItemResult(InboundEmailItemOutcome.CreatedTicket, "<id1>", "VS-000001", false, false, false, null),
            new InboundEmailItemResult(InboundEmailItemOutcome.CreatedTicket, "<id2>", "VS-000002", false, false, false, null)
        ]);

        var handler = CreateHandler(receiver, factory);
        var result = await handler.HandleAsync(new ProcessIncomingEmailsCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        // Only first 2 should be processed, third quarantined by disposition
        Assert.Equal(2, factory.ProcessCallCount);
        Assert.Equal(1, result.Value!.Quarantined);
        Assert.Contains(result.Value.Failures, f => f.Code == "AggregateBudgetExceeded");
        // Third mail still marked as processed (quota-quarantined)
        Assert.Contains(receiver.Marked, h => h.Value == "fake\\m3");
    }

    [Fact]
    public async Task Handler_processes_all_when_under_aggregate_limit()
    {
        var oneKb = 1024L;
        var mail1 = MailWithAttachments("fake\\a1", oneKb);
        var mail2 = MailWithAttachments("fake\\a2", oneKb);

        var receiver = new FakeReceiver([mail1, mail2]);
        var factory = new ScriptedFactory([
            new InboundEmailItemResult(InboundEmailItemOutcome.CreatedTicket, "<id1>", "VS-000010", false, false, false, null),
            new InboundEmailItemResult(InboundEmailItemOutcome.CreatedTicket, "<id2>", "VS-000011", false, false, false, null)
        ]);

        var handler = CreateHandler(receiver, factory);
        var result = await handler.HandleAsync(new ProcessIncomingEmailsCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, factory.ProcessCallCount);
        Assert.Equal(0, result.Value!.Quarantined);
        Assert.DoesNotContain(result.Value.Failures, f => f.Code == "aggregate-quota-exceeded");
    }

    [Fact]
    public async Task Handler_single_huge_mail_exceeding_limit_is_quarantined()
    {
        // Single mail disposition RawMessageTooLarge from IMAP client
        var mail = MailWithDisposition("fake\\huge", ImapItemDisposition.RawMessageTooLarge);
        var receiver = new FakeReceiver([mail]);
        var factory = new ScriptedFactory([]);

        var handler = CreateHandler(receiver, factory);
        var result = await handler.HandleAsync(new ProcessIncomingEmailsCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, factory.ProcessCallCount);
        Assert.Equal(1, result.Value!.Quarantined);
        Assert.Contains(result.Value.Failures, f => f.Code == "RawMessageTooLarge");
    }

    [Fact]
    public async Task Handler_quarantine_persistence_failure_leaves_mail_unseen()
    {
        var mail1 = MailWithAttachments("fake\\m1", 1024);
        var mail2 = MailWithAttachments("fake\\m2", 1024);
        var mail3 = MailWithDisposition("fake\\m3", ImapItemDisposition.AggregateBudgetExceeded); // disposition quarantine

        var receiver = new FakeReceiver([mail1, mail2, mail3]);
        var factory = new ScriptedFactory([
            new InboundEmailItemResult(InboundEmailItemOutcome.CreatedTicket, "<id1>", "VS-000001", false, false, false, null),
            new InboundEmailItemResult(InboundEmailItemOutcome.CreatedTicket, "<id2>", "VS-000002", false, false, false, null)
        ]);

        var failingRepo = new FailingRepo();
        var handler = CreateHandler(receiver, factory, failingRepo, new NoopUnitOfWork(), new NoopClassifier());
        var result = await handler.HandleAsync(new ProcessIncomingEmailsCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, factory.ProcessCallCount);
        Assert.Contains(result.Value!.Failures, f => f.Code == "quarantine-failed");
        Assert.DoesNotContain(receiver.Marked, h => h.Value == "fake\\m3"); // not marked because quarantine failed
    }

    [Fact]
    public async Task Handler_quarantine_persists_before_mark_ordering()
    {
        var mail1 = MailWithAttachments("fake\\m1", 1024);
        var mail2 = MailWithAttachments("fake\\m2", 1024);
        var mail3 = MailWithDisposition("fake\\m3", ImapItemDisposition.AggregateBudgetExceeded);

        var events = new List<string>();
        var receiver = new OrderedFakeReceiver([mail1, mail2, mail3], events);
        var factory = new ScriptedFactory([
            new InboundEmailItemResult(InboundEmailItemOutcome.CreatedTicket, "<id1>", "VS-000001", false, false, false, null),
            new InboundEmailItemResult(InboundEmailItemOutcome.CreatedTicket, "<id2>", "VS-000002", false, false, false, null)
        ]);

        var repo = new OrderedRepo(events);
        var uow = new OrderedUow(events);
        var handler = CreateHandler(receiver, factory, repo, uow, new NoopClassifier());
        await handler.HandleAsync(new ProcessIncomingEmailsCommand(), CancellationToken.None);

        // One shared recorder — exact order: AddQuarantine -> SaveChanges -> MarkProcessed (quarantine tail, after prior successful marks)
        Assert.Equal(new[] { "AddQuarantine", "SaveChanges", "MarkProcessed" }, events.TakeLast(3).ToArray());
        Assert.Equal(5, events.Count); // 2 prior MarkProcessed + quarantine triad
    }

    private sealed class FailingRepo : IProcessedEmailRepository
    {
        public Task<ProcessedEmailMessage?> GetByIdempotencyKeyAsync(string key, CancellationToken ct) => Task.FromResult<ProcessedEmailMessage?>(null);
        public Task AddAsync(ProcessedEmailMessage msg, CancellationToken ct) => throw new InvalidOperationException("DB down");
        public Task<ProcessedEmailMessage?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult<ProcessedEmailMessage?>(null);
        public Task<IReadOnlyList<ProcessedEmailMessage>> GetDueAcknowledgementsAsync(int take, DateTime now, CancellationToken ct) => Task.FromResult<IReadOnlyList<ProcessedEmailMessage>>(Array.Empty<ProcessedEmailMessage>());
        public IQueryable<ProcessedEmailMessage> GetListQueryable() => Enumerable.Empty<ProcessedEmailMessage>().AsQueryable();
    }

    private sealed class OrderedRepo : IProcessedEmailRepository
    {
        private readonly List<string> events;
        public OrderedRepo(List<string> events) => this.events = events;
        public Task<ProcessedEmailMessage?> GetByIdempotencyKeyAsync(string key, CancellationToken ct) => Task.FromResult<ProcessedEmailMessage?>(null);
        public Task AddAsync(ProcessedEmailMessage msg, CancellationToken ct) { events.Add("AddQuarantine"); return Task.CompletedTask; }
        public Task<ProcessedEmailMessage?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult<ProcessedEmailMessage?>(null);
        public Task<IReadOnlyList<ProcessedEmailMessage>> GetDueAcknowledgementsAsync(int take, DateTime now, CancellationToken ct) => Task.FromResult<IReadOnlyList<ProcessedEmailMessage>>(Array.Empty<ProcessedEmailMessage>());
        public IQueryable<ProcessedEmailMessage> GetListQueryable() => Enumerable.Empty<ProcessedEmailMessage>().AsQueryable();
    }

    private sealed class OrderedUow : IUnitOfWork
    {
        private readonly List<string> events;
        public OrderedUow(List<string> events) => this.events = events;
        public Task<int> SaveChangesAsync(CancellationToken ct) { events.Add("SaveChanges"); return Task.FromResult(1); }
        public void ClearTrackedChanges() { }
    }

    private sealed class OrderedFakeReceiver : IEmailReceiver
    {
        private readonly IReadOnlyList<IncomingEmail> messages;
        private readonly List<string> events;
        public List<EmailReceiptHandle> Marked { get; } = [];
        public OrderedFakeReceiver(IReadOnlyList<IncomingEmail> messages, List<string> events)
        {
            this.messages = messages;
            this.events = events;
        }
        public async IAsyncEnumerable<IncomingEmail> FetchUnreadAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            foreach (var m in messages) { yield return m; await Task.Yield(); }
        }
        public Task MarkAsProcessedAsync(EmailReceiptHandle h, CancellationToken ct) { events.Add("MarkProcessed"); Marked.Add(h); return Task.CompletedTask; }
    }

    private static IncomingEmail MailWithAttachments(string receiptValue, long fileSize)
    {
        var attachment = new IncomingEmailAttachment("file.pdf", "application/pdf", fileSize, new byte[0]);
        return new IncomingEmail(
            MessageId: $"<{receiptValue}@test>",
            ReceiptHandle: new EmailReceiptHandle(EmailReceiptKind.Fake, receiptValue),
            FromAddress: "customer@example.test",
            FromDisplayName: "Customer",
            Subject: "Help",
            Body: "Body",
            IsHtml: false,
            ReceivedAt: DateTime.UtcNow,
            Attachments: [attachment]);
    }

    private static IncomingEmail MailWithDisposition(string receiptValue, ImapItemDisposition disposition) =>
        new(
            MessageId: null,
            ReceiptHandle: new EmailReceiptHandle(EmailReceiptKind.Fake, receiptValue),
            FromAddress: null,
            FromDisplayName: null,
            Subject: null,
            Body: string.Empty,
            IsHtml: false,
            ReceivedAt: DateTime.UtcNow,
            Attachments: Array.Empty<IncomingEmailAttachment>(),
            AuthenticationVerdict: null,
            RawSize: 1024,
            TotalAttachmentCount: 0,
            IsOversized: true,
            Disposition: disposition);

    private static ProcessIncomingEmailsHandler CreateHandler(IEmailReceiver receiver, IInboundEmailItemProcessorFactory factory, IProcessedEmailRepository? repo = null, IUnitOfWork? uow = null, IDatabaseErrorClassifier? classifier = null)
    {
        repo ??= new InMemoryProcessedEmailRepo();
        uow ??= new NoopUnitOfWork();
        classifier ??= new NoopClassifier();
        return new(
            receiver,
            new FixedSettings(),
            factory,
            new AlwaysEnterGate(),
            new StubMessageProvider(),
            NullLogger<ProcessIncomingEmailsHandler>.Instance,
            repo,
            uow,
            TimeProvider.System,
            classifier,
            new TestQuota());
    }

    private sealed class TestQuota : IMailboxQuotaSettings
    {
        public int MaxMessagesPerRun => 100;
        public int MaxAttachmentsPerMessage => 10;
        public long MaxAggregateBytesPerRun => 50L * 1024 * 1024;
        public long MaxRawMessageBytes => 5L * 1024 * 1024;
    }

    private sealed class InMemoryProcessedEmailRepo : IProcessedEmailRepository
    {
        private readonly Dictionary<string, ProcessedEmailMessage> store = new();
        public Task<ProcessedEmailMessage?> GetByIdempotencyKeyAsync(string key, CancellationToken ct) => Task.FromResult(store.TryGetValue(key, out var v) ? v : null);
        public Task AddAsync(ProcessedEmailMessage msg, CancellationToken ct) { store[msg.IdempotencyKey] = msg; return Task.CompletedTask; }
        public Task<ProcessedEmailMessage?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult<ProcessedEmailMessage?>(null);
        public Task<IReadOnlyList<ProcessedEmailMessage>> GetDueAcknowledgementsAsync(int take, DateTime now, CancellationToken ct) => Task.FromResult<IReadOnlyList<ProcessedEmailMessage>>(Array.Empty<ProcessedEmailMessage>());
        public IQueryable<ProcessedEmailMessage> GetListQueryable() => store.Values.AsQueryable();
    }

    private sealed class NoopUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct) => Task.FromResult(1);
        public void ClearTrackedChanges() { }
    }

    private sealed class NoopClassifier : IDatabaseErrorClassifier
    {
        public bool IsProcessedEmailIdempotencyConflict(Exception ex) => false;
        public bool IsOptimisticConcurrencyConflict(Exception ex) => false;
    }

    private sealed class FixedSettings : IEmailBoundarySettings
    {
        public string ReceiverMode => "Fake";
        public string SupportMailboxAddress => "support@vshelpdesk.local";
        public string SupportMailboxDisplayName => "VS Help Desk";
    }

    private sealed class AlwaysEnterGate : IProcessIncomingEmailsGate
    {
        public Task<IProcessIncomingEmailsLease?> TryAcquireAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IProcessIncomingEmailsLease?>(new NoopLease());
        private sealed class NoopLease : IProcessIncomingEmailsLease
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class StubMessageProvider : IMessageProvider
    {
        public string Get(string key) =>
            key == MessageKeys.MailProcessing.FailedToFetchUnreadEmails ? "localized-fetch-failure" : key;
        public string Get(string key, params object[] args) => Get(key);
    }

    private sealed class FakeReceiver(IReadOnlyList<IncomingEmail> messages) : IEmailReceiver
    {
        public List<EmailReceiptHandle> Marked { get; } = [];
        public async IAsyncEnumerable<IncomingEmail> FetchUnreadAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var m in messages) { yield return m; await Task.Yield(); }
        }
        public Task MarkAsProcessedAsync(EmailReceiptHandle receiptHandle, CancellationToken cancellationToken)
        {
            Marked.Add(receiptHandle);
            return Task.CompletedTask;
        }
    }

    private sealed class ScriptedFactory(IReadOnlyList<InboundEmailItemResult> results) : IInboundEmailItemProcessorFactory
    {
        public int ProcessCallCount { get; private set; }
        public Task<InboundEmailItemResult> ProcessAsync(IncomingEmail email, CancellationToken cancellationToken)
        {
            var idx = ProcessCallCount++;
            return Task.FromResult(results[idx]);
        }
        public Task<AcknowledgementDispatchSummary> RetryDueAcknowledgementsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new AcknowledgementDispatchSummary(0, 0, 0));
    }
}
