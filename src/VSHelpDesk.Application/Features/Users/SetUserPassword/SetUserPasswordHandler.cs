using Microsoft.Extensions.Logging;
using VSHelpDesk.Application.Abstractions.Correlation;
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
    IApplicationDbContext dbContext,
    ICurrentUserService currentUserService,
    TimeProvider? timeProvider = null,
    ILogger<SetUserPasswordHandler>? logger = null,
    ICorrelationIdProvider? correlationIdProvider = null)
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

        if (currentUserService.UserId is Guid actorId && actorId != Guid.Empty)
        {
            var now = (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime;
            var correlationId = correlationIdProvider?.GetCorrelationId();
            dbContext.Add(new UserAuditEvent(
                actorId,
                user.Id,
                "PasswordReset",
                null,
                null,
                null,
                null,
                now,
                correlationId));
        }
        else
        {
            logger?.LogWarning(
                "User audit skipped: missing actor context for {EventType} target {TargetId}",
                "PasswordReset",
                user.Id);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
