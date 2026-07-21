using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace VSHelpDesk.WebAPI.IntegrationTests.Support;

/// <summary>
/// Injects non-committed test secrets so Development appsettings can stay empty.
/// Uses <see cref="IWebHostBuilder.UseSetting"/> so values win over process environment variables.
/// </summary>
public sealed class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string TestSigningKey = "integration-test-signing-key-32-bytes!!";
    public const string TestJobsApiKey = "integration-test-jobs-api-key-32!!";
    public const string TestSeedPassword = "IntegrationSeedPassword1!";
    public const string TestAdminPassword = TestSeedPassword;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Auth:SigningKey", TestSigningKey);
        builder.UseSetting("Jobs:ApiKey", TestJobsApiKey);
        builder.UseSetting("SeedUser:Enabled", "true");
        builder.UseSetting("SeedUser:Password", TestSeedPassword);
        builder.UseSetting("SeedUser:Username", "support");
        builder.UseSetting("SeedUser:FullName", "Local Support User");
        builder.UseSetting("SeedUser:Email", "support@vshelpdesk.local");
        builder.UseSetting("SeedAdmin:Enabled", "true");
        builder.UseSetting("SeedAdmin:Password", TestAdminPassword);
        builder.UseSetting("SeedAdmin:Username", "admin");
        builder.UseSetting("SeedAdmin:FullName", "Local Admin User");
        builder.UseSetting("SeedAdmin:Email", "admin@vshelpdesk.local");
    }
}
