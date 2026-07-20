using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VSHelpDesk.Application.Abstractions.Authentication;
using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Domain.Entities;

namespace VSHelpDesk.Infrastructure.Persistence.Seed;

public sealed class DevelopmentDataSeeder(
    IApplicationDbContext applicationDbContext,
    IPasswordHasher passwordHasher,
    IOptions<SeedUserOptions> seedUserOptions)
{
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var options = seedUserOptions.Value;
        if (!options.Enabled)
        {
            return;
        }

        var fullName = Require(options.FullName, "SeedUser:FullName");
        var username = Require(options.Username, "SeedUser:Username");
        var email = Require(options.Email, "SeedUser:Email");
        var password = Require(options.Password, "SeedUser:Password");

        if (await applicationDbContext.Users.AnyAsync(
                user => user.Username == username,
                cancellationToken))
        {
            return;
        }

        applicationDbContext.Add(new User(
            fullName,
            username,
            email,
            passwordHasher.Hash(password)));
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
