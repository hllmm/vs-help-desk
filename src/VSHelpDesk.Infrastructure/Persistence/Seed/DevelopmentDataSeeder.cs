using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VSHelpDesk.Application.Abstractions.Authentication;
using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Domain.Entities;
using VSHelpDesk.Domain.Enums;

namespace VSHelpDesk.Infrastructure.Persistence.Seed;

public sealed class DevelopmentDataSeeder(
    IApplicationDbContext applicationDbContext,
    IPasswordHasher passwordHasher,
    IOptions<SeedUserOptions> seedUserOptions,
    IOptions<SeedAdminOptions> seedAdminOptions)
{
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var userOptions = seedUserOptions.Value;
        if (userOptions.Enabled)
        {
            await EnsureUserAsync(
                Require(userOptions.FullName, "SeedUser:FullName"),
                Require(userOptions.Username, "SeedUser:Username"),
                Require(userOptions.Email, "SeedUser:Email"),
                Require(userOptions.Password, "SeedUser:Password"),
                UserRole.Support,
                cancellationToken);
        }

        var adminOptions = seedAdminOptions.Value;
        if (adminOptions.Enabled)
        {
            await EnsureUserAsync(
                Require(adminOptions.FullName, "SeedAdmin:FullName"),
                Require(adminOptions.Username, "SeedAdmin:Username"),
                Require(adminOptions.Email, "SeedAdmin:Email"),
                Require(adminOptions.Password, "SeedAdmin:Password"),
                UserRole.Admin,
                cancellationToken);
        }
    }

    private async Task EnsureUserAsync(
        string fullName,
        string username,
        string email,
        string password,
        UserRole role,
        CancellationToken cancellationToken)
    {
        var existing = await applicationDbContext.Users
            .FirstOrDefaultAsync(u => u.Username == username, cancellationToken);
        if (existing is not null)
        {
            // Keep local/CI seed password in sync with configuration (user may already exist).
            existing.ReplacePasswordHash(passwordHasher.Hash(password));
            existing.AssignRole(role); // deterministic local roles
            await applicationDbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        applicationDbContext.Add(new User(
            fullName,
            username,
            email,
            passwordHasher.Hash(password),
            role));
        await applicationDbContext.SaveChangesAsync(cancellationToken);
    }

    private static string Require(string value, string configurationKey)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"The {configurationKey} configuration value is required when development seeding is enabled.");
        }

        return value;
    }
}
