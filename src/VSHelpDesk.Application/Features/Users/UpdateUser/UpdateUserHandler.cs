using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Application.Common.Exceptions;
using VSHelpDesk.Application.Features.Users.CreateUser;
using VSHelpDesk.Application.Features.Users.GetUsers;
using VSHelpDesk.Domain.Entities;

namespace VSHelpDesk.Application.Features.Users.UpdateUser;

public sealed class UpdateUserHandler(IApplicationDbContext applicationDbContext)
{
    public async Task<UserListItemDto> HandleAsync(
        UpdateUserCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var fullName = CreateUserHandler.ValidateFullName(command.FullName);
        var email = CreateUserHandler.ValidateEmail(command.Email);
        var role = CreateUserHandler.ParseRole(command.Role);

        var user = applicationDbContext.Users.FirstOrDefault(candidate => candidate.Id == command.Id)
            ?? throw new NotFoundException(nameof(User), command.Id);

        LastAdminGuard.EnsureCanDemoteOrDeactivate(
            applicationDbContext.Users,
            command.Id,
            role,
            command.IsActive);

        user.UpdateProfile(fullName, email);
        user.AssignRole(role);
        if (command.IsActive)
        {
            user.Activate();
        }
        else
        {
            user.Deactivate();
        }

        await applicationDbContext.SaveChangesAsync(cancellationToken);

        return new UserListItemDto(
            user.Id,
            user.FullName,
            user.Username,
            user.Email,
            user.Role.ToString(),
            user.IsActive,
            user.CreatedAt,
            user.LastLoginAt);
    }
}
