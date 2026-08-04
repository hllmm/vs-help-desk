using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Domain.Tickets;

namespace VSHelpDesk.Infrastructure.Persistence;

/// <summary>
/// Allocates VS-###### ticket numbers by delegating to <see cref="ISequenceValueAllocator"/>.
/// </summary>
public sealed class TicketNumberGenerator(ISequenceValueAllocator sequenceAllocator) : ITicketNumberGenerator
{
    /// <summary>Must match the AddTicketNumberSequence migration.</summary>
    public const string SequenceName = "ticket_number_seq";

    public async Task<string> NextAsync(CancellationToken cancellationToken = default)
    {
        var seq = await sequenceAllocator.NextAsync(SequenceName, cancellationToken);
        return TicketNumberFormat.Format(seq);
    }
}
