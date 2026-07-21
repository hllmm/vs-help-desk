using Microsoft.Extensions.DependencyInjection;

namespace VSHelpDesk.Infrastructure.Persistence.Seed;

public static class ServiceProviderExtensions
{
    public static async Task SeedDevelopmentDataAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var seeder = scope.ServiceProvider.GetRequiredService<DevelopmentDataSeeder>();
        await seeder.SeedAsync(cancellationToken);
    }
}
