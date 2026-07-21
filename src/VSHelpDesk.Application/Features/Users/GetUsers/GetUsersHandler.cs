using VSHelpDesk.Application.Abstractions.Persistence;

namespace VSHelpDesk.Application.Features.Users.GetUsers;

public sealed class GetUsersHandler(IApplicationDbContext applicationDbContext)
{
    public Task<IReadOnlyList<UserListItemDto>> HandleAsync(
        CancellationToken cancellationToken = default)
    {
        // Sync materialization matches other Application list handlers (e.g. GetParameters).
        var rows = applicationDbContext.Users
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
