using System.Globalization;
using Microsoft.EntityFrameworkCore;
using VSHelpDesk.Application.Abstractions.Persistence;

namespace VSHelpDesk.Infrastructure.Persistence.Sequences;

/// <summary>
/// PostgreSQL <c>nextval()</c> based sequence allocator.
/// </summary>
public sealed class PostgresSequenceAllocator(ApplicationDbContext dbContext) : ISequenceValueAllocator
{
    public async Task<long> NextAsync(string sequenceName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sequenceName);

        await dbContext.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = dbContext.Database.GetDbConnection().CreateCommand();
            // Safe: sequenceName is a compile-time constant, never user input.
            command.CommandText = $"SELECT nextval('{sequenceName}')";
            var scalar = await command.ExecuteScalarAsync(cancellationToken);
            return Convert.ToInt64(scalar, CultureInfo.InvariantCulture);
        }
        finally
        {
            await dbContext.Database.CloseConnectionAsync();
        }
    }
}
