using Microsoft.Extensions.DependencyInjection;
using VSHelpDesk.Application.Features.ScheduledJobs.ResolveInactiveTickets;
using VSHelpDesk.Infrastructure.Processing;

namespace VSHelpDesk.Infrastructure.UnitTests.Processing;

public sealed class ScopedInactiveTicketResolverFactoryTests
{
    [Fact]
    public async Task Factory_CreatesAndDisposesDistinctAsyncScopePerCandidate()
    {
        var tracker = new ScopeTracker();
        var services = new ServiceCollection();
        services.AddSingleton(tracker);
        services.AddScoped<IInactiveTicketResolver, TrackingResolver>();
        var root = services.BuildServiceProvider();
        var scopeFactory = new TrackingServiceScopeFactory(root, tracker);
        var factory = new ScopedInactiveTicketResolverFactory(scopeFactory);

        var firstId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var secondId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var cutoff = new DateTime(2026, 8, 8, 12, 0, 0, DateTimeKind.Utc);
        var now = new DateTime(2026, 8, 11, 12, 0, 0, DateTimeKind.Utc);

        var first = await factory.ResolveAsync(firstId, cutoff, now, CancellationToken.None);
        var second = await factory.ResolveAsync(secondId, cutoff, now, CancellationToken.None);

        Assert.Equal(InactiveTicketResolutionOutcome.Resolved, first);
        Assert.Equal(InactiveTicketResolutionOutcome.Resolved, second);
        Assert.Equal(2, tracker.CreatedScopeIds.Count);
        Assert.NotEqual(tracker.CreatedScopeIds[0], tracker.CreatedScopeIds[1]);
        Assert.Equal(2, tracker.DisposedScopeIds.Count);
        Assert.Equal(tracker.CreatedScopeIds, tracker.DisposedScopeIds);
        Assert.Equal([firstId, secondId], tracker.ResolvedTicketIds);
        Assert.Equal(2, tracker.ResolverInstanceIds.Count);
        Assert.NotEqual(tracker.ResolverInstanceIds[0], tracker.ResolverInstanceIds[1]);
    }

    private sealed class ScopeTracker
    {
        public List<Guid> CreatedScopeIds { get; } = [];
        public List<Guid> DisposedScopeIds { get; } = [];
        public List<Guid> ResolvedTicketIds { get; } = [];
        public List<Guid> ResolverInstanceIds { get; } = [];
    }

    private sealed class TrackingServiceScopeFactory(
        ServiceProvider root,
        ScopeTracker tracker) : IServiceScopeFactory
    {
        public IServiceScope CreateScope()
        {
            var inner = root.CreateScope();
            var id = Guid.NewGuid();
            tracker.CreatedScopeIds.Add(id);
            return new TrackedScope(inner, tracker, id);
        }

        private sealed class TrackedScope(
            IServiceScope inner,
            ScopeTracker tracker,
            Guid id) : IServiceScope, IAsyncDisposable
        {
            private int disposed;

            public IServiceProvider ServiceProvider => inner.ServiceProvider;

            public void Dispose()
            {
                if (Interlocked.Exchange(ref disposed, 1) == 0)
                {
                    tracker.DisposedScopeIds.Add(id);
                    inner.Dispose();
                }
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class TrackingResolver(ScopeTracker tracker) : IInactiveTicketResolver
    {
        private readonly Guid instanceId = Guid.NewGuid();

        public Task<InactiveTicketResolutionOutcome> ResolveAsync(
            Guid ticketId,
            DateTime cutoffUtc,
            DateTime nowUtc,
            CancellationToken cancellationToken)
        {
            tracker.ResolverInstanceIds.Add(instanceId);
            tracker.ResolvedTicketIds.Add(ticketId);
            return Task.FromResult(InactiveTicketResolutionOutcome.Resolved);
        }
    }
}
