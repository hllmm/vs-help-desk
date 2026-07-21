using VSHelpDesk.Application.Abstractions.Email;
using VSHelpDesk.Application.Features.MailProcessing.Acknowledgements;

namespace VSHelpDesk.Application.Features.MailProcessing.ProcessIncomingEmails;

public interface IInboundEmailItemProcessorFactory
{
    Task<InboundEmailItemResult> ProcessAsync(
        IncomingEmail email,
        CancellationToken cancellationToken);

    Task<AcknowledgementDispatchSummary> RetryDueAcknowledgementsAsync(
        CancellationToken cancellationToken);
}
