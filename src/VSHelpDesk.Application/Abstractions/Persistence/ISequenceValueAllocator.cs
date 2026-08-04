namespace VSHelpDesk.Application.Abstractions.Persistence;

/// <summary>
/// Allocates the next value from a named database sequence.
/// Provider-specific implementations use native DB sequences;
/// the fallback uses MAX+1 with process-level concurrency control.
/// </summary>
public interface ISequenceValueAllocator
{
    /// <summary>Returns the next value from the named sequence.</summary>
    Task<long> NextAsync(string sequenceName, CancellationToken cancellationToken = default);
}
