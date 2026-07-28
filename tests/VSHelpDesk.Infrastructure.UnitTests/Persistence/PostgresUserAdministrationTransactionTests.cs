using Microsoft.EntityFrameworkCore;
using Npgsql;
using VSHelpDesk.Application.Common.Exceptions;
using VSHelpDesk.Application.Features.Users;
using VSHelpDesk.Domain.Entities;
using VSHelpDesk.Domain.Enums;
using VSHelpDesk.Infrastructure.Persistence;

namespace VSHelpDesk.Infrastructure.UnitTests.Persistence;

public sealed class PostgresUserAdministrationTransactionTests
{
    [PostgresFact]
    public async Task ConcurrentAdminDemotions_CannotBothCommit()
    {
        var rootConnection = PostgresTestConnection.TryGet()
            ?? throw new InvalidOperationException(
                "PostgreSQL connection is required.");
        var databaseName =
            $"vshd_admin_tx_{Guid.NewGuid():N}";
        var databaseConnection = await CreateDatabaseAsync(
            rootConnection,
            databaseName);

        try
        {
            Guid firstAdminId;
            Guid secondAdminId;
            await using (var setup = CreateContext(databaseConnection))
            {
                await setup.Database.EnsureCreatedAsync();
                var first = CreateAdmin("first-admin");
                var second = CreateAdmin("second-admin");
                setup.Users.AddRange(first, second);
                await setup.SaveChangesAsync();
                firstAdminId = first.Id;
                secondAdminId = second.Id;
            }

            var readyCount = 0;
            var bothReady = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);

            var firstAttempt = DemoteAsync(firstAdminId);
            var secondAttempt = DemoteAsync(secondAdminId);
            var results = await Task.WhenAll(
                CaptureAsync(firstAttempt),
                CaptureAsync(secondAttempt));

            Assert.Equal(1, results.Count(exception => exception is null));
            Assert.Contains(
                results,
                exception => exception is OptimisticConcurrencyException);

            await using var verification = CreateContext(databaseConnection);
            var activeAdmins = await verification.Users.CountAsync(
                user => user.Role == UserRole.Admin && user.IsActive);
            Assert.Equal(1, activeAdmins);

            async Task DemoteAsync(Guid userId)
            {
                await using var db = CreateContext(databaseConnection);
                var transaction =
                    new PostgresUserAdministrationTransaction(db);
                await transaction.ExecuteAsync(
                    async cancellationToken =>
                    {
                        var target = await db.Users.SingleAsync(
                            user => user.Id == userId,
                            cancellationToken);
                        LastAdminGuard.EnsureCanDemoteOrDeactivate(
                            db.Users,
                            target.Id,
                            UserRole.Support,
                            newIsActive: true);

                        if (Interlocked.Increment(ref readyCount) == 2)
                        {
                            bothReady.TrySetResult();
                        }

                        await bothReady.Task.WaitAsync(
                            TimeSpan.FromSeconds(10),
                            cancellationToken);
                        target.AssignRole(UserRole.Support);
                        await db.SaveChangesAsync(cancellationToken);
                        return true;
                    },
                    CancellationToken.None);
            }
        }
        finally
        {
            await DropDatabaseAsync(rootConnection, databaseName);
        }
    }

    private static User CreateAdmin(string username) =>
        new(
            username,
            username,
            $"{username}@example.test",
            "hash",
            UserRole.Admin);

    private static ApplicationDbContext CreateContext(
        string connectionString)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new ApplicationDbContext(options);
    }

    private static async Task<Exception?> CaptureAsync(Task operation)
    {
        try
        {
            await operation;
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static async Task<string> CreateDatabaseAsync(
        string rootConnection,
        string databaseName)
    {
        var adminBuilder = new NpgsqlConnectionStringBuilder(rootConnection)
        {
            Database = "postgres",
            Pooling = false
        };
        await using var connection =
            new NpgsqlConnection(adminBuilder.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE \"{databaseName}\"";
        await command.ExecuteNonQueryAsync();

        var databaseBuilder =
            new NpgsqlConnectionStringBuilder(rootConnection)
            {
                Database = databaseName,
                Pooling = false
            };
        return databaseBuilder.ConnectionString;
    }

    private static async Task DropDatabaseAsync(
        string rootConnection,
        string databaseName)
    {
        var adminBuilder = new NpgsqlConnectionStringBuilder(rootConnection)
        {
            Database = "postgres",
            Pooling = false
        };
        await using var connection =
            new NpgsqlConnection(adminBuilder.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)";
        await command.ExecuteNonQueryAsync();
    }
}
