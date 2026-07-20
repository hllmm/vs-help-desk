using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Domain.Entities;
using VSHelpDesk.Infrastructure;
using VSHelpDesk.Infrastructure.Persistence;

namespace VSHelpDesk.Infrastructure.UnitTests.Persistence;

public sealed class ApplicationDbContextTests
{
    [Fact]
    public void Model_MapsOnlyUserWithRequiredConstraints()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=metadata_test;Username=test_user")
            .Options;
        using var context = new ApplicationDbContext(options);

        var entityType = Assert.Single(context.Model.GetEntityTypes());
        Assert.Equal(typeof(User), entityType.ClrType);
        Assert.Equal("Users", entityType.GetTableName());
        Assert.Equal(
            [nameof(User.Id)],
            entityType.FindPrimaryKey()!.Properties.Select(property => property.Name));

        var fullName = entityType.FindProperty(nameof(User.FullName))!;
        var username = entityType.FindProperty(nameof(User.Username))!;
        var email = entityType.FindProperty(nameof(User.Email))!;
        var passwordHash = entityType.FindProperty(nameof(User.PasswordHash))!;
        var isActive = entityType.FindProperty(nameof(User.IsActive))!;
        var createdAt = entityType.FindProperty(nameof(User.CreatedAt))!;
        var lastLoginAt = entityType.FindProperty(nameof(User.LastLoginAt))!;

        Assert.Equal(200, fullName.GetMaxLength());
        Assert.Equal(100, username.GetMaxLength());
        Assert.Equal(255, email.GetMaxLength());
        Assert.False(fullName.IsNullable);
        Assert.False(username.IsNullable);
        Assert.False(email.IsNullable);
        Assert.False(passwordHash.IsNullable);
        Assert.False(isActive.IsNullable);
        Assert.False(createdAt.IsNullable);
        Assert.True(lastLoginAt.IsNullable);
        Assert.Equal("timestamp with time zone", createdAt.GetColumnType());
        Assert.Equal("timestamp with time zone", lastLoginAt.GetColumnType());
        Assert.Contains(
            entityType.GetIndexes(),
            index => index.IsUnique &&
                index.Properties.Select(property => property.Name).SequenceEqual([nameof(User.Username)]));
    }

    [Fact]
    public void AddInfrastructure_MissingConnectionString_ThrowsClearConfigurationError()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(
            () => services.AddInfrastructure(configuration));

        Assert.Contains("ConnectionStrings:DefaultConnection", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddInfrastructure_ResolvesSameScopedConcreteAndAbstractionContext()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=metadata_test;Username=test_user"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);
        using var serviceProvider = services.BuildServiceProvider();
        using var scope = serviceProvider.CreateScope();

        var concrete = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var abstraction = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        Assert.Same(concrete, abstraction);
    }

    [Fact]
    public void ApplicationDbContextFactory_MissingEnvironmentConnection_ThrowsClearConfigurationError()
    {
        const string environmentKey = "ConnectionStrings__DefaultConnection";
        var originalValue = Environment.GetEnvironmentVariable(environmentKey);

        try
        {
            Environment.SetEnvironmentVariable(environmentKey, null);
            var factory = new ApplicationDbContextFactory();

            var exception = Assert.Throws<InvalidOperationException>(
                () => factory.CreateDbContext([]));

            Assert.Contains("ConnectionStrings:DefaultConnection", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(environmentKey, originalValue);
        }
    }
}
