using VSHelpDesk.Application.Abstractions.Authentication;
using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Application.Common.Exceptions;
using VSHelpDesk.Application.Features.Users;
using VSHelpDesk.Application.Features.Users.CreateUser;
using VSHelpDesk.Application.Features.Users.GetUsers;
using VSHelpDesk.Domain.Entities;

namespace VSHelpDesk.Application.Features.Users.UpdateUser;

public sealed class UpdateUserHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService,
    TimeProvider timeProvider,
    IUserAdministrationTransaction userAdministrationTransaction)
{
    public async Task<UserListItemDto> HandleAsync(
        UpdateUserCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!currentUserService.IsAuthenticated
            || currentUserService.UserId is not Guid actorUserId
            || actorUserId == Guid.Empty)
        {
            throw new UnauthorizedApplicationException();
        }

        var fullName = CreateUserHandler.ValidateFullName(command.FullName);
        var email = CreateUserHandler.ValidateEmail(command.Email);
        var role = CreateUserHandler.ParseRole(command.Role);

        return await userAdministrationTransaction.ExecuteAsync(
            async transactionCancellationToken =>
            {
                var user = applicationDbContext.Users.FirstOrDefault(
                        candidate => candidate.Id == command.Id)
                    ?? throw new NotFoundException(
                        nameof(User),
                        command.Id);

                LastAdminGuard.EnsureCanDemoteOrDeactivate(
                    applicationDbContext.Users,
                    command.Id,
                    role,
                    command.IsActive);

                var before = UserAdministrationAuditState.Format(user);
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

                applicationDbContext.Add(
                    new UserAdministrationAuditLog(
                        actorUserId,
                        user.Id,
                        "user-updated",
                        timeProvider.GetUtcNow().UtcDateTime,
                        before,
                        UserAdministrationAuditState.Format(user)));
                await applicationDbContext.SaveChangesAsync(
                    transactionCancellationToken);

                return new UserListItemDto(
                    user.Id,
                    user.FullName,
                    user.Username,
                    user.Email,
                    user.Role.ToString(),
                    user.IsActive,
                    user.CreatedAt,
                    user.LastLoginAt);
            },
            cancellationToken);
    }
}
