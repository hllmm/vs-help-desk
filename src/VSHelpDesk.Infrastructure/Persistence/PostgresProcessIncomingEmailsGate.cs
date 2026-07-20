using Microsoft.Extensions.Logging;
using Npgsql;
using VSHelpDesk.Application.Features.MailProcessing.ProcessIncomingEmails;

namespace VSHelpDesk.Infrastructure.Persistence;

/// <summary>
/// PostgreSQL session advisory-lock gate for process-incoming-emails single-flight.
/// Each successful lease owns a dedicated open connection until disposed.
/// </summary>
public sealed class PostgresProcessIncomingEmailsGate : IProcessIncomingEmailsGate
{
    public const long AdvisoryLockKey = 6220394968519887180L;

    private readonly string connectionString;
    private readonly ILogger<PostgresProcessIncomingEmailsGate> logger;

    public PostgresProcessIncomingEmailsGate(
        string connectionString,
        ILogger<PostgresProcessIncomingEmailsGate> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentNullException.ThrowIfNull(logger);

        this.connectionString = connectionString;
        this.logger = logger;
    }

    public async Task<IProcessIncomingEmailsLease?> TryAcquireAsync(
        CancellationToken cancellationToken = default)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT pg_try_advisory_lock(@key);";
        command.Parameters.Add(new NpgsqlParameter<long>("key", AdvisoryLockKey));

        var acquired = (bool)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
        if (!acquired)
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            return null;
        }

        return new PostgresLease(connection, logger);
    }

    private sealed class PostgresLease(
        NpgsqlConnection connection,
        ILogger logger) : IProcessIncomingEmailsLease
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
                    "Failed to release process-incoming-emails advisory lock");
            }
            finally
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
