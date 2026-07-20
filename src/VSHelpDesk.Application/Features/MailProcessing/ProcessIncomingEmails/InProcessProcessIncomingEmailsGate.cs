namespace VSHelpDesk.Application.Features.MailProcessing.ProcessIncomingEmails;

/// <summary>
/// Temporary in-process lease gate. Task 8 replaces production registration with PostgreSQL.
/// </summary>
public sealed class InProcessProcessIncomingEmailsGate : IProcessIncomingEmailsGate
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<IProcessIncomingEmailsLease?> TryAcquireAsync(
        CancellationToken cancellationToken = default)
    {
        if (!await gate.WaitAsync(0, cancellationToken))
        {
            return null;
        }

        return new InProcessLease(gate);
    }

    private sealed class InProcessLease(SemaphoreSlim gate) : IProcessIncomingEmailsLease
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
