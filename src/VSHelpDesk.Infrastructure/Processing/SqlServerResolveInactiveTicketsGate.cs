using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using VSHelpDesk.Application.Features.ScheduledJobs.ResolveInactiveTickets;

namespace VSHelpDesk.Infrastructure.Processing;

/// <summary>
/// SQL Server session applock (<c>sp_getapplock</c>) gate for resolve-inactive-tickets single-flight.
/// </summary>
public sealed class SqlServerResolveInactiveTicketsGate : IResolveInactiveTicketsGate
{
    public const string LockResourceName = "VSHelpDesk_ResolveInactiveTickets_Lock";

    private readonly string _connectionString;
    private readonly ILogger<SqlServerResolveInactiveTicketsGate> _logger;

    public SqlServerResolveInactiveTicketsGate(
        string connectionString,
        ILogger<SqlServerResolveInactiveTicketsGate> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentNullException.ThrowIfNull(logger);

        _connectionString = connectionString;
        _logger = logger;
    }

    public async Task<IResolveInactiveTicketsLease?> TryAcquireAsync(
        CancellationToken cancellationToken = default)
    {
        SqlConnection? connection = new(_connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var command = connection.CreateCommand();
            command.CommandText = "EXEC @result = sp_getapplock @Resource = @resource, @LockMode = 'Exclusive', @LockOwner = 'Session', @LockTimeout = 0;";
            command.Parameters.Add(new SqlParameter("@resource", LockResourceName));

            var resultParam = new SqlParameter("@result", System.Data.SqlDbType.Int)
            {
                Direction = System.Data.ParameterDirection.Output
            };
            command.Parameters.Add(resultParam);

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            var resultCode = (int)(resultParam.Value ?? -1);
            if (resultCode < 0)
            {
                return null;
            }

            var lease = new SqlServerLease(connection, _logger);
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

    private sealed class SqlServerLease(
        SqlConnection connection,
        ILogger logger) : IResolveInactiveTicketsLease
    {
        private int _disposed;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            try
            {
                await using var command = connection.CreateCommand();
                command.CommandText = "EXEC sp_releaseapplock @Resource = @resource, @LockOwner = 'Session';";
                command.Parameters.Add(new SqlParameter("@resource", LockResourceName));
                await command.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Failed to release SQL Server resolve-inactive-tickets applock");
            }
            finally
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
