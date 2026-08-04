using System.Transactions;
using VSHelpDesk.Application.Abstractions.Persistence.Repositories;
using VSHelpDesk.Application.Common.Exceptions;
using VSHelpDesk.Application.Features.Users.CreateUser;
using VSHelpDesk.Application.Features.Users.GetUsers;
using VSHelpDesk.Domain.Entities;

namespace VSHelpDesk.Application.Features.Users.UpdateUser;

public sealed class UpdateUserHandler(
    IUserRepository userRepository,
    IUnitOfWork unitOfWork)
{
    private static readonly SemaphoreSlim AdminUpdateLock = new(1, 1);
    public async Task<UserListItemDto> HandleAsync(
        UpdateUserCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var fullName = CreateUserHandler.ValidateFullName(command.FullName);
        var email = CreateUserHandler.ValidateEmail(command.Email);
        var role = CreateUserHandler.ParseRole(command.Role);

        await AdminUpdateLock.WaitAsync(cancellationToken);
        try
        {
            using var scope = new TransactionScope(
                TransactionScopeOption.Required,
                new TransactionOptions { IsolationLevel = IsolationLevel.Serializable },
                TransactionScopeAsyncFlowOption.Enabled);

            var user = await userRepository.GetByIdAsync(command.Id, cancellationToken)
                ?? throw new NotFoundException(nameof(User), command.Id);

            LastAdminGuard.EnsureCanDemoteOrDeactivate(
                userRepository.GetListQueryable(),
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

            userRepository.Update(user);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            scope.Complete();

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
        finally
        {
            AdminUpdateLock.Release();
        }
    }
}
