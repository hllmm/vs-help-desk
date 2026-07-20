using Microsoft.Extensions.Logging.Abstractions;
using VSHelpDesk.Infrastructure.Persistence;

namespace VSHelpDesk.Infrastructure.UnitTests.Persistence;

public sealed class PostgresProcessIncomingEmailsGateTests
{
    [PostgresFact]
    public async Task TwoConnections_OnlyOneCanHoldJobLease()
    {
        var connectionString = PostgresTestConnection.TryGet()
            ?? throw new InvalidOperationException("PostgreSQL connection string required.");

        var firstGate = CreateGate(connectionString);
        var secondGate = CreateGate(connectionString);

        await using var firstLease = await firstGate.TryAcquireAsync();
        Assert.NotNull(firstLease);

        var secondLease = await secondGate.TryAcquireAsync();
        Assert.Null(secondLease);
    }

    [PostgresFact]
    public async Task DisposedLease_AllowsLaterAcquisition()
    {
        var connectionString = PostgresTestConnection.TryGet()
            ?? throw new InvalidOperationException("PostgreSQL connection string required.");

        var gate = CreateGate(connectionString);

        var firstLease = await gate.TryAcquireAsync();
        Assert.NotNull(firstLease);
        await firstLease!.DisposeAsync();

        await using var secondLease = await gate.TryAcquireAsync();
        Assert.NotNull(secondLease);
    }

    private static PostgresProcessIncomingEmailsGate CreateGate(string connectionString) =>
        new(connectionString, NullLogger<PostgresProcessIncomingEmailsGate>.Instance);
}
