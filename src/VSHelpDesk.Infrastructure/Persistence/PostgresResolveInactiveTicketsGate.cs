using Microsoft.Extensions.Logging;
using Npgsql;
using VSHelpDesk.Application.Features.ScheduledJobs.ResolveInactiveTickets;

namespace VSHelpDesk.Infrastructure.Persistence;

/// <summary>
/// PostgreSQL session advisory-lock gate for resolve-inactive-tickets single-flight.
/// Each successful lease owns a dedicated open connection until disposed.
/// Key is distinct from process-incoming-emails so the two jobs can run together.
/// </summary>
public sealed class PostgresResolveInactiveTicketsGate : IResolveInactiveTicketsGate
{
    public const long AdvisoryLockKey = 6220394968519887181L;

    private readonly string connectionString;
    private readonly ILogger<PostgresResolveInactiveTicketsGate> logger;

    public PostgresResolveInactiveTicketsGate(
        string connectionString,
        ILogger<PostgresResolveInactiveTicketsGate> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentNullException.ThrowIfNull(logger);

        this.connectionString = connectionString;
        this.logger = logger;
    }

    public async Task<IResolveInactiveTicketsLease?> TryAcquireAsync(
        CancellationToken cancellationToken = default)
    {
        // Dispose on any failure before successful lock; transfer ownership only after acquire.
        NpgsqlConnection? connection = new(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT pg_try_advisory_lock(@key);";
            command.Parameters.Add(new NpgsqlParameter<long>("key", AdvisoryLockKey));

            var acquired = (bool)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
            if (!acquired)
            {
                return null;
            }

            var lease = new PostgresLease(connection, logger);
            connection = null;
            return lease;
        }
        finally
        {
            if (connection is not null)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private sealed class PostgresLease(
        NpgsqlConnection connection,
        ILogger logger) : IResolveInactiveTicketsLease
    {
        private int disposed;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
            {
                return;
            }

            try
            {
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT pg_advisory_unlock(@key);";
                command.Parameters.Add(new NpgsqlParameter<long>("key", AdvisoryLockKey));
                await command.ExecuteScalarAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Failed to release resolve-inactive-tickets advisory lock");
            }
            finally
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
