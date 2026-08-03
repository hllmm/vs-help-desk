using VSHelpDesk.Application.Abstractions.Persistence.Repositories;

namespace VSHelpDesk.Application.Features.Users.GetUsers;

public sealed class GetUsersHandler(IUserRepository userRepository)
{
    public Task<IReadOnlyList<UserListItemDto>> HandleAsync(
        CancellationToken cancellationToken = default)
    {
        // Sync materialization matches other Application list handlers (e.g. GetParameters).
        var rows = userRepository.GetListQueryable()
            .OrderBy(user => user.Username)
            .ToList();

        IReadOnlyList<UserListItemDto> items = rows
            .Select(user => new UserListItemDto(
                user.Id,
                user.FullName,
                user.Username,
                user.Email,
                user.Role.ToString(),
                user.IsActive,
                user.CreatedAt,
                user.LastLoginAt))
            .ToList();

        return Task.FromResult(items);
    }
}
