using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using VSHelpDesk.Application.Abstractions.Email;
using VSHelpDesk.Application.Common.Exceptions;
using VSHelpDesk.Application.Features.MailProcessing;
using VSHelpDesk.Application.Features.MailProcessing.Acknowledgements;
using VSHelpDesk.Application.Features.MailProcessing.ProcessIncomingEmails;

namespace VSHelpDesk.Application.UnitTests.Features.MailProcessing;

public sealed class ProcessIncomingEmailsHandlerTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 7, 31, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Stream_ProcessesPriorItemBeforeRequestingNext()
    {
        var first = Mail(
            "<lazy-1@test>",
            "first@example.test",
            "First",
            "Body",
            "fake\0lazy-1");
        var second = Mail(
            "<lazy-2@test>",
            "second@example.test",
            "Second",
            "Body",
            "fake\0lazy-2");
        var state = new LazyConsumptionState();
        var receiver = new LazyGuardReceiver([first, second], state);
        var factory = new LazyGuardFactory(state);
        var handler = CreateHandler(receiver, factory);

        var result = await handler.HandleAsync(
            new ProcessIncomingEmailsCommand(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.FetchedCount);
        Assert.Equal(1, receiver.MaximumOutstandingItems);
        Assert.Equal(2, factory.ProcessCallCount);
    }

    [Fact]
    public async Task PoisonFirstReceipt_DoesNotStopLaterValidReceipt()
    {
        var poison = Mail("<poison@test>", null, "Bad", "x", "fake\0poison");
        var valid = Mail("<valid@test>", "customer@example.test", "Help", "Body", "fake\0valid");
        var receiver = new FakeReceiver([poison, valid]);
        var factory = new ScriptedFactory(
        [
            new InboundEmailItemResult(
                InboundEmailItemOutcome.Quarantined,
                IdempotencyKey: "<poison@test>",
                TicketNumber: null,
                WasReopened: false,
                AcknowledgementSent: false,
                AcknowledgementFailed: false,
                FailureCode: null),
            new InboundEmailItemResult(
                InboundEmailItemOutcome.CreatedTicket,
                IdempotencyKey: "<valid@test>",
                TicketNumber: "VS-000501",
                WasReopened: false,
                AcknowledgementSent: true,
                AcknowledgementFailed: false,
                FailureCode: null)
        ]);
        var handler = CreateHandler(receiver, factory);

        var result = await handler.HandleAsync(new ProcessIncomingEmailsCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.FetchedCount);
        Assert.Equal(1, result.Value.CreatedTickets);
        Assert.Equal(0, result.Value.CustomerReplies);
        Assert.Equal(0, result.Value.ReopenedTickets);
        Assert.Equal(0, result.Value.AlreadyProcessed);
        Assert.Equal(1, result.Value.Quarantined);
        Assert.Equal(0, result.Value.RetryableFailures);
        Assert.Equal(1, result.Value.AcknowledgementsSent);
        Assert.Equal(0, result.Value.AcknowledgementsFailed);
        Assert.Equal(["VS-000501"], result.Value.CreatedTicketNumbers);
        Assert.Empty(result.Value.Failures);
        Assert.Equal(
            [poison.ReceiptHandle.Value, valid.ReceiptHandle.Value],
            receiver.Marked.Select(handle => handle.Value).ToArray());
        Assert.Equal(2, factory.ProcessCallCount);
    }

    [Fact]
    public async Task UnexpectedFirstDatabaseFailure_DoesNotReuseScopeOrStopLaterReceipt()
    {
        var first = Mail("<boom@test>", "a@example.test", "Boom", "x", "fake\0boom");
        var second = Mail("<ok@test>", "b@example.test", "Ok", "y", "fake\0ok");
        var receiver = new FakeReceiver([first, second]);
        var factory = new ThrowingThenSuccessFactory(
            throwOnFirstProcess: true,
            success: new InboundEmailItemResult(
                InboundEmailItemOutcome.CreatedTicket,
                IdempotencyKey: "<ok@test>",
                TicketNumber: "VS-000502",
                WasReopened: false,
                AcknowledgementSent: true,
                AcknowledgementFailed: false,
                FailureCode: null));
        var handler = CreateHandler(receiver, factory);

        var result = await handler.HandleAsync(new ProcessIncomingEmailsCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.CreatedTickets);
        Assert.Equal(1, result.Value.RetryableFailures);
        Assert.Equal(["VS-000502"], result.Value.CreatedTicketNumbers);
        var failure = Assert.Single(result.Value.Failures);
        Assert.Equal("processing-failed", failure.Code);
        Assert.Equal(ToItemReference(first.ReceiptHandle), failure.ItemReference);
        Assert.DoesNotContain(first.ReceiptHandle.Value, failure.ItemReference, StringComparison.Ordinal);
        Assert.Equal([second.ReceiptHandle.Value], receiver.Marked.Select(h => h.Value).ToArray());
        Assert.Equal(2, factory.ProcessCallCount);
        Assert.Equal(2, factory.DistinctProcessScopes);
    }

    [Fact]
    public async Task SmtpFailure_MarksReceiptBecauseRetryStateIsDurable()
    {
        var mail = Mail("<ack-fail@test>", "customer@example.test", "Help", "Body", "fake\0ack-fail");
        var receiver = new FakeReceiver([mail]);
        var factory = new ScriptedFactory(
        [
            new InboundEmailItemResult(
                InboundEmailItemOutcome.CreatedTicket,
                IdempotencyKey: "<ack-fail@test>",
                TicketNumber: "VS-000503",
                WasReopened: false,
                AcknowledgementSent: false,
                AcknowledgementFailed: true,
                FailureCode: null)
        ]);
        var handler = CreateHandler(receiver, factory);

        var result = await handler.HandleAsync(new ProcessIncomingEmailsCommand(), CancellationToken.None);

        Assert.Equal(1, result.Value!.CreatedTickets);
        Assert.Equal(0, result.Value.AcknowledgementsSent);
        Assert.Equal(1, result.Value.AcknowledgementsFailed);
        Assert.Equal(0, result.Value.RetryableFailures);
        Assert.Empty(result.Value.Failures);
        Assert.Equal([mail.ReceiptHandle.Value], receiver.Marked.Select(h => h.Value).ToArray());
    }

    [Fact]
    public async Task MarkSeenFailure_IsReportedAndLaterReceiptStillRuns()
    {
        var first = Mail("<mark-fail@test>", "a@example.test", "One", "x", "fake\0mark-fail");
        var second = Mail("<mark-ok@test>", "b@example.test", "Two", "y", "fake\0mark-ok");
        var receiver = new FakeReceiver([first, second])
        {
            ThrowOnMarkValues = new HashSet<string>(StringComparer.Ordinal) { first.ReceiptHandle.Value }
        };
        var factory = new ScriptedFactory(
        [
            new InboundEmailItemResult(
                InboundEmailItemOutcome.CreatedTicket,
                IdempotencyKey: "<mark-fail@test>",
                TicketNumber: "VS-000504",
                WasReopened: false,
                AcknowledgementSent: true,
                AcknowledgementFailed: false,
                FailureCode: null),
            new InboundEmailItemResult(
                InboundEmailItemOutcome.CreatedTicket,
                IdempotencyKey: "<mark-ok@test>",
                TicketNumber: "VS-000505",
                WasReopened: false,
                AcknowledgementSent: true,
                AcknowledgementFailed: false,
                FailureCode: null)
        ]);
        var handler = CreateHandler(receiver, factory);

        var result = await handler.HandleAsync(new ProcessIncomingEmailsCommand(), CancellationToken.None);

        Assert.Equal(2, result.Value!.CreatedTickets);
        Assert.Equal(0, result.Value.RetryableFailures);
        Assert.Equal(["VS-000504", "VS-000505"], result.Value.CreatedTicketNumbers);
        var failure = Assert.Single(result.Value.Failures);
        Assert.Equal("mark-seen-failed", failure.Code);
        Assert.Equal(ToItemReference(first.ReceiptHandle), failure.ItemReference);
        Assert.Equal([second.ReceiptHandle.Value], receiver.Marked.Select(h => h.Value).ToArray());
        Assert.Equal(2, factory.ProcessCallCount);
    }

    [Fact]
    public async Task RetryableFailure_DoesNotMarkReceipt()
    {
        var mail = Mail("<retry@test>", "customer@example.test", "Help", "Body", "fake\0retry");
        var receiver = new FakeReceiver([mail]);
        var factory = new ScriptedFactory(
        [
            new InboundEmailItemResult(
                InboundEmailItemOutcome.RetryableFailure,
                IdempotencyKey: "<retry@test>",
                TicketNumber: null,
                WasReopened: false,
                AcknowledgementSent: false,
                AcknowledgementFailed: false,
                FailureCode: "ticket-concurrency")
        ]);
        var handler = CreateHandler(receiver, factory);

        var result = await handler.HandleAsync(new ProcessIncomingEmailsCommand(), CancellationToken.None);

        Assert.Equal(0, result.Value!.CreatedTickets);
        Assert.Equal(1, result.Value.RetryableFailures);
        Assert.Empty(result.Value.CreatedTicketNumbers);
        var failure = Assert.Single(result.Value.Failures);
        Assert.Equal("ticket-concurrency", failure.Code);
        Assert.Equal(ToItemReference(mail.ReceiptHandle), failure.ItemReference);
        Assert.Empty(receiver.Marked);
    }

    [Fact]
    public async Task InvalidSender_IsQuarantinedAndMarkedByReceipt()
    {
        var mail = Mail("<bad@test>", "not-valid", "Poison", "x", "fake\0bad");
        var receiver = new FakeReceiver([mail]);
        var factory = new ScriptedFactory(
        [
            new InboundEmailItemResult(
                InboundEmailItemOutcome.Quarantined,
                IdempotencyKey: "<bad@test>",
                TicketNumber: null,
                WasReopened: false,
                AcknowledgementSent: false,
                AcknowledgementFailed: false,
                FailureCode: null)
        ]);
        var handler = CreateHandler(receiver, factory);

        var result = await handler.HandleAsync(new ProcessIncomingEmailsCommand(), CancellationToken.None);

        Assert.Equal(1, result.Value!.Quarantined);
        Assert.Equal(0, result.Value.CreatedTickets);
        Assert.Equal(0, result.Value.RetryableFailures);
        Assert.Empty(result.Value.Failures);
        Assert.Equal([mail.ReceiptHandle.Value], receiver.Marked.Select(h => h.Value).ToArray());
    }

    [Fact]
    public async Task AlreadyProcessed_IsMarkedAndCounted()
    {
        var mail = Mail("<dup@test>", "customer@example.test", "Dup", "Body", "fake\0dup");
        var receiver = new FakeReceiver([mail]);
        var factory = new ScriptedFactory(
        [
            new InboundEmailItemResult(
                InboundEmailItemOutcome.AlreadyProcessed,
                IdempotencyKey: "<dup@test>",
                TicketNumber: "VS-000510",
                WasReopened: false,
                AcknowledgementSent: false,
                AcknowledgementFailed: false,
                FailureCode: null)
        ]);
        var handler = CreateHandler(receiver, factory);

        var result = await handler.HandleAsync(new ProcessIncomingEmailsCommand(), CancellationToken.None);

        Assert.Equal(1, result.Value!.AlreadyProcessed);
        Assert.Equal(0, result.Value.CreatedTickets);
        Assert.Equal([mail.ReceiptHandle.Value], receiver.Marked.Select(h => h.Value).ToArray());
    }

    [Fact]
    public async Task AppendedReply_AndReopen_AreCountedSeparately()
    {
        var reply = Mail("<r1@test>", "a@example.test", "Re: [VS-1]", "x", "fake\0r1");
        var reopen = Mail("<r2@test>", "b@example.test", "Re: [VS-2]", "y", "fake\0r2");
        var receiver = new FakeReceiver([reply, reopen]);
        var factory = new ScriptedFactory(
        [
            new InboundEmailItemResult(
                InboundEmailItemOutcome.AppendedReply,
                IdempotencyKey: "<r1@test>",
                TicketNumber: "VS-000001",
                WasReopened: false,
                AcknowledgementSent: false,
                AcknowledgementFailed: false,
                FailureCode: null),
            new InboundEmailItemResult(
                InboundEmailItemOutcome.AppendedReply,
                IdempotencyKey: "<r2@test>",
                TicketNumber: "VS-000002",
                WasReopened: true,
                AcknowledgementSent: false,
                AcknowledgementFailed: false,
                FailureCode: null)
        ]);
        var handler = CreateHandler(receiver, factory);

        var result = await handler.HandleAsync(new ProcessIncomingEmailsCommand(), CancellationToken.None);

        Assert.Equal(2, result.Value!.CustomerReplies);
        Assert.Equal(1, result.Value.ReopenedTickets);
        Assert.Equal(0, result.Value.AcknowledgementsSent);
        Assert.Equal(
            [reply.ReceiptHandle.Value, reopen.ReceiptHandle.Value],
            receiver.Marked.Select(h => h.Value).ToArray());
    }

    [Fact]
    public async Task RetryDueAcknowledgements_AreAddedToJobCounters()
    {
        var receiver = new FakeReceiver([]);
        var factory = new ScriptedFactory([])
        {
            RetrySummary = new AcknowledgementDispatchSummary(Attempted: 3, Sent: 2, Failed: 1)
        };
        var handler = CreateHandler(receiver, factory);

        var result = await handler.HandleAsync(new ProcessIncomingEmailsCommand(), CancellationToken.None);

        Assert.Equal(0, result.Value!.FetchedCount);
        Assert.Equal(2, result.Value.AcknowledgementsSent);
        Assert.Equal(1, result.Value.AcknowledgementsFailed);
        Assert.Equal(1, factory.RetryCallCount);
    }

    [Fact]
    public async Task GateBusy_ThrowsJobAlreadyRunningException()
    {
        var handler = new ProcessIncomingEmailsHandler(
            new FakeReceiver([]),
            new FixedSettings(),
            new ScriptedFactory([]),
            new BusyGate(),
            NullLogger<ProcessIncomingEmailsHandler>.Instance);

        var exception = await Assert.ThrowsAsync<JobAlreadyRunningException>(() =>
            handler.HandleAsync(new ProcessIncomingEmailsCommand(), CancellationToken.None));

        Assert.Equal(
            "The 'process-incoming-emails' job is already running.",
            exception.Message);
    }

    [Fact]
    public async Task Failures_NeverIncludeRawReceiptOrCustomerData()
    {
        var receiptValue = "fake\0secret-uid-999";
        var mail = Mail("<x@test>", "customer@example.test", "Help", "secret body", receiptValue);
        var receiver = new FakeReceiver([mail]);
        var factory = new ThrowingThenSuccessFactory(throwOnFirstProcess: true, success: null!);
        var handler = CreateHandler(receiver, factory);

        var result = await handler.HandleAsync(new ProcessIncomingEmailsCommand(), CancellationToken.None);

        var failure = Assert.Single(result.Value!.Failures);
        Assert.Equal("processing-failed", failure.Code);
        Assert.Equal(ToItemReference(mail.ReceiptHandle), failure.ItemReference);
        Assert.DoesNotContain(receiptValue, failure.ItemReference, StringComparison.Ordinal);
        Assert.DoesNotContain("customer@example.test", failure.ItemReference, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", failure.ItemReference, StringComparison.Ordinal);
        Assert.DoesNotContain("<x@test>", failure.ItemReference, StringComparison.Ordinal);
    }

    private static ProcessIncomingEmailsHandler CreateHandler(
        IEmailReceiver receiver,
        IInboundEmailItemProcessorFactory factory) =>
        new(
            receiver,
            new FixedSettings(),
            factory,
            new AlwaysEnterGate(),
            NullLogger<ProcessIncomingEmailsHandler>.Instance);

    private static IncomingEmail Mail(
        string? id,
        string? from,
        string subject,
        string body,
        string receiptValue) =>
        new(
            MessageId: id,
            ReceiptHandle: new EmailReceiptHandle(EmailReceiptKind.Fake, receiptValue),
            FromAddress: from,
            FromDisplayName: "Customer",
            Subject: subject,
            Body: body,
            IsHtml: false,
            ReceivedAt: FixedNow.UtcDateTime,
            Attachments: Array.Empty<IncomingEmailAttachment>());

    private static string ToItemReference(EmailReceiptHandle handle) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(handle.Value)))
            .ToLowerInvariant();

    private sealed class FixedSettings : IEmailBoundarySettings
    {
        public string ReceiverMode => "Fake";
        public string SupportMailboxAddress => "support@vshelpdesk.local";
        public string SupportMailboxDisplayName => "VS Help Desk";
    }

    private sealed class AlwaysEnterGate : IProcessIncomingEmailsGate
    {
        public Task<IProcessIncomingEmailsLease?> TryAcquireAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IProcessIncomingEmailsLease?>(new NoopLease());

        private sealed class NoopLease : IProcessIncomingEmailsLease
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class BusyGate : IProcessIncomingEmailsGate
    {
        public Task<IProcessIncomingEmailsLease?> TryAcquireAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IProcessIncomingEmailsLease?>(null);
    }

    private sealed class FakeReceiver(IReadOnlyList<IncomingEmail> messages) : IEmailReceiver
    {
        public List<EmailReceiptHandle> Marked { get; } = [];
        public HashSet<string> ThrowOnMarkValues { get; init; } = new(StringComparer.Ordinal);

        public async IAsyncEnumerable<IncomingEmail> ReadUnreadAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken = default)
        {
            foreach (var message in messages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return message;
                await Task.Yield();
            }
        }

        public Task MarkAsProcessedAsync(
            EmailReceiptHandle receiptHandle,
            CancellationToken cancellationToken = default)
        {
            if (ThrowOnMarkValues.Contains(receiptHandle.Value))
            {
                throw new InvalidOperationException("IMAP STORE failed");
            }

            Marked.Add(receiptHandle);
            return Task.CompletedTask;
        }
    }

    private sealed class LazyConsumptionState
    {
        public bool ProcessorActive { get; set; }
    }

    private sealed class LazyGuardReceiver(
        IReadOnlyList<IncomingEmail> messages,
        LazyConsumptionState state) : IEmailReceiver
    {
        private int outstandingItems;

        public int MaximumOutstandingItems { get; private set; }

        public async IAsyncEnumerable<IncomingEmail> ReadUnreadAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken = default)
        {
            foreach (var message in messages)
            {
                if (state.ProcessorActive)
                {
                    throw new InvalidOperationException(
                        "The next item was requested before processing completed.");
                }

                cancellationToken.ThrowIfCancellationRequested();
                outstandingItems++;
                MaximumOutstandingItems = Math.Max(
                    MaximumOutstandingItems,
                    outstandingItems);
                try
                {
                    yield return message;
                }
                finally
                {
                    outstandingItems--;
                }

                await Task.Yield();
            }
        }

        public Task MarkAsProcessedAsync(
            EmailReceiptHandle receiptHandle,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class LazyGuardFactory(LazyConsumptionState state)
        : IInboundEmailItemProcessorFactory
    {
        public int ProcessCallCount { get; private set; }

        public async Task<InboundEmailItemResult> ProcessAsync(
            IncomingEmail email,
            CancellationToken cancellationToken)
        {
            Assert.False(state.ProcessorActive);
            state.ProcessorActive = true;
            try
            {
                ProcessCallCount++;
                await Task.Yield();
                return new InboundEmailItemResult(
                    InboundEmailItemOutcome.AlreadyProcessed,
                    email.MessageId ?? "receipt",
                    TicketNumber: null,
                    WasReopened: false,
                    AcknowledgementSent: false,
                    AcknowledgementFailed: false,
                    FailureCode: null);
            }
            finally
            {
                state.ProcessorActive = false;
            }
        }

        public Task<AcknowledgementDispatchSummary>
            RetryDueAcknowledgementsAsync(
                CancellationToken cancellationToken) =>
            Task.FromResult(
                new AcknowledgementDispatchSummary(0, 0, 0));
    }

    private sealed class ScriptedFactory(IReadOnlyList<InboundEmailItemResult> results)
        : IInboundEmailItemProcessorFactory
    {
        private int processIndex;

        public int ProcessCallCount { get; private set; }
        public int RetryCallCount { get; private set; }
        public AcknowledgementDispatchSummary RetrySummary { get; init; } =
            new(Attempted: 0, Sent: 0, Failed: 0);

        public Task<InboundEmailItemResult> ProcessAsync(
            IncomingEmail email,
            CancellationToken cancellationToken)
        {
            ProcessCallCount++;
            return Task.FromResult(results[processIndex++]);
        }

        public Task<AcknowledgementDispatchSummary> RetryDueAcknowledgementsAsync(
            CancellationToken cancellationToken)
        {
            RetryCallCount++;
            return Task.FromResult(RetrySummary);
        }
    }

    private sealed class ThrowingThenSuccessFactory(
        bool throwOnFirstProcess,
        InboundEmailItemResult? success) : IInboundEmailItemProcessorFactory
    {
        private int processIndex;

        public int ProcessCallCount { get; private set; }
        public int DistinctProcessScopes { get; private set; }

        public Task<InboundEmailItemResult> ProcessAsync(
            IncomingEmail email,
            CancellationToken cancellationToken)
        {
            ProcessCallCount++;
            DistinctProcessScopes++;
            if (throwOnFirstProcess && processIndex++ == 0)
            {
                throw new InvalidOperationException("database unavailable");
            }

            return Task.FromResult(success
                ?? throw new InvalidOperationException("no success result configured"));
        }

        public Task<AcknowledgementDispatchSummary> RetryDueAcknowledgementsAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(new AcknowledgementDispatchSummary(0, 0, 0));
    }
}
