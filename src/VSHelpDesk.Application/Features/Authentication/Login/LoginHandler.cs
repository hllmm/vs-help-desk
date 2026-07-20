using VSHelpDesk.Application.Abstractions.Authentication;
using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Application.Common.Models;

namespace VSHelpDesk.Application.Features.Authentication.Login;

public sealed class LoginHandler(
    IApplicationDbContext applicationDbContext,
    IPasswordHasher passwordHasher,
    ITokenService tokenService,
    TimeProvider timeProvider)
{
    private const string InvalidCredentialsError = "Invalid username or password.";

    public async Task<Result<LoginResult>> HandleAsync(LoginCommand command, CancellationToken cancellationToken)
    {
        var user = applicationDbContext.Users.FirstOrDefault(candidate => candidate.Username == command.Username);
        var passwordIsValid = passwordHasher.Verify(command.Password, user?.PasswordHash);

        if (user is null || !user.IsActive || !passwordIsValid)
        {
            return Result.Failure<LoginResult>(InvalidCredentialsError);
        }

        user.RecordLogin(timeProvider.GetUtcNow().UtcDateTime);
        await applicationDbContext.SaveChangesAsync(cancellationToken);

        var accessToken = tokenService.CreateToken(user);
        return Result.Success(new LoginResult(accessToken, user.Id, user.FullName, user.Username));
    }
}
