namespace VSHelpDesk.Application.Abstractions.Persistence;

/// <summary>
/// Allocates the next unique ticket number (BR-003). Concurrent-safe implementations
/// should rely on a DB sequence or equivalent; unique index remains the last line of defense.
/// </summary>
public interface ITicketNumberGenerator
{
    Task<string> NextAsync(CancellationToken cancellationToken = default);
}
