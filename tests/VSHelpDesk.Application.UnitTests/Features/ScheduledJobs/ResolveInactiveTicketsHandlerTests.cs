using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Application.Common.Exceptions;
using VSHelpDesk.Application.Features.ScheduledJobs.ResolveInactiveTickets;
using VSHelpDesk.Domain.Entities;
using VSHelpDesk.Domain.Enums;

namespace VSHelpDesk.Application.UnitTests.Features.ScheduledJobs;

public sealed class ResolveInactiveTicketsHandlerTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTime CutoffUtc =
        FixedNow.UtcDateTime - ResolveInactiveTicketsPolicy.InactivityThreshold;

    [Fact]
    public async Task SelectsOnlyInclusiveDueWaitingCandidatesInDeterministicOrder()
    {
        var idA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var idB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var idC = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var idD = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
        var idE = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

        // Equal waiting timestamps: order by Id (B then C).
        var tickets = new[]
        {
            CreateWaiting(idE, CutoffUtc.AddHours(-2)), // earliest due
            CreateWaiting(idC, CutoffUtc),              // equal stamp, higher Id
            CreateWaiting(idB, CutoffUtc),              // equal stamp, lower Id
            CreateWaiting(idD, CutoffUtc.AddTicks(1)),  // one tick below three days → not due
            CreateNew(idA, CutoffUtc.AddDays(-10)),     // wrong status
        };

        var factory = new RecordingFactory();
        var db = new FakeDb(tickets);
        var handler = CreateHandler(db, factory);

        var result = await handler.HandleAsync(
            new ResolveInactiveTicketsCommand(),
            CancellationToken.None);

        Assert.Equal(3, result.Candidates);
        Assert.Equal([idE, idB, idC], factory.CalledTicketIds);
        Assert.Equal(3, result.Resolved);
    }

    [Fact]
    public async Task CapturesNowOnceAndPassesSameNowAndCutoffToEveryCandidate()
    {
        var tickets = new[]
        {
            CreateWaiting(Guid.NewGuid(), CutoffUtc.AddHours(-1)),
            CreateWaiting(Guid.NewGuid(), CutoffUtc),
        };
        var time = new CountingTimeProvider(FixedNow);
        var factory = new RecordingFactory();
        var handler = CreateHandler(new FakeDb(tickets), factory, time);

        await handler.HandleAsync(new ResolveInactiveTicketsCommand(), CancellationToken.None);

        Assert.Equal(1, time.GetUtcNowCallCount);
        Assert.Equal(2, factory.Calls.Count);
        Assert.All(factory.Calls, call =>
        {
            Assert.Equal(FixedNow.UtcDateTime, call.NowUtc);
            Assert.Equal(CutoffUtc, call.CutoffUtc);
        });
    }

    [Fact]
    public async Task CountsResolvedSkippedConflictedAndFailedWithoutPublishingIds()
    {
        var tickets = Enumerable.Range(0, 5)
            .Select(i => CreateWaiting(
                Guid.Parse($"00000000-0000-0000-0000-{i + 1:D12}"),
                CutoffUtc.AddMinutes(-i)))
            .ToArray();

        var factory = new ScriptedFactory(
        [
            InactiveTicketResolutionOutcome.Resolved,
            InactiveTicketResolutionOutcome.Skipped,
            InactiveTicketResolutionOutcome.Conflicted,
            null, // throw
            InactiveTicketResolutionOutcome.Resolved
        ]);
        var handler = CreateHandler(new FakeDb(tickets), factory);

        var result = await handler.HandleAsync(
            new ResolveInactiveTicketsCommand(),
            CancellationToken.None);

        Assert.Equal(CutoffUtc, result.CutoffUtc);
        Assert.Equal(5, result.Candidates);
        Assert.Equal(2, result.Resolved);
        Assert.Equal(1, result.Skipped);
        Assert.Equal(1, result.Conflicted);
        Assert.Equal(1, result.Failed);

        var propertyNames = typeof(ResolveInactiveTicketsResult)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);
        Assert.Equal(
            new HashSet<string>(StringComparer.Ordinal)
            {
                nameof(ResolveInactiveTicketsResult.CutoffUtc),
                nameof(ResolveInactiveTicketsResult.Candidates),
                nameof(ResolveInactiveTicketsResult.Resolved),
                nameof(ResolveInactiveTicketsResult.Skipped),
                nameof(ResolveInactiveTicketsResult.Conflicted),
                nameof(ResolveInactiveTicketsResult.Failed)
            },
            propertyNames);

        var json = JsonSerializer.Serialize(result);
        Assert.DoesNotContain("ticket", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("customer", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("exception", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("VS-", json, StringComparison.Ordinal);
        foreach (var id in tickets.Select(t => t.Id.ToString()))
        {
            Assert.DoesNotContain(id, json, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task CandidateFailure_DoesNotStopLaterCandidates()
    {
        var first = CreateWaiting(Guid.Parse("11111111-1111-1111-1111-111111111111"), CutoffUtc);
        var second = CreateWaiting(Guid.Parse("22222222-2222-2222-2222-222222222222"), CutoffUtc.AddMinutes(-1));
        var factory = new ScriptedFactory(
        [
            null, // throw
            InactiveTicketResolutionOutcome.Resolved
        ]);
        var handler = CreateHandler(new FakeDb([first, second]), factory);

        var result = await handler.HandleAsync(
            new ResolveInactiveTicketsCommand(),
            CancellationToken.None);

        Assert.Equal(2, result.Candidates);
        Assert.Equal(1, result.Failed);
        Assert.Equal(1, result.Resolved);
        Assert.Equal(2, factory.CallCount);
    }

    [Fact]
    public async Task BusyGate_ThrowsJobAlreadyRunningWithoutQueryOrFactoryCall()
    {
        var tickets = new[] { CreateWaiting(Guid.NewGuid(), CutoffUtc) };
        var db = new FakeDb(tickets);
        var factory = new RecordingFactory();
        var handler = new ResolveInactiveTicketsHandler(
            db,
            factory,
            new BusyGate(),
            new FixedTimeProvider(FixedNow),
            NullLogger<ResolveInactiveTicketsHandler>.Instance);

        var ex = await Assert.ThrowsAsync<JobAlreadyRunningException>(() =>
            handler.HandleAsync(new ResolveInactiveTicketsCommand(), CancellationToken.None));

        Assert.Equal(
            "The 'resolve-inactive-tickets' job is already running.",
            ex.Message);
        Assert.Equal(0, db.TicketQueryCount);
        Assert.Empty(factory.CalledTicketIds);
    }

    [Fact]
    public async Task Cancellation_PropagatesAndDisposesLease()
    {
        var tickets = new[]
        {
            CreateWaiting(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), CutoffUtc.AddHours(-1)),
            CreateWaiting(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), CutoffUtc)
        };
        using var cts = new CancellationTokenSource();
        var lease = new TrackingLease();
        var factory = new ScriptedFactory(
        [
            InactiveTicketResolutionOutcome.Resolved,
            InactiveTicketResolutionOutcome.Resolved
        ])
        {
            OnResolve = () => cts.Cancel()
        };
        var gate = new TrackingGate(lease);
        var handler = new ResolveInactiveTicketsHandler(
            new FakeDb(tickets),
            factory,
            gate,
            new FixedTimeProvider(FixedNow),
            NullLogger<ResolveInactiveTicketsHandler>.Instance);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            handler.HandleAsync(new ResolveInactiveTicketsCommand(), cts.Token));

        Assert.True(lease.Disposed);
    }

    private static ResolveInactiveTicketsHandler CreateHandler(
        IApplicationDbContext db,
        IInactiveTicketResolverFactory factory,
        TimeProvider? time = null) =>
        new(
            db,
            factory,
            new AlwaysEnterGate(),
            time ?? new FixedTimeProvider(FixedNow),
            NullLogger<ResolveInactiveTicketsHandler>.Instance);

    private static Ticket CreateWaiting(Guid id, DateTime waitingSince)
    {
        var ticket = Ticket.Create(
            $"VS-{id.ToString("N")[..6].ToUpperInvariant()}",
            "Waiting",
            "Ada",
            "ada@example.test",
            waitingSince.AddHours(-1));
        ticket.MarkAsWaitingCustomerReply(waitingSince);
        typeof(Ticket).GetProperty(nameof(Ticket.Id))!.SetValue(ticket, id);
        return ticket;
    }

    private static Ticket CreateNew(Guid id, DateTime createdAt)
    {
        var ticket = Ticket.Create(
            $"VS-{id.ToString("N")[..6].ToUpperInvariant()}",
            "New",
            "Ada",
            "ada@example.test",
            createdAt);
        typeof(Ticket).GetProperty(nameof(Ticket.Id))!.SetValue(ticket, id);
        return ticket;
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class CountingTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public int GetUtcNowCallCount { get; private set; }

        public override DateTimeOffset GetUtcNow()
        {
            GetUtcNowCallCount++;
            return utcNow;
        }
    }

    private sealed class AlwaysEnterGate : IResolveInactiveTicketsGate
    {
        public Task<IResolveInactiveTicketsLease?> TryAcquireAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IResolveInactiveTicketsLease?>(new NoopLease());
    }

    private sealed class BusyGate : IResolveInactiveTicketsGate
    {
        public Task<IResolveInactiveTicketsLease?> TryAcquireAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IResolveInactiveTicketsLease?>(null);
    }

    private sealed class TrackingGate(TrackingLease lease) : IResolveInactiveTicketsGate
    {
        public Task<IResolveInactiveTicketsLease?> TryAcquireAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IResolveInactiveTicketsLease?>(lease);
    }

    private sealed class TrackingLease : IResolveInactiveTicketsLease
    {
        public bool Disposed { get; private set; }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class NoopLease : IResolveInactiveTicketsLease
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingFactory : IInactiveTicketResolverFactory
    {
        public List<Guid> CalledTicketIds { get; } = [];
        public List<(Guid TicketId, DateTime CutoffUtc, DateTime NowUtc)> Calls { get; } = [];

        public Task<InactiveTicketResolutionOutcome> ResolveAsync(
            Guid ticketId,
            DateTime cutoffUtc,
            DateTime nowUtc,
            CancellationToken cancellationToken)
        {
            CalledTicketIds.Add(ticketId);
            Calls.Add((ticketId, cutoffUtc, nowUtc));
            return Task.FromResult(InactiveTicketResolutionOutcome.Resolved);
        }
    }

    private sealed class ScriptedFactory(IReadOnlyList<InactiveTicketResolutionOutcome?> outcomes)
        : IInactiveTicketResolverFactory
    {
        private int index;

        public int CallCount { get; private set; }
        public Action? OnResolve { get; init; }

        public Task<InactiveTicketResolutionOutcome> ResolveAsync(
            Guid ticketId,
            DateTime cutoffUtc,
            DateTime nowUtc,
            CancellationToken cancellationToken)
        {
            CallCount++;
            OnResolve?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();

            var outcome = outcomes[index++];
            if (outcome is null)
            {
                throw new InvalidOperationException("Simulated candidate failure.");
            }

            return Task.FromResult(outcome.Value);
        }
    }

    private sealed class FakeDb : IApplicationDbContext
    {
        private readonly Ticket[] tickets;

        public FakeDb(IReadOnlyList<Ticket> tickets)
        {
            this.tickets = tickets.ToArray();
        }

        public int TicketQueryCount { get; private set; }

        public IQueryable<User> Users => Array.Empty<User>().AsQueryable();

        public IQueryable<Ticket> Tickets
        {
            get
            {
                TicketQueryCount++;
                return tickets.AsQueryable();
            }
        }

        public IQueryable<TicketMessage> TicketMessages =>
            Array.Empty<TicketMessage>().AsQueryable();
        public IQueryable<TicketAttachment> TicketAttachments =>
            Array.Empty<TicketAttachment>().AsQueryable();
        public IQueryable<ProcessedEmailMessage> ProcessedEmailMessages =>
            Array.Empty<ProcessedEmailMessage>().AsQueryable();

        public void Add<TEntity>(TEntity entity) where TEntity : class
        {
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(0);

        public void ClearTrackedChanges()
        {
        }
    }
}
