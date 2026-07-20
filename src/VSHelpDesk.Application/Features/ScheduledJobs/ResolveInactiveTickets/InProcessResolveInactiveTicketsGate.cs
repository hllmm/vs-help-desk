namespace VSHelpDesk.Application.Features.ScheduledJobs.ResolveInactiveTickets;

/// <summary>
/// In-process lease gate for unit tests. Production uses PostgreSQL advisory locks.
/// </summary>
public sealed class InProcessResolveInactiveTicketsGate : IResolveInactiveTicketsGate
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<IResolveInactiveTicketsLease?> TryAcquireAsync(
        CancellationToken cancellationToken = default)
    {
        if (!await gate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new InProcessLease(gate);
    }

    private sealed class InProcessLease(SemaphoreSlim gate) : IResolveInactiveTicketsLease
    {
        private int disposed;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
            {
                gate.Release();
            }

            return ValueTask.CompletedTask;
        }
    }
}
