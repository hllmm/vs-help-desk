using VSHelpDesk.Application.Abstractions.Authentication;
using VSHelpDesk.Application.Abstractions.Persistence.Repositories;
using VSHelpDesk.Application.Common.Exceptions;
using VSHelpDesk.Application.Features.Users.CreateUser;
using VSHelpDesk.Domain.Entities;

namespace VSHelpDesk.Application.Features.Users.SetUserPassword;

public sealed class SetUserPasswordHandler(
    IUserRepository userRepository,
    IUnitOfWork unitOfWork,
    IPasswordHasher passwordHasher)
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
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
