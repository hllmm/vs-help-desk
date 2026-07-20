using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using VSHelpDesk.Application.Abstractions.Authentication;
using VSHelpDesk.Application.Abstractions.Email;
using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Application.Common.Exceptions;
using VSHelpDesk.Application.Features.Tickets.ReplyToTicket;
using VSHelpDesk.Domain.Entities;
using VSHelpDesk.Domain.Enums;

namespace VSHelpDesk.Application.UnitTests.Features.Tickets.ReplyToTicket;

public sealed class SupportReplyToTicketHandlerTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 4, 11, 0, 0, TimeSpan.Zero);
    private static readonly Guid SupportUserId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    [Fact]
    public async Task ExactLimit_PersistsAndSendsPlainText()
    {
        var ticket = CreateCustomerRepliedTicket("VS-000301");
        var db = new FakeDb(ticket);
        var sender = new RecordingSender();
        var handler = CreateHandler(db, sender);
        var content = new string('x', SupportReplyLimits.MaxContentLength);

        var result = await handler.HandleAsync(
            new SupportReplyToTicketCommand(ticket.Id, $"  {content}  "),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.EmailDelivered);
        Assert.True(result.Value.TicketStateUpdated);
        Assert.Null(result.Value.NoticeCode);
        Assert.Equal(nameof(TicketStatus.WaitingCustomerReply), result.Value.Status);
        Assert.Equal(TicketStatus.WaitingCustomerReply, ticket.Status);
        Assert.Single(db.Messages);
        Assert.Equal(MessageSenderType.Support, db.Messages[0].SenderType);
        Assert.False(db.Messages[0].IsHtml);
        Assert.Equal(SupportUserId, db.Messages[0].UserId);
        Assert.Equal(content, db.Messages[0].Content);
        Assert.Single(sender.Sent);
        Assert.False(sender.Sent[0].IsHtml);
        Assert.Equal(content, sender.Sent[0].Body);
    }

    [Fact]
    public async Task OverLimit_ReturnsReplyContentTooLongWithoutSavingOrSending()
    {
        var ticket = CreateCustomerRepliedTicket("VS-000302");
        var db = new FakeDb(ticket);
        var sender = new RecordingSender();
        var handler = CreateHandler(db, sender);
        var content = new string('x', SupportReplyLimits.MaxContentLength + 1);

        var result = await handler.HandleAsync(
            new SupportReplyToTicketCommand(ticket.Id, content),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(SupportReplyCodes.ContentTooLong, result.Error);
        Assert.Empty(db.Messages);
        Assert.Empty(sender.Sent);
        Assert.Equal(0, db.SaveCallCount);
    }

    [Fact]
    public async Task Blank_ReturnsReplyContentRequiredWithoutSavingOrSending()
    {
        var ticket = CreateCustomerRepliedTicket("VS-000303");
        var db = new FakeDb(ticket);
        var sender = new RecordingSender();
        var handler = CreateHandler(db, sender);

        var result = await handler.HandleAsync(
            new SupportReplyToTicketCommand(ticket.Id, "   \t  "),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(SupportReplyCodes.ContentRequired, result.Error);
        Assert.Empty(db.Messages);
        Assert.Empty(sender.Sent);
        Assert.Equal(0, db.SaveCallCount);
    }

    [Fact]
    public async Task SmtpFailure_KeepsMessageAndReturnsSafeNoticeCode()
    {
        var ticket = CreateCustomerRepliedTicket("VS-000304");
        var db = new FakeDb(ticket);
        var sender = new RecordingSender { ThrowMessage = "SMTP down: password=secret" };
        var handler = CreateHandler(db, sender);
        const string replyContent = "We enabled VPN on your account.";

        var result = await handler.HandleAsync(
            new SupportReplyToTicketCommand(ticket.Id, replyContent),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.EmailDelivered);
        Assert.False(result.Value.TicketStateUpdated);
        Assert.Equal(SupportReplyCodes.SmtpDeliveryFailed, result.Value.NoticeCode);
        Assert.Equal(nameof(TicketStatus.CustomerReplied), result.Value.Status);
        Assert.Equal(TicketStatus.CustomerReplied, ticket.Status);
        Assert.Single(db.Messages);
        Assert.Equal(MessageSenderType.Support, db.Messages[0].SenderType);
        Assert.False(db.Messages[0].IsHtml);
        Assert.Equal(SupportUserId, db.Messages[0].UserId);
        Assert.Equal(replyContent, db.Messages[0].Content);
        Assert.Empty(sender.Sent);

        AssertNoSensitiveLeak(
            result.Value,
            forbidden: ["password", "secret", "SMTP down", "ada@example.test", replyContent]);
    }

    [Fact]
    public async Task FirstSaveConflict_PropagatesAndNeverSends()
    {
        var ticket = CreateCustomerRepliedTicket("VS-000305");
        var db = new FakeDb(ticket, conflictOnSaveCalls: [1]);
        var sender = new RecordingSender();
        var handler = CreateHandler(db, sender);

        await Assert.ThrowsAsync<OptimisticConcurrencyException>(() =>
            handler.HandleAsync(
                new SupportReplyToTicketCommand(ticket.Id, "Please try again."),
                CancellationToken.None));

        Assert.Empty(db.Messages);
        Assert.Empty(sender.Sent);
        Assert.Equal(1, db.SaveCallCount);
    }

    [Fact]
    public async Task StateSaveConflict_ReloadsRetriesAndSendsExactlyOnce()
    {
        var ticket = CreateCustomerRepliedTicket("VS-000306");
        var db = new FakeDb(ticket, conflictOnSaveCalls: [2]);
        var sender = new RecordingSender();
        var handler = CreateHandler(db, sender);

        var result = await handler.HandleAsync(
            new SupportReplyToTicketCommand(ticket.Id, "Restart the printer."),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.EmailDelivered);
        Assert.True(result.Value.TicketStateUpdated);
        Assert.Null(result.Value.NoticeCode);
        Assert.Equal(nameof(TicketStatus.WaitingCustomerReply), result.Value.Status);
        Assert.Single(db.Messages);
        Assert.False(db.Messages[0].IsHtml);
        Assert.Equal(SupportUserId, db.Messages[0].UserId);
        Assert.Equal("Restart the printer.", db.Messages[0].Content);
        Assert.Single(sender.Sent);
        Assert.Equal(3, db.SaveCallCount);
        Assert.Equal(1, db.ClearTrackedCallCount);
        Assert.Equal(TicketStatus.WaitingCustomerReply, db.PersistedStatus);
    }

    [Fact]
    public async Task SecondStateSaveConflict_ReturnsNoticeAndSendsExactlyOnce()
    {
        var ticket = CreateCustomerRepliedTicket("VS-000307");
        var db = new FakeDb(ticket, conflictOnSaveCalls: [2, 3]);
        var sender = new RecordingSender();
        var handler = CreateHandler(db, sender);

        var result = await handler.HandleAsync(
            new SupportReplyToTicketCommand(ticket.Id, "Status remains customer replied."),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.EmailDelivered);
        Assert.False(result.Value.TicketStateUpdated);
        Assert.Equal(SupportReplyCodes.TicketStateConflict, result.Value.NoticeCode);
        Assert.Equal(nameof(TicketStatus.CustomerReplied), result.Value.Status);
        Assert.Single(db.Messages);
        Assert.False(db.Messages[0].IsHtml);
        Assert.Single(sender.Sent);
        Assert.Equal(3, db.SaveCallCount);
        Assert.Equal(2, db.ClearTrackedCallCount);
        Assert.Equal(TicketStatus.CustomerReplied, db.PersistedStatus);
    }

    [Fact]
    public async Task StateSaveConflict_WhenConcurrentResolve_ReturnsTicketStateConflictWithoutDomainException()
    {
        // Save 1 = message; save 2 = waiting-state (conflict). Reload snapshot is Resolved
        // (concurrent manual/auto resolve won), so waiting mutation must not throw DomainException.
        var ticket = CreateCustomerRepliedTicket("VS-000310");
        var db = new FakeDb(
            ticket,
            conflictOnSaveCalls: [2],
            concurrentResolveAfterMessageSave: true);
        var sender = new RecordingSender();
        var handler = CreateHandler(db, sender);

        var result = await handler.HandleAsync(
            new SupportReplyToTicketCommand(ticket.Id, "Reply already emailed."),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.EmailDelivered);
        Assert.False(result.Value.TicketStateUpdated);
        Assert.Equal(SupportReplyCodes.TicketStateConflict, result.Value.NoticeCode);
        Assert.Equal(nameof(TicketStatus.Resolved), result.Value.Status);
        Assert.Single(db.Messages);
        Assert.False(db.Messages[0].IsHtml);
        Assert.Equal(SupportUserId, db.Messages[0].UserId);
        Assert.Single(sender.Sent);
        Assert.Equal(2, db.SaveCallCount);
        Assert.Equal(1, db.ClearTrackedCallCount);
        Assert.Equal(TicketStatus.Resolved, db.PersistedStatus);
    }

    [Fact]
    public async Task CancellationAfterMessageSave_PropagatesInsteadOfClaimingSmtpFailure()
    {
        var ticket = CreateCustomerRepliedTicket("VS-000308");
        var db = new FakeDb(ticket);
        using var cts = new CancellationTokenSource();
        var sender = new RecordingSender
        {
            OnSend = () => cts.Cancel(),
            ObserveToken = true
        };
        var handler = CreateHandler(db, sender);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            handler.HandleAsync(
                new SupportReplyToTicketCommand(ticket.Id, "Cancelled mid-send."),
                cts.Token));

        Assert.Single(db.Messages);
        Assert.Equal(1, db.SaveCallCount);
        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task ResolvedTicket_ThrowsConflictBeforeMessageSaveOrSmtp()
    {
        var ticket = Ticket.Create(
            "VS-000309",
            "Resolved reply",
            "Ada",
            "ada@example.test",
            FixedNow.UtcDateTime.AddHours(-2));
        var resolvedAt = FixedNow.UtcDateTime.AddHours(-1);
        Assert.True(ticket.ResolveManually(resolvedAt, SupportUserId));
        var originalUpdatedAt = ticket.UpdatedAt;
        var originalLastActivityAt = ticket.LastActivityAt;
        var db = new FakeDb(ticket);
        var sender = new RecordingSender();
        var handler = CreateHandler(db, sender);

        await Assert.ThrowsAsync<ResolvedTicketReplyException>(() =>
            handler.HandleAsync(
                new SupportReplyToTicketCommand(ticket.Id, "Should not persist."),
                CancellationToken.None));

        Assert.Empty(db.Messages);
        Assert.Empty(sender.Sent);
        Assert.Equal(0, db.SaveCallCount);
        Assert.Equal(TicketStatus.Resolved, ticket.Status);
        Assert.Equal(resolvedAt, ticket.ResolvedAt);
        Assert.Equal(SupportUserId, ticket.ClosedByUserId);
        Assert.Equal(originalUpdatedAt, ticket.UpdatedAt);
        Assert.Equal(originalLastActivityAt, ticket.LastActivityAt);
    }

    private static Ticket CreateCustomerRepliedTicket(string ticketNumber)
    {
        var ticket = Ticket.Create(
            ticketNumber,
            "Printer",
            "Ada",
            "ada@example.test",
            FixedNow.UtcDateTime);
        ticket.MarkAsCustomerReplied(FixedNow.UtcDateTime);
        return ticket;
    }

    private static void AssertNoSensitiveLeak(
        SupportReplyToTicketResult value,
        IEnumerable<string> forbidden)
    {
        var payload = string.Join(
            '|',
            value.TicketId,
            value.TicketNumber,
            value.MessageId,
            value.Status,
            value.EmailDelivered,
            value.TicketStateUpdated,
            value.NoticeCode ?? string.Empty);

        foreach (var term in forbidden)
        {
            Assert.DoesNotContain(term, payload, StringComparison.OrdinalIgnoreCase);
        }

        foreach (var property in typeof(SupportReplyToTicketResult).GetProperties())
        {
            var raw = property.GetValue(value)?.ToString() ?? string.Empty;
            foreach (var term in forbidden)
            {
                Assert.DoesNotContain(term, raw, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    private static SupportReplyToTicketHandler CreateHandler(FakeDb db, IEmailSender sender) =>
        new(
            db,
            sender,
            new FixedCurrentUser(),
            new FixedTimeProvider(FixedNow),
            NullLogger<SupportReplyToTicketHandler>.Instance);

    private sealed class FixedCurrentUser : ICurrentUserService
    {
        public Guid? UserId => SupportUserId;
        public bool IsAuthenticated => true;
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class RecordingSender : IEmailSender
    {
        public string? ThrowMessage { get; init; }
        public Action? OnSend { get; init; }
        public bool ObserveToken { get; init; }
        public List<EmailMessage> Sent { get; } = [];

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            OnSend?.Invoke();
            if (ObserveToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (ThrowMessage is not null)
            {
                throw new InvalidOperationException(ThrowMessage);
            }

            Sent.Add(message);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Save-call map: 1 = message/activity, 2 = first waiting-state, 3 = reloaded waiting retry.
    /// <see cref="ClearTrackedChanges"/> switches the query to a fresh persisted snapshot so a
    /// failed tracked waiting-state mutation cannot masquerade as a saved status.
    /// </summary>
    private sealed class FakeDb : IApplicationDbContext
    {
        private readonly HashSet<int> conflictOnSaveCalls;
        private readonly List<object> pending = [];
        private Ticket queryTicket;
        private TicketStatus persistedStatus;
        private DateTime? persistedWaitingSince;
        private DateTime persistedUpdatedAt;
        private DateTime persistedLastActivityAt;

        private readonly bool concurrentResolveAfterMessageSave;

        public FakeDb(
            Ticket ticket,
            IEnumerable<int>? conflictOnSaveCalls = null,
            bool concurrentResolveAfterMessageSave = false)
        {
            queryTicket = ticket;
            this.conflictOnSaveCalls = conflictOnSaveCalls?.ToHashSet() ?? [];
            this.concurrentResolveAfterMessageSave = concurrentResolveAfterMessageSave;
            CapturePersistedSnapshot(ticket);
        }

        public List<TicketMessage> Messages { get; } = [];
        public int SaveCallCount { get; private set; }
        public int ClearTrackedCallCount { get; private set; }
        public TicketStatus PersistedStatus => persistedStatus;

        public IQueryable<User> Users => Array.Empty<User>().AsQueryable();
        public IQueryable<Ticket> Tickets => new[] { queryTicket }.AsQueryable();
        public IQueryable<TicketMessage> TicketMessages => Messages.AsQueryable();
        public IQueryable<TicketAttachment> TicketAttachments =>
            Array.Empty<TicketAttachment>().AsQueryable();
        public IQueryable<ProcessedEmailMessage> ProcessedEmailMessages =>
            Array.Empty<ProcessedEmailMessage>().AsQueryable();

        public void Add<TEntity>(TEntity entity) where TEntity : class => pending.Add(entity!);

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCallCount++;
            if (conflictOnSaveCalls.Contains(SaveCallCount))
            {
                throw new OptimisticConcurrencyException(
                    $"Simulated concurrency conflict on save call {SaveCallCount}.");
            }

            foreach (var entity in pending)
            {
                if (entity is TicketMessage message)
                {
                    Messages.Add(message);
                }
            }

            pending.Clear();
            CapturePersistedSnapshot(queryTicket);

            // After the message commit succeeds, a concurrent resolve can win the DB race
            // before our waiting-state save — reload must see Resolved.
            if (concurrentResolveAfterMessageSave && SaveCallCount == 1)
            {
                persistedStatus = TicketStatus.Resolved;
                persistedWaitingSince = null;
            }

            return Task.FromResult(1);
        }

        public void ClearTrackedChanges()
        {
            ClearTrackedCallCount++;
            pending.Clear();
            // Fresh snapshot with last successfully persisted state only.
            queryTicket = CloneWithPersistedState(queryTicket);
        }

        private void CapturePersistedSnapshot(Ticket ticket)
        {
            persistedStatus = ticket.Status;
            persistedWaitingSince = ticket.WaitingCustomerSince;
            persistedUpdatedAt = ticket.UpdatedAt;
            persistedLastActivityAt = ticket.LastActivityAt;
        }

        private Ticket CloneWithPersistedState(Ticket source)
        {
            var clone = Ticket.Create(
                source.TicketNumber,
                source.Subject,
                source.CustomerName,
                source.CustomerEmail,
                source.CreatedAt);

            // Match the source Id so reload-by-id still works.
            typeof(Ticket)
                .GetProperty(nameof(Ticket.Id))!
                .SetValue(clone, source.Id);

            ApplyStatus(clone, persistedStatus, persistedWaitingSince, persistedUpdatedAt, persistedLastActivityAt);
            return clone;
        }

        private static void ApplyStatus(
            Ticket ticket,
            TicketStatus status,
            DateTime? waitingSince,
            DateTime updatedAt,
            DateTime lastActivityAt)
        {
            // Rebuild lifecycle with domain methods where possible, then force timestamps.
            if (status == TicketStatus.CustomerReplied)
            {
                ticket.MarkAsCustomerReplied(updatedAt);
            }
            else if (status == TicketStatus.WaitingCustomerReply)
            {
                ticket.MarkAsCustomerReplied(updatedAt);
                ticket.MarkAsWaitingCustomerReply(updatedAt);
            }
            else if (status == TicketStatus.Resolved)
            {
                ticket.ResolveManually(updatedAt, SupportUserId);
            }

            SetPrivate(ticket, nameof(Ticket.WaitingCustomerSince), waitingSince);
            SetPrivate(ticket, nameof(Ticket.UpdatedAt), updatedAt);
            SetPrivate(ticket, nameof(Ticket.LastActivityAt), lastActivityAt);
        }

        private static void SetPrivate(Ticket ticket, string propertyName, object? value)
        {
            typeof(Ticket)
                .GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)!
                .SetValue(ticket, value);
        }
    }
}
