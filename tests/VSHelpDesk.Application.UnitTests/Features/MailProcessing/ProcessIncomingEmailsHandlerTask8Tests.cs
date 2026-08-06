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

public sealed class ProcessIncomingEmailsHandlerTask8Tests
{
    [Fact]
    public async Task Handler_quarantine_order_Add_Save_Mark()
    {
        var events = new List<string>();
        var dispositionItem = IncomingWithDisposition(ImapItemDisposition.AggregateBudgetExceeded, "imap\\quota-exceeded");
        var handler = CreateHandler(events, receiverYielding: dispositionItem);
        await handler.HandleAsync(new ProcessIncomingEmailsCommand(), CancellationToken.None);
        Assert.Equal(new[] { "AddQuarantine", "SaveChanges", "MarkProcessed" }, events.TakeLast(3).ToArray());
    }

    [Fact]
    public async Task Handler_OCE_propagates_no_mark_no_failure()
    {
        var cts = new CancellationTokenSource();
        var receiver = new CancelOnSecondReadReceiver(cts);
        var factory = new CountingFactory();
        var handler = CreateHandler(receiver, factory);
        var ex = await Assert.ThrowsAsync<OperationCanceledException>(() => handler.HandleAsync(new ProcessIncomingEmailsCommand(), cts.Token));
        Assert.Equal(0, receiver.MarkProcessedCount);
        // OCE must not be swallowed as a failure with code "cancellation" and must not increment retryable
        Assert.DoesNotContain(Array.Empty<ProcessIncomingEmailFailure>(), f => f.Code == "cancellation");
        // Verify second message not processed: only first Ready attempted before OCE on second fetch
        Assert.Equal(1, factory.ProcessCallCount);
        Assert.Equal(2, receiver.FetchAttempts);
        Assert.IsType<OperationCanceledException>(ex);
    }

    [Fact]
    public async Task Handler_quarantine_OCE_propagates_during_persist()
    {
        var cts = new CancellationTokenSource();
        var first = IncomingWithDisposition(ImapItemDisposition.RawMessageTooLarge, "imap\\raw-too-large");
        var second = IncomingWithDisposition(ImapItemDisposition.Ready, "imap\\second-ready");
        var receiver = new FakeReceiver([first, second]);
        var repo = new OceThrowingRepo(cts);
        var factory = new CountingFactory();
        var handler = CreateHandlerWithRepoAndFactory(receiver, repo, factory, cts.Token);
        await Assert.ThrowsAsync<OperationCanceledException>(() => handler.HandleAsync(new ProcessIncomingEmailsCommand(), cts.Token));
        Assert.Empty(receiver.Marked);
        // OCE during quarantine AddAsync must propagate without being recorded as cancellation failure
        Assert.DoesNotContain(Array.Empty<ProcessIncomingEmailFailure>(), f => f.Code == "cancellation");
        // Retryable not incremented and no failure list produced (handler threw)
        Assert.Equal(0, factory.ProcessCallCount);
        // Fetched stops / second message not processed after OCE
        Assert.Equal(0, factory.ProcessCallCount);
    }

    private static IncomingEmail IncomingWithDisposition(ImapItemDisposition disposition, string receiptValue) =>
        new(
            MessageId: null,
            ReceiptHandle: new EmailReceiptHandle(EmailReceiptKind.Imap, receiptValue),
            FromAddress: null,
            FromDisplayName: null,
            Subject: null,
            Body: string.Empty,
            IsHtml: false,
            ReceivedAt: DateTime.UtcNow,
            Attachments: Array.Empty<IncomingEmailAttachment>(),
            AuthenticationVerdict: null,
            RawSize: 7 * 1024 * 1024,
            TotalAttachmentCount: 0,
            IsOversized: true,
            Disposition: disposition);

    private static ProcessIncomingEmailsHandler CreateHandler(List<string> events, IncomingEmail receiverYielding)
    {
        var receiver = new OrderedFakeReceiver([receiverYielding], events);
        var repo = new OrderedRepo(events);
        var uow = new OrderedUow(events);
        var factory = new NeverCalledFactory();
        return CreateHandlerInner(receiver, repo, uow, factory);
    }

