using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VSHelpDesk.Domain.Entities;
using VSHelpDesk.Infrastructure.Authentication;
using VSHelpDesk.Infrastructure.Persistence;
using VSHelpDesk.Infrastructure.Persistence.Seed;

namespace VSHelpDesk.Infrastructure.UnitTests.Persistence;

public sealed class DevelopmentDataSeederTests
{
    [Fact]
    public async Task SeedAsync_CreatesActiveUserWithVerifiableNonPlaintextHash()
    {
        var password = Guid.NewGuid().ToString("N");
        await using var context = CreateContext();
        var passwordHasher = new PasswordHasher();
        var options = new SeedUserOptions
        {
            Enabled = true,
            FullName = "Local Support User",
            Username = "support",
            Email = "support@vshelpdesk.local",
            Password = password
        };
        var seeder = new DevelopmentDataSeeder(context, passwordHasher, Options.Create(options));

        await seeder.SeedAsync();

        var user = await context.Users.SingleAsync();
        Assert.True(user.IsActive);
        Assert.Equal(options.Username, user.Username);
        Assert.Equal(options.Email, user.Email);
        Assert.NotEqual(password, user.PasswordHash);
        Assert.True(passwordHasher.Verify(password, user.PasswordHash));
    }

    [Fact]
    public async Task SeedAsync_ExistingUsername_DoesNotDuplicateAndResyncsPasswordHash()
    {
        var password = Guid.NewGuid().ToString("N");
        await using var context = CreateContext();
        var passwordHasher = new PasswordHasher();
        var options = new SeedUserOptions
        {
            Enabled = true,
            FullName = "Local Support User",
            Username = "support",
            Email = "support@vshelpdesk.local",
            Password = password
        };
        var seeder = new DevelopmentDataSeeder(context, passwordHasher, Options.Create(options));

        await seeder.SeedAsync();
        Assert.Equal(1, await context.Users.CountAsync());

        var rotated = Guid.NewGuid().ToString("N");
        var rotatedOptions = new SeedUserOptions
        {
            Enabled = true,
            FullName = options.FullName,
            Username = options.Username,
            Email = options.Email,
            Password = rotated
        };
        var rotatedSeeder = new DevelopmentDataSeeder(
            context,
            passwordHasher,
            Options.Create(rotatedOptions));
        await rotatedSeeder.SeedAsync();

        var user = await context.Users.SingleAsync();
        Assert.True(passwordHasher.Verify(rotated, user.PasswordHash));
        Assert.False(passwordHasher.Verify(password, user.PasswordHash));
    }

    [Fact]
    public async Task SeedAsync_MissingPassword_ThrowsNamedConfigurationError()
    {
        await using var context = CreateContext();
        var options = new SeedUserOptions
        {
            Enabled = true,
            FullName = "Local Support User",
            Username = "support",
            Email = "support@vshelpdesk.local"
        };
        var seeder = new DevelopmentDataSeeder(context, new PasswordHasher(), Options.Create(options));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => seeder.SeedAsync());

        Assert.Contains("SeedUser:Password", exception.Message, StringComparison.Ordinal);
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new ApplicationDbContext(options);
    }
}
