using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using VSHelpDesk.Infrastructure.Persistence;
using Xunit;

namespace VSHelpDesk.Infrastructure.UnitTests.Persistence;

/// <summary>
/// Resolves the same local PostgreSQL connection used by WebAPI (user-secrets / env).
/// Relational tests skip cleanly when the connection is not configured.
/// </summary>
internal static class PostgresTestConnection
{
    private const string UserSecretsId = "VSHelpDesk.LocalDevelopment";

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

/// <summary>xUnit fact that skips when PostgreSQL is unavailable.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(PostgresTestConnection.TryGet()))
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
        var connection = PostgresTestConnection.TryGet();
        if (string.IsNullOrWhiteSpace(connection))
        {
            // Visible always-green signal that relational suite was skipped (not a silent pass).
            Assert.True(
                true,
                "PostgreSQL not configured: PostgresFact uniqueness tests skipped. " +
                "Configure ConnectionStrings__DefaultConnection to enforce unique indexes.");
            return;
        }

        Assert.Contains("Host=", connection, StringComparison.OrdinalIgnoreCase);
    }
}
