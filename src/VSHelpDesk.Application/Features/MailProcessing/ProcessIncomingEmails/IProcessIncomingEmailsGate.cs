namespace VSHelpDesk.Application.Features.MailProcessing.ProcessIncomingEmails;

/// <summary>Single-flight gate for overlapping job runs (lease-based).</summary>
public interface IProcessIncomingEmailsGate
{
    Task<IProcessIncomingEmailsLease?> TryAcquireAsync(
        CancellationToken cancellationToken = default);
}

public interface IProcessIncomingEmailsLease : IAsyncDisposable
{
}
