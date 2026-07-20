using System.Reflection;
using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Application.Common.Exceptions;
using VSHelpDesk.Application.Features.ScheduledJobs.ResolveInactiveTickets;
using VSHelpDesk.Domain.Entities;
using VSHelpDesk.Domain.Enums;

namespace VSHelpDesk.Application.UnitTests.Features.ScheduledJobs;

public sealed class InactiveTicketResolverTests
{
    private static readonly DateTime NowUtc = new(2026, 8, 11, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime CutoffUtc =
        NowUtc - TimeSpan.FromDays(ResolveInactiveTicketsPolicy.DefaultInactivityDays);

    [Fact]
    public void Policy_OneTickBelowThreeDays_IsNotEligible()
    {
        var ticket = WaitingTicket(CutoffUtc.AddTicks(1));

        Assert.False(ResolveInactiveTicketsPolicy.IsEligible(ticket, CutoffUtc));
    }

    [Fact]
    public void Policy_ExactlyThreeDays_IsEligible()
    {
        var ticket = WaitingTicket(CutoffUtc);

        Assert.True(ResolveInactiveTicketsPolicy.IsEligible(ticket, CutoffUtc));
    }

    [Fact]
    public void Policy_OneTickBeyondThreeDays_IsEligible()
    {
        var ticket = WaitingTicket(CutoffUtc.AddTicks(-1));

        Assert.True(ResolveInactiveTicketsPolicy.IsEligible(ticket, CutoffUtc));
    }

    [Theory]
    [InlineData(TicketStatus.New)]
    [InlineData(TicketStatus.CustomerReplied)]
    [InlineData(TicketStatus.Resolved)]
    public void Policy_NonWaitingStatus_IsNotEligible(TicketStatus status)
    {
        var ticket = Ticket.Create(
            "VS-POL001",
            "Non-waiting",
            "Ada",
            "ada@example.test",
            CutoffUtc.AddDays(-1));
        ApplyStatus(ticket, status, CutoffUtc.AddDays(-1));

        Assert.False(ResolveInactiveTicketsPolicy.IsEligible(ticket, CutoffUtc));
    }

    [Fact]
    public async Task EligibleTicket_ResolvesAutomaticallyWithNullCloser()
    {
        var ticket = WaitingTicket(CutoffUtc);
        var db = new FakeDb(ticket);
        var resolver = new InactiveTicketResolver(db);

        var outcome = await resolver.ResolveAsync(
            ticket.Id,
            CutoffUtc,
            NowUtc,
            CancellationToken.None);

        Assert.Equal(InactiveTicketResolutionOutcome.Resolved, outcome);
        Assert.Equal(1, db.SaveCallCount);
        Assert.Equal(TicketStatus.Resolved, ticket.Status);
        Assert.Null(ticket.ClosedByUserId);
        Assert.Equal(NowUtc, ticket.ResolvedAt);
        Assert.Equal(NowUtc, ticket.UpdatedAt);
        Assert.Equal(NowUtc, ticket.LastActivityAt);
        Assert.Null(ticket.WaitingCustomerSince);
    }

    [Fact]
    public async Task MissingOrNoLongerEligibleTicket_ReturnsSkippedWithoutSave()
    {
        var missingResolver = new InactiveTicketResolver(new FakeDb());
        var missingOutcome = await missingResolver.ResolveAsync(
            Guid.NewGuid(),
            CutoffUtc,
            NowUtc,
            CancellationToken.None);
        Assert.Equal(InactiveTicketResolutionOutcome.Skipped, missingOutcome);

        var recent = WaitingTicket(CutoffUtc.AddTicks(1));
        var recentDb = new FakeDb(recent);
        var recentResolver = new InactiveTicketResolver(recentDb);
        var recentOutcome = await recentResolver.ResolveAsync(
            recent.Id,
            CutoffUtc,
            NowUtc,
            CancellationToken.None);

        Assert.Equal(InactiveTicketResolutionOutcome.Skipped, recentOutcome);
        Assert.Equal(0, recentDb.SaveCallCount);
        Assert.Equal(TicketStatus.WaitingCustomerReply, recent.Status);
    }

    [Fact]
    public async Task FirstConflict_ReloadsRechecksAndResolvesWhenStillEligible()
    {
        var ticket = WaitingTicket(CutoffUtc);
        var db = new FakeDb(ticket, conflictOnSaveCalls: [1]);
        var resolver = new InactiveTicketResolver(db);

        var outcome = await resolver.ResolveAsync(
            ticket.Id,
            CutoffUtc,
            NowUtc,
            CancellationToken.None);

        Assert.Equal(InactiveTicketResolutionOutcome.Resolved, outcome);
        Assert.Equal(2, db.SaveCallCount);
        Assert.Equal(1, db.ClearTrackedCallCount);
        Assert.Equal(TicketStatus.Resolved, db.PersistedStatus);
        Assert.Null(db.PersistedClosedByUserId);
        Assert.Equal(NowUtc, db.PersistedResolvedAt);
    }

    [Fact]
    public async Task FirstConflict_ReloadedCustomerReply_ReturnsSkippedWithoutSecondResolve()
    {
        var ticket = WaitingTicket(CutoffUtc);
        var replyAt = NowUtc.AddMinutes(-1);
        var db = new FakeDb(ticket, conflictOnSaveCalls: [1])
        {
            OnConflict = snapshot =>
            {
                snapshot.Status = TicketStatus.CustomerReplied;
                snapshot.WaitingCustomerSince = null;
                snapshot.UpdatedAt = replyAt;
                snapshot.LastActivityAt = replyAt;
                snapshot.ResolvedAt = null;
                snapshot.ClosedByUserId = null;
            }
        };
        var resolver = new InactiveTicketResolver(db);

        var outcome = await resolver.ResolveAsync(
            ticket.Id,
            CutoffUtc,
            NowUtc,
            CancellationToken.None);

        Assert.Equal(InactiveTicketResolutionOutcome.Skipped, outcome);
        Assert.Equal(1, db.SaveCallCount);
        Assert.Equal(1, db.ClearTrackedCallCount);
        Assert.Equal(TicketStatus.CustomerReplied, db.QueryTicket.Status);
        Assert.Null(db.QueryTicket.ClosedByUserId);
        Assert.Null(db.QueryTicket.ResolvedAt);
        Assert.Equal(TicketStatus.CustomerReplied, db.PersistedStatus);
    }

    [Fact]
    public async Task SecondConflict_ReturnsConflictedAndClearsTracking()
    {
        var ticket = WaitingTicket(CutoffUtc);
        var db = new FakeDb(ticket, conflictOnSaveCalls: [1, 2]);
        var resolver = new InactiveTicketResolver(db);

        var outcome = await resolver.ResolveAsync(
            ticket.Id,
            CutoffUtc,
            NowUtc,
            CancellationToken.None);

        Assert.Equal(InactiveTicketResolutionOutcome.Conflicted, outcome);
        Assert.Equal(2, db.SaveCallCount);
        Assert.Equal(2, db.ClearTrackedCallCount);
        Assert.Equal(TicketStatus.WaitingCustomerReply, db.PersistedStatus);
        Assert.Null(db.PersistedClosedByUserId);
    }

    [Fact]
    public async Task NonConcurrencyFailure_PropagatesForOrchestratorCounting()
    {
        var ticket = WaitingTicket(CutoffUtc);
        var db = new FakeDb(ticket)
        {
            OnSave = () => throw new InvalidOperationException("db unavailable")
        };
        var resolver = new InactiveTicketResolver(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            resolver.ResolveAsync(ticket.Id, CutoffUtc, NowUtc, CancellationToken.None));

        Assert.Equal("db unavailable", ex.Message);
        Assert.Equal(1, db.SaveCallCount);
        Assert.Equal(0, db.ClearTrackedCallCount);
    }

    [Fact]
    public async Task Cancellation_Propagates()
    {
        var ticket = WaitingTicket(CutoffUtc);
        using var cts = new CancellationTokenSource();
        var db = new FakeDb(ticket)
        {
            OnSave = () => cts.Cancel()
        };
        var resolver = new InactiveTicketResolver(db);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            resolver.ResolveAsync(ticket.Id, CutoffUtc, NowUtc, cts.Token));
    }

