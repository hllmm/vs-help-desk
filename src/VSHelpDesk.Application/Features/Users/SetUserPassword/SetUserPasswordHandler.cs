using VSHelpDesk.Application.Abstractions.Authentication;
using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Application.Common.Exceptions;
using VSHelpDesk.Application.Features.Users.CreateUser;
using VSHelpDesk.Domain.Entities;

namespace VSHelpDesk.Application.Features.Users.SetUserPassword;

public sealed class SetUserPasswordHandler(
    IApplicationDbContext applicationDbContext,
    IPasswordHasher passwordHasher)
{
    public async Task HandleAsync(
        SetUserPasswordCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var password = CreateUserHandler.ValidatePassword(command.Password);

        var user = applicationDbContext.Users.FirstOrDefault(candidate => candidate.Id == command.Id)
            ?? throw new NotFoundException(nameof(User), command.Id);

        user.ReplacePasswordHash(passwordHasher.Hash(password));
        await applicationDbContext.SaveChangesAsync(cancellationToken);
    }
}
