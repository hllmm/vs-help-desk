using System.Transactions;
using VSHelpDesk.Application.Abstractions.Authentication;
using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Application.Abstractions.Persistence.Repositories;
using VSHelpDesk.Application.Common.Exceptions;
using VSHelpDesk.Application.Features.Users.CreateUser;
using VSHelpDesk.Application.Features.Users.GetUsers;
using VSHelpDesk.Domain.Entities;

namespace VSHelpDesk.Application.Features.Users.UpdateUser;

public sealed class UpdateUserHandler(
    IUserRepository userRepository,
    IUnitOfWork unitOfWork,
    IApplicationDbContext? dbContext = null,
    ICurrentUserService? currentUserService = null,
    TimeProvider? timeProvider = null)
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

            if (dbContext is not null)
            {
                await dbContext.ExecuteSqlRawAsync(
                    "SELECT pg_advisory_xact_lock(6220394968519887181);",
                    cancellationToken);
            }

            var user = await userRepository.GetByIdAsync(command.Id, cancellationToken)
                ?? throw new NotFoundException(nameof(User), command.Id);

            LastAdminGuard.EnsureCanDemoteOrDeactivate(
                userRepository.GetListQueryable(),
                command.Id,
                role,
                command.IsActive);

            var beforeRole = user.Role.ToString();
            var beforeIsActive = user.IsActive;

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

            var afterRole = user.Role.ToString();
            var afterIsActive = user.IsActive;

            userRepository.Update(user);

            if (dbContext is not null
                && currentUserService?.UserId is Guid actorId2
                && actorId2 != Guid.Empty)
            {
                var now = (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime;
                if (!string.Equals(beforeRole, afterRole, StringComparison.Ordinal))
                {
                    dbContext.Add(new UserAuditEvent(
                        actorId2,
                        user.Id,
                        "RoleChanged",
                        beforeRole,
                        afterRole,
                        null,
                        null,
                        now,
                        null));
                }

                if (beforeIsActive != afterIsActive)
                {
                    dbContext.Add(new UserAuditEvent(
                        actorId2,
                        user.Id,
                        "ActiveChanged",
                        null,
                        null,
                        beforeIsActive,
                        afterIsActive,
                        now,
                        null));
                }
            }

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
