using Microsoft.Extensions.DependencyInjection;
using VSHelpDesk.Application.Abstractions.Authentication;
using VSHelpDesk.Domain.Entities;
using VSHelpDesk.Infrastructure.Persistence;

namespace VSHelpDesk.WebAPI.IntegrationTests.Support;

public sealed record IntegrationTestUser(
    Guid Id,
    string Username,
    string Password)
{
    public static async Task<IntegrationTestUser> CreateInactiveAsync(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var token = Guid.NewGuid().ToString("N");
        var username = $"inactive-{token[..12]}";
        var email = $"inactive-{token[..12]}@example.test";
        var password = $"Pw-{token[..16]}!";
        var passwordHash = passwordHasher.Hash(password);

        var user = new User(
            fullName: "Inactive Integration User",
            username: username,
            email: email,
            passwordHash: passwordHash);
        user.Deactivate();

        db.Users.Add(user);
        await db.SaveChangesAsync();

        return new IntegrationTestUser(user.Id, username, password);
    }

    public static async Task DeleteAsync(IServiceProvider services, Guid userId)
    {
        ArgumentNullException.ThrowIfNull(services);

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await db.Users.FindAsync(userId);
        if (user is null)
        {
            return;
        }

        db.Users.Remove(user);
        await db.SaveChangesAsync();
    }
}
