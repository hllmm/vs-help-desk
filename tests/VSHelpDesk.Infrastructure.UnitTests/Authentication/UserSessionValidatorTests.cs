using Microsoft.EntityFrameworkCore;
using VSHelpDesk.Domain.Entities;
using VSHelpDesk.Domain.Enums;
using VSHelpDesk.Infrastructure.Authentication;
using VSHelpDesk.Infrastructure.Persistence;

namespace VSHelpDesk.Infrastructure.UnitTests.Authentication;

public sealed class UserSessionValidatorTests
{
    [Fact]
    public async Task IsCurrentAsync_ActiveMatchingUser_ReturnsTrue()
    {
        await using var db = CreateContext();
        var user = CreateUser();
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var validator = new UserSessionValidator(db);

        var result = await validator.IsCurrentAsync(
            user.Id,
            user.SecurityVersion,
            user.Role.ToString(),
            CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task IsCurrentAsync_MismatchedVersionOrRole_ReturnsFalse()
    {
        await using var db = CreateContext();
        var user = CreateUser();
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var validator = new UserSessionValidator(db);

        Assert.False(await validator.IsCurrentAsync(
            user.Id,
            user.SecurityVersion + 1,
            user.Role.ToString(),
            CancellationToken.None));
        Assert.False(await validator.IsCurrentAsync(
            user.Id,
            user.SecurityVersion,
            UserRole.Admin.ToString(),
            CancellationToken.None));
        Assert.False(await validator.IsCurrentAsync(
            user.Id,
            user.SecurityVersion,
            "support",
            CancellationToken.None));
    }

    [Fact]
    public async Task IsCurrentAsync_InactiveOrMissingUser_ReturnsFalse()
    {
        await using var db = CreateContext();
        var user = CreateUser();
        user.Deactivate();
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var validator = new UserSessionValidator(db);

        Assert.False(await validator.IsCurrentAsync(
            user.Id,
            user.SecurityVersion,
            user.Role.ToString(),
            CancellationToken.None));
        Assert.False(await validator.IsCurrentAsync(
            Guid.NewGuid(),
            1,
            UserRole.Support.ToString(),
            CancellationToken.None));
    }

    private static User CreateUser() =>
        new("Active User", "active.user", "active@example.test", "hash", UserRole.Support);

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new ApplicationDbContext(options);
    }
}