    private static Ticket WaitingTicket(DateTime waitingSince)
    {
        var ticket = Ticket.Create(
            "VS-WAIT01",
            "Waiting",
            "Ada",
            "ada@example.test",
            waitingSince.AddHours(-1));
        ticket.MarkAsWaitingCustomerReply(waitingSince);
        return ticket;
    }

    private static void ApplyStatus(Ticket ticket, TicketStatus status, DateTime stamp)
    {
        switch (status)
        {
            case TicketStatus.New:
                break;
            case TicketStatus.CustomerReplied:
                ticket.MarkAsCustomerReplied(stamp);
                break;
            case TicketStatus.Resolved:
                ticket.ResolveManually(stamp, Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"));
                break;
            case TicketStatus.WaitingCustomerReply:
                ticket.MarkAsWaitingCustomerReply(stamp);
                break;
        }
    }

    /// <summary>
    /// Save conflicts restore last successfully persisted snapshot (Week 3 reply pattern).
    /// OnConflict can simulate a concurrent customer reply committing before reload.
    /// </summary>
    private sealed class FakeDb : IApplicationDbContext
    {
        private readonly HashSet<int> conflictOnSaveCalls;
        private Ticket? queryTicket;
        private TicketStatus persistedStatus;
        private DateTime? persistedWaitingSince;
        private DateTime? persistedResolvedAt;
        private Guid? persistedClosedByUserId;
        private DateTime persistedUpdatedAt;
        private DateTime persistedLastActivityAt;

        public FakeDb(Ticket? ticket = null, IEnumerable<int>? conflictOnSaveCalls = null)
        {
            queryTicket = ticket;
            this.conflictOnSaveCalls = conflictOnSaveCalls?.ToHashSet() ?? [];
            if (ticket is not null)
            {
                CapturePersistedSnapshot(ticket);
            }
        }

        public Action? OnSave { get; init; }
        public Action<PersistedSnapshot>? OnConflict { get; init; }
        public int SaveCallCount { get; private set; }
        public int ClearTrackedCallCount { get; private set; }
        public Ticket QueryTicket => queryTicket!;
        public TicketStatus PersistedStatus => persistedStatus;
        public Guid? PersistedClosedByUserId => persistedClosedByUserId;
        public DateTime? PersistedResolvedAt => persistedResolvedAt;

        public IQueryable<User> Users => Array.Empty<User>().AsQueryable();
        public IQueryable<Ticket> Tickets =>
            queryTicket is null
                ? Array.Empty<Ticket>().AsQueryable()
                : new[] { queryTicket }.AsQueryable();
        public IQueryable<TicketMessage> TicketMessages =>
            Array.Empty<TicketMessage>().AsQueryable();
        public IQueryable<TicketAttachment> TicketAttachments =>
            Array.Empty<TicketAttachment>().AsQueryable();
        public IQueryable<ProcessedEmailMessage> ProcessedEmailMessages =>
            Array.Empty<ProcessedEmailMessage>().AsQueryable();

        public IQueryable<ApplicationParameter> ApplicationParameters =>
            Array.Empty<ApplicationParameter>().AsQueryable();

        public void Add<TEntity>(TEntity entity) where TEntity : class
        {
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCallCount++;
            OnSave?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();

            if (conflictOnSaveCalls.Contains(SaveCallCount))
            {
                if (OnConflict is not null)
                {
                    var snapshot = new PersistedSnapshot
                    {
                        Status = persistedStatus,
                        WaitingCustomerSince = persistedWaitingSince,
                        ResolvedAt = persistedResolvedAt,
                        ClosedByUserId = persistedClosedByUserId,
                        UpdatedAt = persistedUpdatedAt,
                        LastActivityAt = persistedLastActivityAt
                    };
                    OnConflict(snapshot);
                    persistedStatus = snapshot.Status;
                    persistedWaitingSince = snapshot.WaitingCustomerSince;
                    persistedResolvedAt = snapshot.ResolvedAt;
                    persistedClosedByUserId = snapshot.ClosedByUserId;
                    persistedUpdatedAt = snapshot.UpdatedAt;
                    persistedLastActivityAt = snapshot.LastActivityAt;
                }

                throw new OptimisticConcurrencyException(
                    $"Simulated concurrency conflict on save call {SaveCallCount}.");
            }

            if (queryTicket is not null)
            {
                CapturePersistedSnapshot(queryTicket);
            }

            return Task.FromResult(1);
        }

        public void ClearTrackedChanges()
        {
            ClearTrackedCallCount++;
            if (queryTicket is null)
            {
                return;
            }

            queryTicket = CloneWithPersistedState(queryTicket);
        }

        private void CapturePersistedSnapshot(Ticket ticket)
        {
            persistedStatus = ticket.Status;
            persistedWaitingSince = ticket.WaitingCustomerSince;
            persistedResolvedAt = ticket.ResolvedAt;
            persistedClosedByUserId = ticket.ClosedByUserId;
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

            typeof(Ticket)
                .GetProperty(nameof(Ticket.Id))!
                .SetValue(clone, source.Id);

            ApplyPersisted(clone);
            return clone;
        }

        private void ApplyPersisted(Ticket ticket)
        {
            if (persistedStatus == TicketStatus.CustomerReplied)
            {
                ticket.MarkAsCustomerReplied(persistedUpdatedAt);
            }
            else if (persistedStatus == TicketStatus.WaitingCustomerReply)
            {
                ticket.MarkAsCustomerReplied(persistedUpdatedAt);
                ticket.MarkAsWaitingCustomerReply(persistedWaitingSince ?? persistedUpdatedAt);
            }
            else if (persistedStatus == TicketStatus.Resolved)
            {
                if (persistedClosedByUserId is Guid closer)
                {
                    ticket.ResolveManually(persistedResolvedAt ?? persistedUpdatedAt, closer);
                }
                else
                {
                    ticket.MarkAsWaitingCustomerReply(persistedUpdatedAt);
                    ticket.ResolveAutomatically(persistedResolvedAt ?? persistedUpdatedAt);
                }
            }

            SetPrivate(ticket, nameof(Ticket.WaitingCustomerSince), persistedWaitingSince);
            SetPrivate(ticket, nameof(Ticket.ResolvedAt), persistedResolvedAt);
            SetPrivate(ticket, nameof(Ticket.ClosedByUserId), persistedClosedByUserId);
            SetPrivate(ticket, nameof(Ticket.UpdatedAt), persistedUpdatedAt);
            SetPrivate(ticket, nameof(Ticket.LastActivityAt), persistedLastActivityAt);
        }

        private static void SetPrivate(Ticket ticket, string propertyName, object? value)
        {
            typeof(Ticket)
                .GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)!
                .SetValue(ticket, value);
        }

        public sealed class PersistedSnapshot
        {
            public TicketStatus Status { get; set; }
            public DateTime? WaitingCustomerSince { get; set; }
            public DateTime? ResolvedAt { get; set; }
            public Guid? ClosedByUserId { get; set; }
            public DateTime UpdatedAt { get; set; }
            public DateTime LastActivityAt { get; set; }
        }
    }
}
