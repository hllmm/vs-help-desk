using Microsoft.Extensions.Logging.Abstractions;
using VSHelpDesk.Infrastructure.Persistence;

namespace VSHelpDesk.Infrastructure.UnitTests.Persistence;

/// <summary>
/// Serializes with other advisory-lock suites so concurrent pg_try_advisory_lock tests
/// cannot steal each other's session locks on the shared local database.
/// </summary>
[Collection("PostgresAdvisoryLocks")]
public sealed class PostgresResolveInactiveTicketsGateTests
{
    [PostgresFact]
    public async Task TwoConnections_OnlyOneCanHoldAutoResolveLease()
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
    public async Task AutoResolveAndIncomingMail_UseDistinctKeysAndCanRunTogether()
    {
        Assert.NotEqual(
            PostgresProcessIncomingEmailsGate.AdvisoryLockKey,
            PostgresResolveInactiveTicketsGate.AdvisoryLockKey);

        var connectionString = PostgresTestConnection.TryGet()
            ?? throw new InvalidOperationException("PostgreSQL connection string required.");

        var mailGate = new PostgresProcessIncomingEmailsGate(
            connectionString,
            NullLogger<PostgresProcessIncomingEmailsGate>.Instance);
        var resolveGate = CreateGate(connectionString);

        await using var mailLease = await mailGate.TryAcquireAsync();
        await using var resolveLease = await resolveGate.TryAcquireAsync();

        Assert.NotNull(mailLease);
        Assert.NotNull(resolveLease);
    }

    [PostgresFact]
    public async Task DisposedAutoResolveLease_AllowsLaterAcquisition()
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

    [PostgresFact]
    public async Task FailedOrCancelledAcquisition_DisposesConnection()
    {
        var connectionString = PostgresTestConnection.TryGet()
            ?? throw new InvalidOperationException("PostgreSQL connection string required.");

        var gate = CreateGate(connectionString);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            gate.TryAcquireAsync(cts.Token));

        // Cancelled acquire must not leave a held advisory lock.
        await using var lease = await gate.TryAcquireAsync();
        Assert.NotNull(lease);

        // Contended acquire returns null and disposes its connection (no throw / hang).
        var otherGate = CreateGate(connectionString);
        var contended = await otherGate.TryAcquireAsync();
        Assert.Null(contended);

        await lease!.DisposeAsync();
        await using var afterRelease = await otherGate.TryAcquireAsync();
        Assert.NotNull(afterRelease);
    }

    private static PostgresResolveInactiveTicketsGate CreateGate(string connectionString) =>
        new(connectionString, NullLogger<PostgresResolveInactiveTicketsGate>.Instance);
}
