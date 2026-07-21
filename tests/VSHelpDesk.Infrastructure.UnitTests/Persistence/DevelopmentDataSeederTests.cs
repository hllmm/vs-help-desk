using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VSHelpDesk.Domain.Enums;
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
        var seeder = CreateSeeder(
            context,
            passwordHasher,
            CreateSeedUserOptions(password),
            disabledAdmin: true);

        await seeder.SeedAsync();

        var user = await context.Users.SingleAsync();
        Assert.True(user.IsActive);
        Assert.Equal("support", user.Username);
        Assert.Equal("support@vshelpdesk.local", user.Email);
        Assert.Equal(UserRole.Support, user.Role);
        Assert.NotEqual(password, user.PasswordHash);
        Assert.True(passwordHasher.Verify(password, user.PasswordHash));
    }

    [Fact]
    public async Task SeedAsync_BothEnabled_CreatesSupportAndAdminWithCorrectRoles()
    {
        var supportPassword = Guid.NewGuid().ToString("N");
        var adminPassword = Guid.NewGuid().ToString("N");
        await using var context = CreateContext();
        var passwordHasher = new PasswordHasher();
        var seeder = CreateSeeder(
            context,
            passwordHasher,
            CreateSeedUserOptions(supportPassword),
            CreateSeedAdminOptions(adminPassword));

        await seeder.SeedAsync();

        var users = await context.Users.OrderBy(u => u.Username).ToListAsync();
        Assert.Equal(2, users.Count);

        var admin = Assert.Single(users, u => u.Username == "admin");
        Assert.Equal(UserRole.Admin, admin.Role);
        Assert.Equal("admin@vshelpdesk.local", admin.Email);
        Assert.True(admin.IsActive);
        Assert.True(passwordHasher.Verify(adminPassword, admin.PasswordHash));

        var support = Assert.Single(users, u => u.Username == "support");
        Assert.Equal(UserRole.Support, support.Role);
        Assert.Equal("support@vshelpdesk.local", support.Email);
        Assert.True(support.IsActive);
        Assert.True(passwordHasher.Verify(supportPassword, support.PasswordHash));
    }

    [Fact]
    public async Task SeedAsync_ExistingUsername_DoesNotDuplicateAndResyncsPasswordHash()
    {
        var password = Guid.NewGuid().ToString("N");
        await using var context = CreateContext();
        var passwordHasher = new PasswordHasher();
        var seeder = CreateSeeder(
            context,
            passwordHasher,
            CreateSeedUserOptions(password),
            disabledAdmin: true);

        await seeder.SeedAsync();
        Assert.Equal(1, await context.Users.CountAsync());

        var rotated = Guid.NewGuid().ToString("N");
        var rotatedSeeder = CreateSeeder(
            context,
            passwordHasher,
            CreateSeedUserOptions(rotated),
            disabledAdmin: true);
        await rotatedSeeder.SeedAsync();

        var user = await context.Users.SingleAsync();
        Assert.Equal(UserRole.Support, user.Role);
        Assert.True(passwordHasher.Verify(rotated, user.PasswordHash));
        Assert.False(passwordHasher.Verify(password, user.PasswordHash));
    }

    [Fact]
    public async Task SeedAsync_ExistingAdmin_ResyncsPasswordAndAssignsAdminRole()
    {
        var password = Guid.NewGuid().ToString("N");
        await using var context = CreateContext();
        var passwordHasher = new PasswordHasher();

        // First seed as admin, then rotate password and re-assert role.
        var seeder = CreateSeeder(
            context,
            passwordHasher,
            disabledUser: true,
            adminOptions: CreateSeedAdminOptions(password));
        await seeder.SeedAsync();

        var rotated = Guid.NewGuid().ToString("N");
        var rotatedSeeder = CreateSeeder(
            context,
            passwordHasher,
            disabledUser: true,
            adminOptions: CreateSeedAdminOptions(rotated));
        await rotatedSeeder.SeedAsync();

        var user = await context.Users.SingleAsync();
        Assert.Equal("admin", user.Username);
        Assert.Equal(UserRole.Admin, user.Role);
        Assert.True(passwordHasher.Verify(rotated, user.PasswordHash));
        Assert.False(passwordHasher.Verify(password, user.PasswordHash));
    }

    [Fact]
    public async Task SeedAsync_MissingPassword_ThrowsNamedConfigurationError()
    {
        await using var context = CreateContext();
        var seeder = CreateSeeder(
            context,
            new PasswordHasher(),
            CreateSeedUserOptions(password: string.Empty),
            disabledAdmin: true);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => seeder.SeedAsync());

        Assert.Contains("SeedUser:Password", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SeedAsync_MissingAdminPassword_ThrowsNamedConfigurationError()
    {
        await using var context = CreateContext();
        var seeder = CreateSeeder(
            context,
            new PasswordHasher(),
            disabledUser: true,
            adminOptions: CreateSeedAdminOptions(password: string.Empty));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => seeder.SeedAsync());

        Assert.Contains("SeedAdmin:Password", exception.Message, StringComparison.Ordinal);
    }

    private static DevelopmentDataSeeder CreateSeeder(
        ApplicationDbContext context,
        PasswordHasher passwordHasher,
        SeedUserOptions? userOptions = null,
        SeedAdminOptions? adminOptions = null,
        bool disabledUser = false,
        bool disabledAdmin = false)
    {
        userOptions ??= disabledUser
            ? new SeedUserOptions { Enabled = false }
            : CreateSeedUserOptions(Guid.NewGuid().ToString("N"));
        adminOptions ??= disabledAdmin
            ? new SeedAdminOptions { Enabled = false }
            : CreateSeedAdminOptions(Guid.NewGuid().ToString("N"));

        return new DevelopmentDataSeeder(
            context,
            passwordHasher,
            Options.Create(userOptions),
            Options.Create(adminOptions));
    }

    private static SeedUserOptions CreateSeedUserOptions(string password) =>
        new()
        {
            Enabled = true,
            FullName = "Local Support User",
            Username = "support",
            Email = "support@vshelpdesk.local",
            Password = password
        };

    private static SeedAdminOptions CreateSeedAdminOptions(string password) =>
        new()
        {
            Enabled = true,
            FullName = "Local Admin User",
            Username = "admin",
            Email = "admin@vshelpdesk.local",
            Password = password
        };

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new ApplicationDbContext(options);
    }
}
