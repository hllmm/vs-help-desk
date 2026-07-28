using Microsoft.EntityFrameworkCore;
using VSHelpDesk.Application.Abstractions.Authentication;
using VSHelpDesk.Domain.Enums;
using VSHelpDesk.Infrastructure.Persistence;

namespace VSHelpDesk.Infrastructure.Authentication;

public sealed class UserSessionValidator(ApplicationDbContext db)
    : IUserSessionValidator
{
    public Task<bool> IsCurrentAsync(
        Guid userId,
        int securityVersion,
        string role,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.TryParse<UserRole>(role, ignoreCase: false, out var parsedRole)
            || !Enum.IsDefined(parsedRole))
        {
            return Task.FromResult(false);
        }

        return db.Users
            .AsNoTracking()
            .AnyAsync(
                user => user.Id == userId
                    && user.IsActive
                    && user.SecurityVersion == securityVersion
                    && user.Role == parsedRole,
                cancellationToken);
    }
}
