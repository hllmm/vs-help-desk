using Microsoft.Extensions.DependencyInjection;
using VSHelpDesk.Application.Abstractions.Email;
using VSHelpDesk.Application.Features.MailProcessing;
using VSHelpDesk.Application.Features.MailProcessing.Acknowledgements;
using VSHelpDesk.Application.Features.MailProcessing.ProcessIncomingEmails;

namespace VSHelpDesk.Infrastructure.Processing;

/// <summary>
/// Creates a fresh async DI scope (and therefore DbContext) per receipt / retry pass.
/// Resolves no scoped services from its own constructor.
/// </summary>
public sealed class ScopedInboundEmailItemProcessorFactory(
    IServiceScopeFactory scopeFactory)
    : IInboundEmailItemProcessorFactory
{
    public async Task<InboundEmailItemResult> ProcessAsync(
        IncomingEmail email,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var processor =
            scope.ServiceProvider.GetRequiredService<IInboundEmailItemProcessor>();
        return await processor.ProcessAsync(email, cancellationToken);
    }

    public async Task<AcknowledgementDispatchSummary>
        RetryDueAcknowledgementsAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dispatcher =
            scope.ServiceProvider.GetRequiredService<AcknowledgementDispatcher>();
        return await dispatcher.RetryDueAsync(cancellationToken);
    }
}
