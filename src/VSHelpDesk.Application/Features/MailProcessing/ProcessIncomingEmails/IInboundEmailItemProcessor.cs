using VSHelpDesk.Application.Abstractions.Email;

namespace VSHelpDesk.Application.Features.MailProcessing.ProcessIncomingEmails;

public interface IInboundEmailItemProcessor
{
    Task<InboundEmailItemResult> ProcessAsync(
        IncomingEmail email,
        CancellationToken cancellationToken);
}
