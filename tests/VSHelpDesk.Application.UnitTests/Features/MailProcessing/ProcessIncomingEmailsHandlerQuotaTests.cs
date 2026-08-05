using Microsoft.Extensions.Logging.Abstractions;
using VSHelpDesk.Application.Abstractions.Email;
using VSHelpDesk.Application.Common.Localization;
using VSHelpDesk.Application.Features.MailProcessing;
using VSHelpDesk.Application.Features.MailProcessing.Acknowledgements;
using VSHelpDesk.Application.Features.MailProcessing.ProcessIncomingEmails;

namespace VSHelpDesk.Application.UnitTests.Features.MailProcessing;

public sealed class ProcessIncomingEmailsHandlerQuotaTests
{
    [Fact]
    public async Task Handler_quarantines_remaining_when_aggregate_exceeds_50MB()
    {
        // Each mail has 20MB attachments -> 3 mails would be 60MB, so third should be quota-quarantined
        var twentyMb = 20L * 1024 * 1024;
        var mail1 = MailWithAttachments("fake\\m1", twentyMb);
        var mail2 = MailWithAttachments("fake\\m2", twentyMb);
        var mail3 = MailWithAttachments("fake\\m3", twentyMb);

        var receiver = new FakeReceiver([mail1, mail2, mail3]);
        var factory = new ScriptedFactory([
            new InboundEmailItemResult(InboundEmailItemOutcome.CreatedTicket, "<id1>", "VS-000001", false, false, false, null),
            new InboundEmailItemResult(InboundEmailItemOutcome.CreatedTicket, "<id2>", "VS-000002", false, false, false, null)
        ]);

        var handler = CreateHandler(receiver, factory);
        var result = await handler.HandleAsync(new ProcessIncomingEmailsCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        // Only first 2 should be processed, third quarantined by quota
        Assert.Equal(2, factory.ProcessCallCount);
        Assert.Equal(1, result.Value!.Quarantined);
        Assert.Contains(result.Value.Failures, f => f.Code == "aggregate-quota-exceeded");
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
        // Single mail with 60MB > 50MB limit should be quarantined directly
        var sixtyMb = 60L * 1024 * 1024;
        var mail = MailWithAttachments("fake\\huge", sixtyMb);
        var receiver = new FakeReceiver([mail]);
        var factory = new ScriptedFactory([]);

        var handler = CreateHandler(receiver, factory);
        var result = await handler.HandleAsync(new ProcessIncomingEmailsCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, factory.ProcessCallCount);
        Assert.Equal(1, result.Value!.Quarantined);
        Assert.Contains(result.Value.Failures, f => f.Code == "aggregate-quota-exceeded");
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

    private static ProcessIncomingEmailsHandler CreateHandler(IEmailReceiver receiver, IInboundEmailItemProcessorFactory factory) =>
        new(
            receiver,
            new FixedSettings(),
            factory,
            new AlwaysEnterGate(),
            new StubMessageProvider(),
            NullLogger<ProcessIncomingEmailsHandler>.Instance);

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
        public Task<IReadOnlyList<IncomingEmail>> FetchUnreadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(messages);
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
