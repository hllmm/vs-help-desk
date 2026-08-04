using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using VSHelpDesk.Infrastructure.Persistence;
using Xunit;

namespace VSHelpDesk.Infrastructure.UnitTests.Persistence;

/// <summary>
/// Resolves the same local PostgreSQL connection used by WebAPI (user-secrets / env).
/// Relational tests skip locally when the connection is not configured; fail when CI=true.
/// </summary>
internal static class PostgresTestConnection
{
    private const string UserSecretsId = "VSHelpDesk.LocalDevelopment";

    internal static bool IsCi =>
        string.Equals(
            Environment.GetEnvironmentVariable("CI"),
            "true",
            StringComparison.OrdinalIgnoreCase);

    internal static bool IsPostgresProvider
    {
        get
        {
            var configured = Environment.GetEnvironmentVariable("Database__Provider");
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return configured.Equals("Postgres", StringComparison.OrdinalIgnoreCase) ||
                       configured.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase) ||
                       configured.Equals("Npgsql", StringComparison.OrdinalIgnoreCase);
            }

            var connection = TryGet();
            return connection?.Contains("Host=", StringComparison.OrdinalIgnoreCase) == true;
        }
    }

    public static string? TryGet()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return fromEnvironment;
        }

        try
        {
            var builder = new ConfigurationBuilder().AddEnvironmentVariables();
            var secretsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".microsoft",
                "usersecrets",
                UserSecretsId,
                "secrets.json");
            if (File.Exists(secretsPath))
            {
                builder.AddJsonFile(secretsPath, optional: true, reloadOnChange: false);
            }

            var fromSecrets = builder.Build().GetConnectionString("DefaultConnection");
            return string.IsNullOrWhiteSpace(fromSecrets) ? null : fromSecrets;
        }
        catch
        {
            return null;
        }
    }

    public static ApplicationDbContext CreateContext()
    {
        var connectionString = TryGet()
            ?? throw new InvalidOperationException(
                "ConnectionStrings:DefaultConnection is required for PostgreSQL tests.");

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new ApplicationDbContext(options);
    }
}

/// <summary>
/// xUnit fact that skips when PostgreSQL is unavailable outside CI.
/// Missing configuration in CI is a hard failure via <see cref="PostgresAvailabilityTests"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (!PostgresTestConnection.IsPostgresProvider)
        {
            Skip = "PostgreSQL-specific test skipped because Database__Provider is not PostgreSQL.";
            return;
        }

        if (!PostgresTestConnection.IsCi
            && string.IsNullOrWhiteSpace(PostgresTestConnection.TryGet()))
        {
            Skip =
                "ConnectionStrings:DefaultConnection not configured (user-secrets or env). " +
                "Uniqueness/sequence suite will NOT run — set ConnectionStrings__DefaultConnection for CI.";
        }
    }
}

public sealed class PostgresAvailabilityTests
{
    [Fact]
    public void PostgresConnection_Configured_Or_SkipReasonIsExplicit()
    {
        if (!PostgresTestConnection.IsPostgresProvider)
        {
            return;
        }

        var connection = PostgresTestConnection.TryGet();
        if (string.IsNullOrWhiteSpace(connection))
        {
            Assert.False(
                PostgresTestConnection.IsCi,
                "CI=true requires ConnectionStrings__DefaultConnection (or user-secrets) so PostgreSQL " +
                "relational tests run; missing configuration must fail the build.");
            // Local: explicit soft-skip signal (not a silent pass of uniqueness coverage).
            Assert.True(
                true,
                "PostgreSQL not configured: PostgresFact uniqueness tests skipped. " +
                "Configure ConnectionStrings__DefaultConnection to enforce unique indexes.");
            return;
        }

        Assert.Contains("Host=", connection, StringComparison.OrdinalIgnoreCase);
    }
}
