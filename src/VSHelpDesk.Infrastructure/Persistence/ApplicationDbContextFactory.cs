using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace VSHelpDesk.Infrastructure.Persistence;

public sealed class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    private const string ConnectionEnvironmentKey = "ConnectionStrings__DefaultConnection";
    private const string ProviderEnvironmentKey = "Database__Provider";
    private const string MigrationsAssemblyEnvironmentKey = "Database__MigrationsAssembly";

    public ApplicationDbContext CreateDbContext(string[] args)
    {
        _ = args;
        var configuredProvider = Environment.GetEnvironmentVariable(ProviderEnvironmentKey);
        var configuredConnection = Environment.GetEnvironmentVariable(ConnectionEnvironmentKey);
        var migrationsAssembly = Environment.GetEnvironmentVariable(MigrationsAssemblyEnvironmentKey);

        var provider = DatabaseProviderConfiguration.Resolve(
            configuredProvider,
            configuredConnection);
        var connectionString = DatabaseProviderConfiguration.ResolveConnectionString(
            provider,
            configuredConnection);

        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
        DatabaseProviderConfiguration.Configure(
            optionsBuilder,
            provider,
            connectionString,
            migrationsAssembly);

        return new ApplicationDbContext(optionsBuilder.Options);
    }
}