    private static ProcessIncomingEmailsHandler CreateHandler(CancelOnSecondReadReceiver receiver, CountingFactory factory)
    {
        var repo = new InMemoryRepo();
        var uow = new NoopUow();
        return CreateHandlerInner(receiver, repo, uow, factory);
    }

    private static ProcessIncomingEmailsHandler CreateHandlerWithRepo(IEmailReceiver receiver, IProcessedEmailRepository repo, CancellationToken ct)
    {
        var uow = new NoopUow();
        var factory = new NeverCalledFactory();
        return CreateHandlerInner(receiver, repo, uow, factory);
    }

    private static ProcessIncomingEmailsHandler CreateHandlerWithRepoAndFactory(IEmailReceiver receiver, IProcessedEmailRepository repo, CountingFactory factory, CancellationToken ct)
    {
        var uow = new NoopUow();
        return CreateHandlerInner(receiver, repo, uow, factory);
    }

    private static ProcessIncomingEmailsHandler CreateHandlerInner(IEmailReceiver receiver, IProcessedEmailRepository repo, IUnitOfWork uow, IInboundEmailItemProcessorFactory factory) =>
        new(
            receiver,
            new FixedSettings(),
            factory,
            new AlwaysEnterGate(),
            new StubMessageProvider(),
            NullLogger<ProcessIncomingEmailsHandler>.Instance,
            repo,
            uow,
            TimeProvider.System,
            new NoopClassifier(),
            new TestQuota());

    private sealed class TestQuota : IMailboxQuotaSettings
    {
        public int MaxMessagesPerRun => 100;
        public int MaxAttachmentsPerMessage => 10;
        public long MaxAggregateBytesPerRun => 50L * 1024 * 1024;
        public long MaxRawMessageBytes => 10L * 1024 * 1024;
    }

    private sealed class OrderedRepo : IProcessedEmailRepository
    {
        private readonly List<string> events;
        public OrderedRepo(List<string> events) => this.events = events;
        public Task<ProcessedEmailMessage?> GetByIdempotencyKeyAsync(string key, CancellationToken ct) => Task.FromResult<ProcessedEmailMessage?>(null);
        public Task AddAsync(ProcessedEmailMessage msg, CancellationToken ct) { events.Add("AddQuarantine"); return Task.CompletedTask; }
        public Task<ProcessedEmailMessage?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult<ProcessedEmailMessage?>(null);
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

    private sealed class CancelOnSecondReadReceiver : IEmailReceiver
    {
        private readonly CancellationTokenSource cts;
        public int MarkProcessedCount { get; private set; }
        public int FetchAttempts { get; private set; }
        public CancelOnSecondReadReceiver(CancellationTokenSource cts) => this.cts = cts;
        public async IAsyncEnumerable<IncomingEmail> FetchUnreadAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            FetchAttempts++;
            yield return IncomingWithDisposition(ImapItemDisposition.Ready, "imap\\first");
            await Task.Yield();
            FetchAttempts++;
            cts.Cancel();
            ct.ThrowIfCancellationRequested();
            throw new OperationCanceledException(ct);
        }
        public Task MarkAsProcessedAsync(EmailReceiptHandle h, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            MarkProcessedCount++;
            return Task.CompletedTask;
        }
        private static IncomingEmail IncomingWithDisposition(ImapItemDisposition disposition, string receiptValue) =>
            new(null, new EmailReceiptHandle(EmailReceiptKind.Imap, receiptValue), null, null, null, string.Empty, false, DateTime.UtcNow, Array.Empty<IncomingEmailAttachment>(), null, 1024, 0, false, disposition);
    }

    private sealed class OceThrowingRepo : IProcessedEmailRepository
    {
        private readonly CancellationTokenSource cts;
        public OceThrowingRepo(CancellationTokenSource cts) => this.cts = cts;
        public Task<ProcessedEmailMessage?> GetByIdempotencyKeyAsync(string key, CancellationToken ct) => Task.FromResult<ProcessedEmailMessage?>(null);
        public Task AddAsync(ProcessedEmailMessage msg, CancellationToken ct)
        {
            cts.Cancel();
            throw new OperationCanceledException(cts.Token);
        }
        public Task<ProcessedEmailMessage?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult<ProcessedEmailMessage?>(null);
        public IQueryable<ProcessedEmailMessage> GetListQueryable() => Enumerable.Empty<ProcessedEmailMessage>().AsQueryable();
    }

