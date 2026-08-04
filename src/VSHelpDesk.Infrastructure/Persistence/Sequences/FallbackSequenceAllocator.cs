using Microsoft.EntityFrameworkCore;
using VSHelpDesk.Application.Abstractions.Persistence;

namespace VSHelpDesk.Infrastructure.Persistence.Sequences;

/// <summary>
/// Fallback sequence allocator for non-sequence providers (SQLite, InMemory).
/// Uses process-level <see cref="SemaphoreSlim"/> to prevent concurrent duplicate numbers.
/// </summary>
public sealed class FallbackSequenceAllocator(ApplicationDbContext dbContext) : ISequenceValueAllocator
{
    private static readonly SemaphoreSlim Lock = new(1, 1);

    public async Task<long> NextAsync(string sequenceName, CancellationToken cancellationToken = default)
    {
        await Lock.WaitAsync(cancellationToken);
        try
        {
            var count = await dbContext.Tickets.CountAsync(cancellationToken);
            return count + 1;
        }
        finally
        {
            Lock.Release();
        }
    }
}
