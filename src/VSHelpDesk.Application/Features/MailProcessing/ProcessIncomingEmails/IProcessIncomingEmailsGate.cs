namespace VSHelpDesk.Application.Features.MailProcessing.ProcessIncomingEmails;

/// <summary>In-process single-flight for overlapping job runs.</summary>
public interface IProcessIncomingEmailsGate
{
    Task<bool> TryEnterAsync(CancellationToken cancellationToken = default);

    void Exit();
}
