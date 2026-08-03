using Microsoft.EntityFrameworkCore;
using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Domain.Tickets;

namespace VSHelpDesk.Infrastructure.Persistence;

/// <summary>
/// Allocates VS-###### numbers via PostgreSQL sequence <c>ticket_number_seq</c>.
/// </summary>
public sealed class TicketNumberGenerator(ApplicationDbContext dbContext) : ITicketNumberGenerator
{
    /// <summary>Must match the fixed SQL below and the AddTicketNumberSequence migration.</summary>
    public const string SequenceName = "ticket_number_seq";

    public async Task<string> NextAsync(CancellationToken cancellationToken = default)
    {
        if (!dbContext.Database.IsRelational())
        {
            var count = await dbContext.Tickets.CountAsync(cancellationToken);
            return TicketNumberFormat.Format(count + 1);
        }

        try
        {
            await dbContext.Database.OpenConnectionAsync(cancellationToken);
            try
            {
                await using var command = dbContext.Database.GetDbConnection().CreateCommand();
                // Literal only — never interpolate user input into identifier SQL.
                command.CommandText = "SELECT nextval('ticket_number_seq')";
                var scalar = await command.ExecuteScalarAsync(cancellationToken);
                var sequenceValue = Convert.ToInt64(scalar, System.Globalization.CultureInfo.InvariantCulture);
                return TicketNumberFormat.Format(sequenceValue);
            }
            finally
            {
                await dbContext.Database.CloseConnectionAsync();
            }
        }
        catch
        {
            var count = await dbContext.Tickets.CountAsync(cancellationToken);
            return TicketNumberFormat.Format(count + 1);
        }
    }
}
