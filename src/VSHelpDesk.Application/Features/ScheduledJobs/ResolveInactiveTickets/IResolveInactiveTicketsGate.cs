namespace VSHelpDesk.Application.Features.ScheduledJobs.ResolveInactiveTickets;

/// <summary>Single-flight gate for overlapping auto-resolve job runs (lease-based).</summary>
public interface IResolveInactiveTicketsGate
{
    Task<IResolveInactiveTicketsLease?> TryAcquireAsync(
        CancellationToken cancellationToken = default);
}

public interface IResolveInactiveTicketsLease : IAsyncDisposable
{
}
