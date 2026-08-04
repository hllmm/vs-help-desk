using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Application.Abstractions.Security;
using VSHelpDesk.Application.Features.MailProcessing.ProcessIncomingEmails;
using VSHelpDesk.Application.Features.ScheduledJobs.ResolveInactiveTickets;
using VSHelpDesk.Infrastructure.Persistence;
using VSHelpDesk.Infrastructure.Processing;
using VSHelpDesk.Infrastructure.Security;
using Xunit;

namespace VSHelpDesk.Infrastructure.UnitTests.Persistence;

public sealed class DbProviderSwitchingTests
{
    [Theory]
    [InlineData("InMemory", typeof(FallbackDatabaseErrorClassifier), typeof(InProcessProcessIncomingEmailsGate))]
    [InlineData("Sqlite", typeof(FallbackDatabaseErrorClassifier), typeof(InProcessProcessIncomingEmailsGate))]
    [InlineData("SqlServer", typeof(SqlServerDatabaseErrorClassifier), typeof(SqlServerProcessIncomingEmailsGate))]
    [InlineData("Postgres", typeof(PostgresDatabaseErrorClassifier), typeof(PostgresProcessIncomingEmailsGate))]
    public void AddInfrastructure_RegistersCorrectProviderComponents(
        string providerName,
        Type expectedClassifierType,
        Type expectedGateType)
    {
        var inMemoryConnectionString = providerName == "Sqlite"
            ? "Data Source=test.db"
            : providerName == "Postgres"
                ? "Host=localhost;Database=test;Username=postgres;Password=postgres"
                : providerName == "SqlServer"
                    ? "Server=localhost;Database=test;User Id=sa;Password=YourPassword123!;"
                    : "InMemoryDb";

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = inMemoryConnectionString,
                ["Database:Provider"] = providerName,
                ["Auth:SecretKey"] = "SuperSecretKeyOfAtLeast32BytesLengthForTesting!",
                ["Email:SmtpHost"] = "localhost",
                ["Email:SmtpPort"] = "25",
                ["Email:FromAddress"] = "support@example.test",
                ["FileStorage:RootPath"] = "test_storage"
            })
            .Build();

        var services = new ServiceCollection();
        var fakeEnv = new FakeHostEnvironment { ContentRootPath = Directory.GetCurrentDirectory() };
        services.AddSingleton<IHostEnvironment>(fakeEnv);

        // Act
        services.AddInfrastructure(configuration);
        var provider = services.BuildServiceProvider();

        // Assert
        var classifier = provider.GetRequiredService<IDatabaseErrorClassifier>();
        Assert.IsType(expectedClassifierType, classifier);

        var gate = provider.GetRequiredService<IProcessIncomingEmailsGate>();
        Assert.IsType(expectedGateType, gate);

        var sanitizer = provider.GetRequiredService<IHtmlSanitizerService>();
        Assert.IsType<HtmlSanitizerService>(sanitizer);
    }

    [Theory]
    [InlineData("Host=localhost;Database=test;Username=postgres", "Npgsql.EntityFrameworkCore.PostgreSQL")]
    [InlineData("Server=localhost;Database=test;User Id=sa;Password=Password123!", "Microsoft.EntityFrameworkCore.SqlServer")]
    [InlineData("Data Source=localhost;Initial Catalog=test;Integrated Security=true;TrustServerCertificate=true", "Microsoft.EntityFrameworkCore.SqlServer")]
    [InlineData("Data Source=test.db", "Microsoft.EntityFrameworkCore.Sqlite")]
    public void AddInfrastructure_InfersProviderFromConnectionString(
        string connectionString,
        string expectedProviderName)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = connectionString,
                ["Auth:SecretKey"] = "SuperSecretKeyOfAtLeast32BytesLengthForTesting!",
                ["Email:SmtpHost"] = "localhost",
                ["Email:SmtpPort"] = "25",
                ["Email:FromAddress"] = "support@example.test",
                ["FileStorage:RootPath"] = "test_storage"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(new FakeHostEnvironment
        {
            ContentRootPath = Directory.GetCurrentDirectory()
        });

        services.AddInfrastructure(configuration);
        using var serviceProvider = services.BuildServiceProvider();
        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        Assert.Equal(expectedProviderName, context.Database.ProviderName);
    }

    [Fact]
    public void AddInfrastructure_ConfiguresCustomMigrationsAssembly()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Data Source=test.db",
                ["Database:Provider"] = "Sqlite",
                ["Database:MigrationsAssembly"] = "VSHelpDesk.Infrastructure",
                ["Auth:SecretKey"] = "SuperSecretKeyOfAtLeast32BytesLengthForTesting!",
                ["Email:SmtpHost"] = "localhost",
                ["Email:SmtpPort"] = "25",
                ["Email:FromAddress"] = "support@example.test",
                ["FileStorage:RootPath"] = "test_storage"
            })
            .Build();

        var services = new ServiceCollection();
        var fakeEnv = new FakeHostEnvironment { ContentRootPath = Directory.GetCurrentDirectory() };
        services.AddSingleton<IHostEnvironment>(fakeEnv);

        services.AddInfrastructure(configuration);
        var provider = services.BuildServiceProvider();

        var dbContext = provider.GetRequiredService<ApplicationDbContext>();
        Assert.NotNull(dbContext);
    }

    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = string.Empty;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
