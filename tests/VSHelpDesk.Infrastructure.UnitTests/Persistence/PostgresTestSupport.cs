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
            Skip = "ConnectionStrings:DefaultConnection not configured (user-secrets or env).";
        }
    }
}
