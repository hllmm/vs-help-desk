using VSHelpDesk.Application.Abstractions.Authentication;
using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Application.Abstractions.Persistence.Repositories;
using VSHelpDesk.Application.Common.Exceptions;
using VSHelpDesk.Application.Features.Users.CreateUser;
using VSHelpDesk.Domain.Entities;

namespace VSHelpDesk.Application.Features.Users.SetUserPassword;

public sealed class SetUserPasswordHandler(
    IUserRepository userRepository,
    IUnitOfWork unitOfWork,
    IPasswordHasher passwordHasher,
    ICurrentUserService? currentUserService = null,
    TimeProvider? timeProvider = null,
    IApplicationDbContext? dbContext = null)
{
    public async Task HandleAsync(
        SetUserPasswordCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var password = CreateUserHandler.ValidatePassword(command.Password);

        var user = await userRepository.GetByIdAsync(command.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(User), command.Id);

        user.ReplacePasswordHash(passwordHasher.Hash(password));
        userRepository.Update(user);

        if (dbContext is not null
            && currentUserService?.UserId is Guid actorId
            && actorId != Guid.Empty)
        {
            var now = (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime;
            dbContext.Add(new UserAuditEvent(
                actorId,
                user.Id,
                "PasswordReset",
                null,
                null,
                null,
                null,
                now,
                null));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
