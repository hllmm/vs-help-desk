using VSHelpDesk.Application.Abstractions.Persistence.Repositories;

namespace VSHelpDesk.Application.Features.Tickets.GetAssignableUsers;

public sealed class GetAssignableUsersHandler(
    IUserRepository userRepository)
{
    public Task<IReadOnlyList<AssignableUserDto>> HandleAsync(
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;

        IReadOnlyList<AssignableUserDto> users = userRepository.GetListQueryable()
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
