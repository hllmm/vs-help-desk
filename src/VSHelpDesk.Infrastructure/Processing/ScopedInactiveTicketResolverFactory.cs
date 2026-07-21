using Microsoft.Extensions.DependencyInjection;
using VSHelpDesk.Application.Features.ScheduledJobs.ResolveInactiveTickets;

namespace VSHelpDesk.Infrastructure.Processing;

/// <summary>
/// Creates a fresh async DI scope (and therefore DbContext) per candidate ticket.
/// Resolves no scoped services from its own constructor.
/// </summary>
public sealed class ScopedInactiveTicketResolverFactory(IServiceScopeFactory scopeFactory)
    : IInactiveTicketResolverFactory
{
    public async Task<InactiveTicketResolutionOutcome> ResolveAsync(
        Guid ticketId,
        DateTime cutoffUtc,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var resolver = scope.ServiceProvider
            .GetRequiredService<IInactiveTicketResolver>();
        return await resolver.ResolveAsync(
            ticketId,
            cutoffUtc,
            nowUtc,
            cancellationToken).ConfigureAwait(false);
    }
}
