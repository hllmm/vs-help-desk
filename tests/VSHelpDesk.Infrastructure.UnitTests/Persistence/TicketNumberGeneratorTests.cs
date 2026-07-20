using Microsoft.EntityFrameworkCore;
using VSHelpDesk.Domain.Tickets;
using VSHelpDesk.Infrastructure.Persistence;

namespace VSHelpDesk.Infrastructure.UnitTests.Persistence;

public sealed class TicketNumberGeneratorTests
{
    [PostgresFact]
    public async Task NextAsync_AgainstPostgreSQL_ReturnsDistinctCanonicalNumbers()
    {
        await using var context = PostgresTestConnection.CreateContext();
        var generator = new TicketNumberGenerator(context);

        var first = await generator.NextAsync();
        var second = await generator.NextAsync();

        Assert.NotEqual(first, second);
        Assert.True(TicketNumberFormat.IsCanonical(first), first);
        Assert.True(TicketNumberFormat.IsCanonical(second), second);
    }

    [PostgresFact]
    public async Task NextAsync_ParallelContexts_AllocateUniqueNumbers()
    {
        // DbContext is not thread-safe; each concurrent allocation uses its own context.
        const int parallelCount = 8;
        var tasks = Enumerable.Range(0, parallelCount).Select(async _ =>
        {
            await using var context = PostgresTestConnection.CreateContext();
            var generator = new TicketNumberGenerator(context);
            return await generator.NextAsync();
        });

        var numbers = await Task.WhenAll(tasks);

        Assert.Equal(parallelCount, numbers.Length);
        Assert.Equal(parallelCount, numbers.Distinct(StringComparer.Ordinal).Count());
        Assert.All(numbers, number => Assert.True(TicketNumberFormat.IsCanonical(number), number));
    }

    [PostgresFact]
    public async Task NextAsync_SequenceExistsAndAdvances()
    {
        await using var context = PostgresTestConnection.CreateContext();
        await context.Database.OpenConnectionAsync();
        try
        {
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = $"SELECT last_value FROM {TicketNumberGenerator.SequenceName}";
            var before = Convert.ToInt64(
                await command.ExecuteScalarAsync(),
                System.Globalization.CultureInfo.InvariantCulture);

            var generator = new TicketNumberGenerator(context);
            var allocated = await generator.NextAsync();

            command.CommandText = $"SELECT last_value FROM {TicketNumberGenerator.SequenceName}";
            var after = Convert.ToInt64(
                await command.ExecuteScalarAsync(),
                System.Globalization.CultureInfo.InvariantCulture);

            Assert.True(after >= before);
            Assert.True(TicketNumberFormat.IsCanonical(allocated), allocated);
            Assert.EndsWith(after.ToString("D6", System.Globalization.CultureInfo.InvariantCulture), allocated);
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }

    [PostgresFact]
    public async Task TicketNumberSequence_Has999999MaximumAndDoesNotCycle()
    {
        await using var context = PostgresTestConnection.CreateContext();
        await context.Database.OpenConnectionAsync();
        try
        {
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = $"""
                SELECT maximum_value, cycle_option
                FROM information_schema.sequences
                WHERE sequence_name = '{TicketNumberGenerator.SequenceName}'
                """;

            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            var maximum = Convert.ToInt64(
                reader.GetValue(0),
                System.Globalization.CultureInfo.InvariantCulture);
            var cycle = reader.GetString(1);

            Assert.Equal(TicketNumberFormat.MaxSequenceValue, maximum);
            Assert.Equal("NO", cycle, ignoreCase: true);
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }
}
