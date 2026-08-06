using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace VSHelpDesk.Infrastructure.Persistence;

internal enum DatabaseProviderKind
{
    Postgres,
    SqlServer,
    Sqlite,
    InMemory
}

internal static class DatabaseProviderConfiguration
{
    internal static DatabaseProviderKind Resolve(
        string? configuredProvider,
        string? connectionString)
    {
        if (!string.IsNullOrWhiteSpace(configuredProvider))
        {
            return configuredProvider.Trim().ToUpperInvariant() switch
            {
                "POSTGRES" or "POSTGRESQL" or "NPGSQL" => DatabaseProviderKind.Postgres,
                "SQLSERVER" or "MSSQL" => DatabaseProviderKind.SqlServer,
                "SQLITE" => DatabaseProviderKind.Sqlite,
                "INMEMORY" or "IN-MEMORY" => DatabaseProviderKind.InMemory,
                _ => throw new InvalidOperationException(
                    $"Unsupported Database:Provider value '{configuredProvider}'. " +
                    "Supported values: Postgres, SqlServer, Sqlite, InMemory.")
            };
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return DatabaseProviderKind.Postgres;
        }

        if (connectionString.Equals("InMemory", StringComparison.OrdinalIgnoreCase) ||
            connectionString.Contains("InMemory", StringComparison.OrdinalIgnoreCase))
        {
            return DatabaseProviderKind.InMemory;
        }

        if (connectionString.Contains("Host=", StringComparison.OrdinalIgnoreCase) ||
            connectionString.Contains("Username=", StringComparison.OrdinalIgnoreCase))
        {
            return DatabaseProviderKind.Postgres;
        }

        // SQL Server also accepts the key "Data Source", so provider-specific
        // markers must be checked before the SQLite fallback.
        if (connectionString.Contains("Server=", StringComparison.OrdinalIgnoreCase) ||
            connectionString.Contains("Initial Catalog=", StringComparison.OrdinalIgnoreCase) ||
            connectionString.Contains("Trusted_Connection=", StringComparison.OrdinalIgnoreCase) ||
            connectionString.Contains("Integrated Security=", StringComparison.OrdinalIgnoreCase) ||
            connectionString.Contains("TrustServerCertificate=", StringComparison.OrdinalIgnoreCase) ||
            connectionString.Contains("MultipleActiveResultSets=", StringComparison.OrdinalIgnoreCase))
        {
            return DatabaseProviderKind.SqlServer;
        }

        if (connectionString.StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase) ||
            connectionString.EndsWith(".db", StringComparison.OrdinalIgnoreCase) ||
            connectionString.Contains(":memory:", StringComparison.OrdinalIgnoreCase))
        {
            return DatabaseProviderKind.Sqlite;
        }

        return DatabaseProviderKind.Postgres;
    }

    internal static string ResolveConnectionString(
        DatabaseProviderKind provider,
        string? connectionString)
    {
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            return connectionString;
        }

        return provider switch
        {
            DatabaseProviderKind.InMemory => "VSHelpDeskDb",
            DatabaseProviderKind.Sqlite => "Data Source=vshelpdesk.db",
            _ => throw new InvalidOperationException(
                $"ConnectionStrings:DefaultConnection is required for database provider '{provider}'.")
        };
    }

    internal static void Configure(
        DbContextOptionsBuilder options,
        DatabaseProviderKind provider,
        string connectionString,
        string? migrationsAssembly = null)
    {
        switch (provider)
        {
            case DatabaseProviderKind.SqlServer:
                if (string.IsNullOrWhiteSpace(migrationsAssembly))
                {
                    options.UseSqlServer(connectionString, o => o.EnableRetryOnFailure());
                }
                else
                {
                    options.UseSqlServer(
                        connectionString,
                        builder =>
                        {
                            builder.MigrationsAssembly(migrationsAssembly);
                            builder.EnableRetryOnFailure();
                        });
                }

                options.ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));
                break;

            case DatabaseProviderKind.Sqlite:
                if (string.IsNullOrWhiteSpace(migrationsAssembly))
                {
                    options.UseSqlite(connectionString);
                }
                else
                {
                    options.UseSqlite(
                        connectionString,
                        builder => builder.MigrationsAssembly(migrationsAssembly));
                }

                options.ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));
                break;

            case DatabaseProviderKind.InMemory:
                options.UseInMemoryDatabase(connectionString);
                options.ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));
                break;

            default:
                if (string.IsNullOrWhiteSpace(migrationsAssembly))
                {
                    options.UseNpgsql(connectionString);
                }
                else
                {
                    options.UseNpgsql(
                        connectionString,
                        builder => builder.MigrationsAssembly(migrationsAssembly));
                }
                break;
        }
    }
}
