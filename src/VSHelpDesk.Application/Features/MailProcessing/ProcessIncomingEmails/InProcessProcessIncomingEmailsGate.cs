namespace VSHelpDesk.Application.Features.MailProcessing.ProcessIncomingEmails;

public sealed class InProcessProcessIncomingEmailsGate : IProcessIncomingEmailsGate
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<bool> TryEnterAsync(CancellationToken cancellationToken = default) =>
        await gate.WaitAsync(0, cancellationToken);

    public void Exit() => gate.Release();
}
