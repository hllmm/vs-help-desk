using VSHelpDesk.Application.Abstractions.Persistence;

namespace VSHelpDesk.Application.Features.Tickets.GetAssignableUsers;

public sealed class GetAssignableUsersHandler(
    IApplicationDbContext applicationDbContext)
{
    public Task<IReadOnlyList<AssignableUserDto>> HandleAsync(
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;

        IReadOnlyList<AssignableUserDto> users = applicationDbContext.Users
            .Where(user => user.IsActive)
            .OrderBy(user => user.FullName)
            .ThenBy(user => user.Username)
            .Select(user => new AssignableUserDto(
                user.Id,
                user.FullName,
                user.Username))
            .ToList();

        return Task.FromResult(users);
    }
}
