using VSHelpDesk.Application.Abstractions.Authentication;
using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Application.Common.Exceptions;
using VSHelpDesk.Application.Features.Users.CreateUser;
using VSHelpDesk.Domain.Entities;

namespace VSHelpDesk.Application.Features.Users.SetUserPassword;

public sealed class SetUserPasswordHandler(
    IApplicationDbContext applicationDbContext,
    IPasswordHasher passwordHasher,
    ICurrentUserService currentUserService,
    TimeProvider timeProvider)
{
    public async Task HandleAsync(
        SetUserPasswordCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!currentUserService.IsAuthenticated
            || currentUserService.UserId is not Guid actorUserId
            || actorUserId == Guid.Empty)
        {
            throw new UnauthorizedApplicationException();
        }

        var password = CreateUserHandler.ValidatePassword(command.Password);

        var user = applicationDbContext.Users.FirstOrDefault(candidate => candidate.Id == command.Id)
            ?? throw new NotFoundException(nameof(User), command.Id);

        user.ReplacePasswordHash(passwordHasher.Hash(password));
        applicationDbContext.Add(new UserAdministrationAuditLog(
            actorUserId,
            user.Id,
            "user-password-reset",
            timeProvider.GetUtcNow().UtcDateTime));
        await applicationDbContext.SaveChangesAsync(cancellationToken);
    }
}
