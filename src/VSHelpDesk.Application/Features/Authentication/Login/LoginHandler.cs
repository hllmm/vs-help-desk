using VSHelpDesk.Application.Abstractions.Authentication;
using VSHelpDesk.Application.Abstractions.Persistence.Repositories;
using VSHelpDesk.Application.Common.Localization;
using VSHelpDesk.Application.Common.Models;

namespace VSHelpDesk.Application.Features.Authentication.Login;

public sealed class LoginHandler(
    IUserRepository userRepository,
    IUnitOfWork unitOfWork,
    IPasswordHasher passwordHasher,
    ITokenService tokenService,
    TimeProvider timeProvider,
    IMessageProvider? messages = null)
{
    private readonly IMessageProvider _messages = messages ?? FallbackMessageProvider.Instance;

    public async Task<Result<LoginResult>> HandleAsync(LoginCommand command, CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByUsernameAsync(command.Username, cancellationToken);
        var passwordIsValid = passwordHasher.Verify(command.Password, user?.PasswordHash);

        // BR-015: inactive users receive the same safe response as invalid credentials.
        if (user is null || !user.IsActive || !passwordIsValid)
        {
            return Result.Failure<LoginResult>(_messages.Get(MessageKeys.Auth.InvalidCredentials));
        }

        user.RecordLogin(timeProvider.GetUtcNow().UtcDateTime);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var accessToken = tokenService.CreateToken(user);
        return Result.Success(new LoginResult(
            accessToken,
            user.Id,
            user.FullName,
            user.Username,
            user.Role.ToString()));
    }
}