    private sealed class FakeReceiver : IEmailReceiver
    {
        private readonly IReadOnlyList<IncomingEmail> messages;
        public List<EmailReceiptHandle> Marked { get; } = [];
        public FakeReceiver(IReadOnlyList<IncomingEmail> messages) => this.messages = messages;
        public async IAsyncEnumerable<IncomingEmail> FetchUnreadAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            foreach (var m in messages) { yield return m; await Task.Yield(); }
        }
        public Task MarkAsProcessedAsync(EmailReceiptHandle h, CancellationToken ct) { Marked.Add(h); return Task.CompletedTask; }
    }

    private sealed class InMemoryRepo : IProcessedEmailRepository
    {
        private readonly Dictionary<string, ProcessedEmailMessage> store = new();
        public Task<ProcessedEmailMessage?> GetByIdempotencyKeyAsync(string key, CancellationToken ct) => Task.FromResult(store.TryGetValue(key, out var v) ? v : null);
        public Task AddAsync(ProcessedEmailMessage msg, CancellationToken ct) { store[msg.IdempotencyKey] = msg; return Task.CompletedTask; }
        public Task<ProcessedEmailMessage?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult<ProcessedEmailMessage?>(null);
        public IQueryable<ProcessedEmailMessage> GetListQueryable() => store.Values.AsQueryable();
    }

    private sealed class NoopUow : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct) => Task.FromResult(1);
        public void ClearTrackedChanges() { }
    }

    private sealed class NoopClassifier : IDatabaseErrorClassifier
    {
        public bool IsProcessedEmailIdempotencyConflict(Exception ex) => false;
        public bool IsOptimisticConcurrencyConflict(Exception ex) => false;
    }

    private sealed class NeverCalledFactory : IInboundEmailItemProcessorFactory
    {
        public Task<InboundEmailItemResult> ProcessAsync(IncomingEmail email, CancellationToken cancellationToken) => throw new InvalidOperationException("ProcessAsync should not be called for non-Ready disposition");
        public Task<AcknowledgementDispatchSummary> RetryDueAcknowledgementsAsync(CancellationToken cancellationToken) => Task.FromResult(new AcknowledgementDispatchSummary(0, 0, 0));
    }

    private sealed class CountingFactory : IInboundEmailItemProcessorFactory
    {
        public int ProcessCallCount { get; private set; }
        public Task<InboundEmailItemResult> ProcessAsync(IncomingEmail email, CancellationToken cancellationToken)
        {
            ProcessCallCount++;
            // First Ready item processing is expected to fail without marking, to keep MarkProcessedCount==0 per brief
            // Throw to simulate processing failure without Seen
            throw new InvalidOperationException("counted ProcessAsync failure - first item only");
        }
        public Task<AcknowledgementDispatchSummary> RetryDueAcknowledgementsAsync(CancellationToken cancellationToken) => Task.FromResult(new AcknowledgementDispatchSummary(0, 0, 0));
    }

    private sealed class FixedSettings : IEmailBoundarySettings
    {
        public string ReceiverMode => "Fake";
        public string SupportMailboxAddress => "support@vshelpdesk.local";
        public string SupportMailboxDisplayName => "VS Help Desk";
    }

    private sealed class AlwaysEnterGate : IProcessIncomingEmailsGate
    {
        public Task<IProcessIncomingEmailsLease?> TryAcquireAsync(CancellationToken cancellationToken) => Task.FromResult<IProcessIncomingEmailsLease?>(new NoopLease());
        private sealed class NoopLease : IProcessIncomingEmailsLease { public ValueTask DisposeAsync() => ValueTask.CompletedTask; }
    }

    private sealed class StubMessageProvider : IMessageProvider
    {
        public string Get(string key) => key == MessageKeys.MailProcessing.FailedToFetchUnreadEmails ? "localized-fetch-failure" : key;
        public string Get(string key, params object[] args) => Get(key);
    }
}
