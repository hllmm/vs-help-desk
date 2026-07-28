using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Application.Common.Exceptions;

namespace VSHelpDesk.Infrastructure.Persistence;

public sealed class PostgresUserAdministrationTransaction(
    ApplicationDbContext db) : IUserAdministrationTransaction
{
    public async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        await using var transaction =
            await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);
        T result;
        try
        {
            result = await operation(cancellationToken);
        }
        catch (Exception exception)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            if (IsSerializationFailure(exception))
            {
                throw CreateConcurrencyException(exception);
            }

            throw;
        }

        try
        {
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (Exception exception)
        {
            if (IsSerializationFailure(exception))
            {
                throw CreateConcurrencyException(exception);
            }

            throw;
        }
    }

    private static bool IsSerializationFailure(Exception exception) =>
        FindPostgresException(exception)?.SqlState
            == PostgresErrorCodes.SerializationFailure;

    private static OptimisticConcurrencyException CreateConcurrencyException(
        Exception exception) =>
        new(
            "The user administration state changed concurrently.",
            exception);

    private static PostgresException? FindPostgresException(
        Exception exception)
    {
        for (Exception? current = exception;
             current is not null;
             current = current.InnerException)
        {
            if (current is PostgresException postgres)
            {
                return postgres;
            }
        }

        return null;
    }
}
